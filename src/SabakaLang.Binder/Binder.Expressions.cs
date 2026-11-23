using SabakaLang.AST;
using SabakaLang.AST.Interfaces;
using SabakaLang.AST.Structures;
using SabakaLang.Lexer.Enums;
using SabakaLang.Runtime;

namespace SabakaLang.Binder;

public sealed partial class Binder
{
    // ------------------------------------------------------------------ entry points

    private BoundType BindExpr(IExpr e, bool allowVoid = false, BoundType? expected = null)
    {
        if (!_model.Types.TryGetValue(e, out var type))
        {
            type = BindExprCore(e, expected);
            _model.Types[e] = type;
        }
        if (type.Kind == TypeKind.Void && !allowVoid)
        {
            Error("A void expression cannot be used as a value.", e.Span);
            type = BoundType.Error;
            _model.Types[e] = type;
        }
        return type;
    }

    private BoundType BindExprCoerced(IExpr e, BoundType target, Span span, Use use, string name)
    {
        var t = BindExpr(e, expected: target);
        Coerce(e, t, target, e.Span.Start.Line > 0 ? e.Span : span, use, name);
        return t;
    }

    private static bool IsLvalue(IExpr e) => e is NameExpr or MemberExpr or IndexExpr;

    private void MarkStructCopy(IExpr e)
    {
        if (IsLvalue(e) && !_model.Conversions.ContainsKey(e)) _model.Conversions[e] = Conversion.CloneStruct;
    }

    private void Coerce(IExpr e, BoundType from, BoundType to, Span span, Use use, string name)
    {
        if (from.IsError || to.IsError) return;

        if (!TypeRules.IsAssignable(from, to, out var conv))
        {
            var ctx = use switch
            {
                Use.Init => $" in initializer of '{name}'",
                Use.Assign => $" in assignment to '{name}'",
                Use.Argument => $" in argument '{name}'",
                Use.Return => $" in return value of '{name}'",
                Use.Element => " in array element",
                _ => ""
            };
            Error($"Cannot convert '{from}' to '{to}'{ctx}.", span);
            return;
        }

        if (from.Kind == TypeKind.Null && !to.IsNullable && to.Kind is not (TypeKind.Dynamic or TypeKind.TypeParam or TypeKind.Null))
        {
            Warn(use switch
            {
                Use.Init => $"Variable '{name}' has non-nullable type '{to}' but is assigned null.",
                Use.Assign => $"Cannot assign null to non-nullable '{to}' ('{name}').",
                Use.Argument => $"Passing null for non-nullable parameter '{name}' of type '{to}'.",
                Use.Return => $"Returning null from '{name}' whose return type '{to}' is non-nullable.",
                _ => $"Null used where non-nullable '{to}' is expected."
            }, span);
        }

        if (conv == Conversion.None && to.IsStruct && from.IsStruct) { MarkStructCopy(e); return; }
        if (conv != Conversion.None) _model.Conversions[e] = conv;
    }

    // ------------------------------------------------------------------ dispatcher

    private BoundType BindExprCore(IExpr e, BoundType? expected)
    {
        switch (e)
        {
            case IntLit l: return Const(e, Value.FromInt(l.Value), BoundType.Int);
            case FloatLit l: return Const(e, Value.FromFloat(l.Value), BoundType.Float);
            case StringLit l: return Const(e, Value.FromString(l.Value), BoundType.String);
            case CharLit l: return Const(e, Value.FromChar(l.Value), BoundType.Char);
            case BoolLit l: return Const(e, Value.FromBool(l.Value), BoundType.Bool);
            case NullLit: return BoundType.Null;
            case NameExpr n: return BindName(n);
            case BinaryExpr b: return BindBinary(b);
            case UnaryExpr u: return BindUnary(u);
            case AssignExpr a: return BindAssign(a);
            case CallExpr c: return BindCall(c);
            case MemberExpr m: return BindMember(m);
            case IndexExpr i: return BindIndex(i);
            case ArrayExpr a: return BindArrayLiteral(a, expected);
            case NewExpr n: return BindNew(n);
            case NewArrayExpr na: return BindNewArray(na);
            case TernaryExpr t: return BindTernary(t);
            case CoalesceExpr c: return BindCoalesce(c);
            case InterpolatedStringExpr s:
                foreach (var p in s.Parts) BindExpr(p);
                return BoundType.String;
            case IsExpr i:
            {
                BindExpr(i.Left);
                var t = ResolveTypeRef(i.Right, _scope, i.Span);
                _model.IsTypes[i] = t;
                return BoundType.Bool;
            }
            case SuperExpr s:
                Error("'super' can only be used to call a base class member: super.method() or super(...).", s.Span);
                return BoundType.Error;
            default:
                Error($"Unsupported expression '{e.GetType().Name}'.", e.Span);
                return BoundType.Error;
        }
    }

    private BoundType Const(IExpr e, Value v, BoundType t)
    {
        _model.Constants[e] = v;
        return t;
    }

    // ------------------------------------------------------------------ names

    private BoundType BindName(NameExpr n)
    {
        var sym = LookupName(n.Name);
        switch (sym)
        {
            case null:
                Error(n.Name == "this"
                    ? "'this' can only be used inside a class method."
                    : $"Undefined symbol '{n.Name}'.", n.Span);
                return BoundType.Error;

            case VariableSymbol v:
                _model.Names[n] = v;
                return v.Type;

            case ConstantSymbol c:
                _model.Names[n] = c;
                return Const(n, c.Value, c.Type);

            case FieldSymbol f when _curType is not null:
            {
                _model.Names[n] = f;
                CheckAccess(f, f.Owner, n.Span);
                var (_, view) = FindMember(_curType, ThisType(_curType), n.Name);
                if (f.IsConst && f.Owner is not null) return Const(n, EnsureFieldConst(f), MemberType(f.Type, view, f.Owner));
                return MemberType(f.Type, view, f.Owner);
            }

            case FunctionSymbol or FunctionGroupSymbol:
                Error($"'{n.Name}' is a function; call it with parentheses.", n.Span);
                return BoundType.Error;

            case TypeSymbol or ModuleSymbol:
                Error($"'{n.Name}' is a {(sym is ModuleSymbol ? "module" : "type")}, not a value.", n.Span);
                return BoundType.Error;

            default:
                Error($"'{n.Name}' cannot be used as a value.", n.Span);
                return BoundType.Error;
        }
    }

    // ------------------------------------------------------------------ operators

    private static string OpText(TokenType op) => op switch
    {
        TokenType.Plus => "+", TokenType.Minus => "-", TokenType.Star => "*", TokenType.Slash => "/", TokenType.Percent => "%",
        TokenType.EqualEqual => "==", TokenType.NotEqual => "!=", TokenType.Less => "<", TokenType.Greater => ">",
        TokenType.LessEqual => "<=", TokenType.GreaterEqual => ">=", TokenType.AndAnd => "&&", TokenType.OrOr => "||",
        TokenType.Bang => "!", _ => op.ToString()
    };

    private static bool Loose(BoundType t) => t.IsDynamic || t.IsError || t.Kind == TypeKind.TypeParam;

    private BoundType BindBinary(BinaryExpr b)
    {
        var lt = BindExpr(b.Left);
        var rt = BindExpr(b.Right);
        if (lt.IsError || rt.IsError) return BoundType.Error;

        BoundType? result = null;
        switch (b.Op)
        {
            case TokenType.AndAnd or TokenType.OrOr:
                if ((lt.Kind == TypeKind.Bool || Loose(lt)) && (rt.Kind == TypeKind.Bool || Loose(rt))) result = BoundType.Bool;
                break;

            case TokenType.Plus:
                if (lt.Kind == TypeKind.String || rt.Kind == TypeKind.String ||
                    lt.Kind == TypeKind.Char && rt.Kind == TypeKind.Char)
                    result = BoundType.String;
                else if (lt.IsNumeric && rt.IsNumeric)
                    result = lt.Kind == TypeKind.Float || rt.Kind == TypeKind.Float ? BoundType.Float : BoundType.Int;
                else if (Loose(lt) || Loose(rt)) result = BoundType.Dynamic;
                break;

            case TokenType.Minus or TokenType.Star or TokenType.Slash or TokenType.Percent:
                if (lt.IsNumeric && rt.IsNumeric)
                    result = lt.Kind == TypeKind.Float || rt.Kind == TypeKind.Float ? BoundType.Float : BoundType.Int;
                else if (Loose(lt) && (rt.IsNumeric || Loose(rt)) || Loose(rt) && lt.IsNumeric) result = BoundType.Dynamic;
                break;

            case TokenType.Less or TokenType.Greater or TokenType.LessEqual or TokenType.GreaterEqual:
                if (lt.IsNumeric && rt.IsNumeric || lt.Kind == rt.Kind && lt.Kind is TypeKind.Char or TypeKind.String ||
                    Loose(lt) || Loose(rt))
                    result = BoundType.Bool;
                break;

            case TokenType.EqualEqual or TokenType.NotEqual:
                result = BindEquality(b, lt, rt) ? BoundType.Bool : null;
                break;
        }

        if (result is null)
        {
            Error($"Operator '{OpText(b.Op)}' cannot be applied to '{lt}' and '{rt}'.", b.Span);
            return BoundType.Error;
        }

        if (_model.Constants.TryGetValue(b.Left, out var l) && _model.Constants.TryGetValue(b.Right, out var r) &&
            !_model.Conversions.ContainsKey(b.Left) && !_model.Conversions.ContainsKey(b.Right) && Fold(b.Op, l, r) is { } folded)
            _model.Constants[b] = folded;

        return result;
    }

    private bool BindEquality(BinaryExpr b, BoundType lt, BoundType rt)
    {
        if (Loose(lt) || Loose(rt)) return true;
        if (lt.Kind == TypeKind.Null || rt.Kind == TypeKind.Null) return true;
        if (lt.IsNumeric && rt.IsNumeric) return true;
        if (lt.Kind == TypeKind.Char && rt.Kind == TypeKind.String) { _model.Conversions[b.Left] = Conversion.CharToString; return true; }
        if (lt.Kind == TypeKind.String && rt.Kind == TypeKind.Char) { _model.Conversions[b.Right] = Conversion.CharToString; return true; }
        return TypeRules.IsAssignable(lt, rt, out _) || TypeRules.IsAssignable(rt, lt, out _);
    }

    private static Value? Fold(TokenType op, Value a, Value b)
    {
        try
        {
            if (a.IsNumber && b.IsNumber)
            {
                var ints = a.Type == SabakaType.Int && b.Type == SabakaType.Int;
                switch (op)
                {
                    case TokenType.Plus: return ints ? Value.FromInt(unchecked(a.Int + b.Int)) : Value.FromFloat(a.ToDouble() + b.ToDouble());
                    case TokenType.Minus: return ints ? Value.FromInt(unchecked(a.Int - b.Int)) : Value.FromFloat(a.ToDouble() - b.ToDouble());
                    case TokenType.Star: return ints ? Value.FromInt(unchecked(a.Int * b.Int)) : Value.FromFloat(a.ToDouble() * b.ToDouble());
                    case TokenType.Slash:
                        if (ints) return b.Int == 0 || a.Int == int.MinValue && b.Int == -1 ? null : Value.FromInt(a.Int / b.Int);
                        return Value.FromFloat(a.ToDouble() / b.ToDouble());
                    case TokenType.Percent:
                        if (ints) return b.Int == 0 || b.Int == -1 ? null : Value.FromInt(a.Int % b.Int);
                        return Value.FromFloat(a.ToDouble() % b.ToDouble());
                    case TokenType.Less: return Value.FromBool(a.ToDouble() < b.ToDouble());
                    case TokenType.Greater: return Value.FromBool(a.ToDouble() > b.ToDouble());
                    case TokenType.LessEqual: return Value.FromBool(a.ToDouble() <= b.ToDouble());
                    case TokenType.GreaterEqual: return Value.FromBool(a.ToDouble() >= b.ToDouble());
                }
            }
            switch (op)
            {
                case TokenType.Plus when a.Type is SabakaType.String or SabakaType.Char || b.Type is SabakaType.String or SabakaType.Char:
                    return Value.FromString(a.ToString() + b.ToString());
                case TokenType.EqualEqual: return Value.FromBool(a.Equals(b));
                case TokenType.NotEqual: return Value.FromBool(!a.Equals(b));
                case TokenType.AndAnd when a.Type == SabakaType.Bool && b.Type == SabakaType.Bool: return Value.FromBool(a.Bool && b.Bool);
                case TokenType.OrOr when a.Type == SabakaType.Bool && b.Type == SabakaType.Bool: return Value.FromBool(a.Bool || b.Bool);
                case TokenType.Less or TokenType.Greater or TokenType.LessEqual or TokenType.GreaterEqual
                    when a.Type == b.Type && a.Type is SabakaType.String or SabakaType.Char:
                {
                    var c = string.CompareOrdinal(a.ToString(), b.ToString());
                    return Value.FromBool(op switch
                    {
                        TokenType.Less => c < 0, TokenType.Greater => c > 0, TokenType.LessEqual => c <= 0, _ => c >= 0
                    });
                }
            }
        }
        catch (ArithmeticException) { }
        return null;
    }

    private BoundType BindUnary(UnaryExpr u)
    {
        var t = BindExpr(u.Operand);
        if (t.IsError) return t;

        switch (u.Op)
        {
            case TokenType.Minus when t.IsNumeric || Loose(t):
                if (_model.Constants.TryGetValue(u.Operand, out var c) && !_model.Conversions.ContainsKey(u.Operand))
                    _model.Constants[u] = c.Type == SabakaType.Int ? Value.FromInt(unchecked(-c.Int)) : Value.FromFloat(-c.Float);
                return Loose(t) ? BoundType.Dynamic : t;
            case TokenType.Bang when t.Kind == TypeKind.Bool || Loose(t):
                if (_model.Constants.TryGetValue(u.Operand, out var b)) _model.Constants[u] = Value.FromBool(!b.Bool);
                return BoundType.Bool;
        }

        Error($"Operator '{OpText(u.Op)}' cannot be applied to '{t}'.", u.Span);
        return BoundType.Error;
    }

    private BoundType BindTernary(TernaryExpr t)
    {
        BindCondition(t.Condition);
        var a = BindExpr(t.Then);
        var b = BindExpr(t.Else);
        if (a.IsError || b.IsError) return BoundType.Error;
        var common = TypeRules.CommonType(a, b);
        if (common is null)
        {
            Error($"The branches of '?:' have incompatible types '{a}' and '{b}'.", t.Span);
            return BoundType.Error;
        }
        Coerce(t.Then, a, common, t.Span, Use.Other, "?:");
        Coerce(t.Else, b, common, t.Span, Use.Other, "?:");
        return common;
    }

    private BoundType BindCoalesce(CoalesceExpr c)
    {
        var a = BindExpr(c.Left);
        var b = BindExpr(c.Right);
        if (a.IsError || b.IsError) return BoundType.Error;
        var common = TypeRules.CommonType(a.Kind == TypeKind.Null ? b : a.NonNull, b);
        if (common is null)
        {
            Error($"The operands of '??' have incompatible types '{a}' and '{b}'.", c.Span);
            return BoundType.Error;
        }
        Coerce(c.Left, a, common, c.Span, Use.Other, "??");
        Coerce(c.Right, b, common, c.Span, Use.Other, "??");
        return b.IsNullable || b.Kind == TypeKind.Null ? common : common.NonNull;
    }

    // ------------------------------------------------------------------ assignment

    private BoundType BindAssign(AssignExpr a)
    {
        if (!IsLvalue(a.Target))
        {
            Error("Invalid assignment target.", a.Span);
            BindExpr(a.Value);
            return BoundType.Error;
        }

        var tt = BindExpr(a.Target);
        var targetName = a.Target switch { NameExpr n => n.Name, MemberExpr m => m.Member, _ => "element" };

        if (!tt.IsError)
        {
            switch (a.Target)
            {
                case NameExpr n when _model.Names.TryGetValue(n, out var sym):
                    if (sym is ConstantSymbol || sym is FieldSymbol { IsConst: true } || sym is VariableSymbol { IsConst: true })
                        Error($"Cannot assign to constant '{n.Name}'.", a.Span);
                    break;
                case NameExpr n when _model.Constants.ContainsKey(n):
                    Error($"Cannot assign to constant '{n.Name}'.", a.Span);
                    break;
                case MemberExpr m when _model.Members.TryGetValue(m, out var mb):
                    switch (mb.Kind)
                    {
                        case MemberKind.Length: Error("'length' is read-only.", a.Span); break;
                        case MemberKind.Constant or MemberKind.EnumMember: Error($"Cannot assign to constant '{m.Member}'.", a.Span); break;
                        case MemberKind.ExtensionProperty: Error($"Property '{m.Member}' is read-only.", a.Span); break;
                        case MemberKind.NativeProperty when mb.Symbol is PropertySymbol { Native.CanWrite: false }:
                            Error($"Property '{m.Member}' is read-only.", a.Span); break;
                        case MemberKind.Field when mb.Symbol is FieldSymbol { IsConst: true }:
                            Error($"Cannot assign to constant '{m.Member}'.", a.Span); break;
                    }
                    break;
                case IndexExpr ix when _model.TypeOf(ix.Object).Kind == TypeKind.String:
                    Error("Strings are immutable: cannot assign to a character of a string.", a.Span);
                    break;
            }
        }

        BindExprCoerced(a.Value, tt, a.Span, Use.Assign, targetName);
        return tt;
    }

    // ------------------------------------------------------------------ indexing, arrays, new

    private BoundType BindIndex(IndexExpr i)
    {
        var ot = BindExpr(i.Object);
        var it = BindExpr(i.Index);
        if (!it.IsError && !it.IsDynamic && it.Kind != TypeKind.Int)
            Error($"Index must be of type int, got '{it}'.", i.Index.Span);
        if (ot.IsError) return ot;

        switch (ot.Kind)
        {
            case TypeKind.Array: return ot.Element!;
            case TypeKind.String: return BoundType.Char;
            case TypeKind.Dynamic: return BoundType.Dynamic;
            default:
                Error($"Cannot index into a value of type '{ot}'.", i.Span);
                return BoundType.Error;
        }
    }

    private BoundType BindArrayLiteral(ArrayExpr a, BoundType? expected)
    {
        var elemExpected = expected is { Kind: TypeKind.Array } ? expected.Element : null;
        if (a.Elements.Count == 0) return elemExpected is null ? BoundType.ArrayOf(BoundType.Dynamic) : expected!.NonNull;

        var types = a.Elements.Select(el => BindExpr(el, expected: elemExpected)).ToList();
        if (elemExpected is not null)
        {
            for (var k = 0; k < types.Count; k++) Coerce(a.Elements[k], types[k], elemExpected, a.Elements[k].Span, Use.Element, "");
            return expected!.NonNull;
        }

        BoundType? common = types[0].Kind == TypeKind.Null ? BoundType.Null : types[0];
        foreach (var t in types.Skip(1))
        {
            if (common is null) break;
            common = TypeRules.CommonType(common, t);
        }
        if (common is null)
        {
            if (!types.Any(t => t.IsError)) Error("Array elements have incompatible types.", a.Span);
            return BoundType.ArrayOf(BoundType.Error);
        }
        if (common.Kind == TypeKind.Null) common = BoundType.Dynamic;
        for (var k = 0; k < types.Count; k++) Coerce(a.Elements[k], types[k], common, a.Elements[k].Span, Use.Element, "");
        return BoundType.ArrayOf(common);
    }

    private BoundType BindNewArray(NewArrayExpr na)
    {
        var st = BindExpr(na.Size);
        if (!st.IsError && !st.IsDynamic && st.Kind != TypeKind.Int)
            Error($"Array size must be of type int, got '{st}'.", na.Size.Span);
        var elem = ResolveValueType(na.ElementType, _scope, na.Span);
        _model.ArrayElementTypes[na] = elem;
        return BoundType.ArrayOf(elem);
    }

    private BoundType BindNew(NewExpr n)
    {
        var sym = LookupInScope(n.TypeName, _scope);
        if (sym is not TypeSymbol t)
        {
            Error(sym is null ? $"Unknown type '{n.TypeName}'." : $"'{n.TypeName}' is not a class.", n.Span);
            foreach (var a in n.Args) BindExpr(a);
            return BoundType.Error;
        }

        var targs = new List<BoundType>();
        foreach (var s in n.TypeArgs) targs.Add(ResolveTypeText(s, n.Span, _scope));
        if (targs.Count > 0 && targs.Count != t.TypeParams.Count)
        {
            Error(t.TypeParams.Count == 0
                ? $"Type '{t.Name}' is not generic."
                : $"Type '{t.Name}' expects {t.TypeParams.Count} type argument(s), got {targs.Count}.", n.Span);
            targs.Clear();
        }
        var type = BoundType.Of(t, targs);

        switch (t.Kind)
        {
            case SymbolKind.Interface:
            case SymbolKind.Enum:
                Error($"Cannot create an instance of {t.Kind.ToString().ToLowerInvariant()} '{t.Name}'.", n.Span);
                foreach (var a in n.Args) BindExpr(a);
                return BoundType.Error;

            case SymbolKind.Struct:
                if (n.Args.Count > 0) Error($"Struct '{t.Name}' has no constructor; 'new {t.Name}()' takes no arguments.", n.Span);
                foreach (var a in n.Args) BindExpr(a);
                _model.News[n] = new NewBinding(NewKind.Struct, t, null);
                return type;
        }

        if (t.IsNative)
        {
            if (!t.Members.TryGetValue("<ctor>", out var g) || g is not FunctionGroupSymbol group)
            {
                Error($"Native class '{t.Name}' cannot be created with 'new'.", n.Span);
                foreach (var a in n.Args) BindExpr(a);
                return type;
            }
            BindInvocation(group.Overloads, n.Args, n.Span, t.Name, null, out var chosen);
            if (chosen is not null) _model.News[n] = new NewBinding(NewKind.Native, t, chosen);
            return type;
        }

        var ctor = FindConstructor(t);
        if (ctor is null)
        {
            if (n.Args.Count > 0) Error($"Class '{t.Name}' has no constructor but received {n.Args.Count} argument(s).", n.Span);
            foreach (var a in n.Args) BindExpr(a);
        }
        else
        {
            if (!CheckAccess(ctor, ctor.Owner, n.Span)) { }
            var classMap = TypeRules.MapFor(ctor.Owner!.TypeParams, ctor.Owner == t ? targs : BaseArgsFor(t, targs, ctor.Owner));
            BindInvocation([ctor], n.Args, n.Span, t.Name, classMap, out _);
        }
        _model.News[n] = new NewBinding(NewKind.Class, t, ctor);
        return type;
    }

    /// <summary>Type arguments of ancestor <paramref name="ancestor"/> as seen from <paramref name="t"/>&lt;targs&gt;.</summary>
    private static IReadOnlyList<BoundType> BaseArgsFor(TypeSymbol t, IReadOnlyList<BoundType> targs, TypeSymbol ancestor)
    {
        var view = BoundType.Of(t, targs);
        for (var c = t; c is not null && !ReferenceEquals(c, ancestor); c = c.BaseSymbol)
        {
            if (c.BaseType is null) break;
            view = TypeRules.Apply(c.BaseType, TypeRules.MapFor(c.TypeParams, view.Args));
        }
        return view.Args;
    }
}
