using SabakaLang.AST;
using SabakaLang.AST.Interfaces;
using SabakaLang.AST.Structures;
using SabakaLang.Runtime;

namespace SabakaLang.Binder;

public sealed partial class Binder
{
    private enum Use { Init, Assign, Argument, Return, Element, Other }

    // ------------------------------------------------------------------ function / class bodies

    private void BindFunctionBody(FunctionSymbol f, bool declareSelf = false)
    {
        var d = f.Decl;
        if (d is null || f.IsAbstract) return;

        var (savedFn, savedScope, savedType) = (_fn, _scope, _curType);
        var owner = f.Owner;
        _curType = owner;
        var root = TypeParamScope(owner, f);
        _scope = root;
        _fn = new FuncCtx(f, f.ReturnType, root, isEntry: false);
        if (declareSelf) root.TryDeclare(f);

        if (owner is not null)
            root.TryDeclare(new VariableSymbol("this", ThisType(owner)) { Slot = _fn.Alloc(), Span = d.Span, Module = _mod.Name });

        foreach (var p in f.Parameters)
        {
            p.Slot = _fn.Alloc();
            root.TryDeclare(p);
            _symbols.Add(p);
        }

        foreach (var st in d.Body) BindStmt(st);

        f.LocalCount = _fn.Max;
        if (f.ReturnType.Kind != TypeKind.Void && !f.ReturnType.IsError && !f.IsConstructor && !AlwaysReturns(d.Body))
            Error($"Function '{f.Name}' must return a value on all code paths.", d.Span);

        (_fn, _scope, _curType) = (savedFn, savedScope, savedType);
    }

    private void BindTypeBodies(TypeSymbol t)
    {
        if (t.Kind is not (SymbolKind.Class or SymbolKind.Struct)) return;

        // field initialisers run inside a synthetic <init> function (this = slot 0)
        var init = new FunctionSymbol("<init>") { Id = $"{t.Id}.<init>", Owner = t, Module = _mod.Name, Span = t.Span };
        var (savedFn, savedScope, savedType) = (_fn, _scope, _curType);
        _curType = t;
        var root = TypeParamScope(t);
        _scope = root;
        _fn = new FuncCtx(init, BoundType.Void, root, isEntry: false);
        root.TryDeclare(new VariableSymbol("this", ThisType(t)) { Slot = _fn.Alloc(), Module = _mod.Name });

        var any = false;
        foreach (var field in t.Fields)
        {
            if (field.IsConst) { EnsureFieldConst(field); any = true; continue; }
            if (field.Decl?.Init is not { } expr) continue;
            BindExprCoerced(expr, field.Type, field.Span, Use.Init, field.Name);
            any = true;
        }
        init.LocalCount = _fn.Max;
        (_fn, _scope, _curType) = (savedFn, savedScope, savedType);

        if (any)
        {
            t.Initializer = init;
            _mod.Bound.Functions.Add(init);
        }

        if (t.Constructor is { } ctor) BindFunctionBody(ctor);
        foreach (var m in t.Methods) BindFunctionBody(m);
    }

    /// <summary>Evaluates a class-level <c>const</c> on first use (constants may be used before the class body is bound).</summary>
    private Value EnsureFieldConst(FieldSymbol f)
    {
        if (f.ConstEvaluated) return f.ConstValue;
        if (f.ConstEvaluating)
        {
            Error($"Circular constant definition '{f.Name}'.", f.Span);
            return Value.Null;
        }
        f.ConstEvaluating = true;

        if (f.Decl?.Init is not { } init)
        {
            Error($"Constant '{f.Name}' requires a value.", f.Span);
            f.ConstEvaluated = true;
            return Value.Null;
        }

        var (savedFn, savedScope, savedType) = (_fn, _scope, _curType);
        var holder = new FunctionSymbol("<const>") { Owner = f.Owner };
        _curType = f.Owner;
        _scope = TypeParamScope(f.Owner);
        _fn = new FuncCtx(holder, BoundType.Void, _scope, isEntry: false);
        var value = EvaluateConst(init, f.Type, f.Name, f.Span);
        (_fn, _scope, _curType) = (savedFn, savedScope, savedType);

        f.ConstValue = value;
        f.ConstEvaluated = true;
        f.ConstEvaluating = false;
        return value;
    }

    /// <summary>Binds <paramref name="init"/>, requires it to be a compile-time constant, converts it to <paramref name="type"/>.</summary>
    private Value EvaluateConst(IExpr init, BoundType type, string name, Span span)
    {
        BindExprCoerced(init, type, span, Use.Init, name);
        if (!_model.Constants.TryGetValue(init, out var v))
        {
            if (!type.IsError) Error($"The value of constant '{name}' must be a constant expression.", span);
            return Value.Null;
        }
        if (_model.Conversions.TryGetValue(init, out var conv))
        {
            v = conv switch
            {
                Conversion.IntToFloat => Value.FromFloat(v.ToDouble()),
                Conversion.CharToString => Value.FromString(v.Char.ToString()),
                _ => v
            };
        }
        return v;
    }

    // ------------------------------------------------------------------ flow analysis

    private static bool AlwaysReturns(IReadOnlyList<IStmt> stmts) => stmts.Any(AlwaysReturns);

    private static bool AlwaysReturns(IStmt s) => s switch
    {
        ReturnStmt => true,
        IfStmt i => i.Else is not null && AlwaysReturns(i.Then) && AlwaysReturns(i.Else),
        WhileStmt w => IsTrueLiteral(w.Condition) && !HasBreak(w.Body),
        ForStmt f => (f.Condition is null || IsTrueLiteral(f.Condition)) && !HasBreak(f.Body),
        SwitchStmt sw => sw.Cases.Any(c => c.Value is null) && sw.Cases.All(c => AlwaysReturns(c.Body) && !HasBreak(c.Body)),
        _ => false
    };

    private static bool IsTrueLiteral(IExpr e) => e is BoolLit { Value: true };

    /// <summary>True when the statements contain a <c>break</c> that leaves the enclosing loop/switch.</summary>
    private static bool HasBreak(IReadOnlyList<IStmt> stmts) => stmts.Any(HasBreak);

    private static bool HasBreak(IStmt s) => s switch
    {
        BreakStmt => true,
        IfStmt i => HasBreak(i.Then) || i.Else is not null && HasBreak(i.Else),
        _ => false // breaks inside nested loops/switches belong to them
    };

    // ------------------------------------------------------------------ statements

    private void BindBlock(IReadOnlyList<IStmt> stmts)
    {
        var saved = EnterScope();
        foreach (var s in stmts) BindStmt(s);
        ExitScope(saved);
    }

    private void BindStmt(IStmt stmt)
    {
        switch (stmt)
        {
            case VarDecl v: BindVarDecl(v); break;
            case ConstDecl c: BindConstDecl(c); break;
            case ExprStmt e: BindExpr(e.Expr, allowVoid: true); break;
            case ReturnStmt r: BindReturn(r); break;

            case IfStmt i:
                BindCondition(i.Condition);
                BindBlock(i.Then);
                if (i.Else is not null) BindBlock(i.Else);
                break;

            case WhileStmt w:
                BindCondition(w.Condition);
                _fn.LoopDepth++;
                BindBlock(w.Body);
                _fn.LoopDepth--;
                break;

            case ForStmt f: BindFor(f); break;
            case ForeachStmt fe: BindForeach(fe); break;
            case SwitchStmt sw: BindSwitch(sw); break;

            case BreakStmt b:
                if (_fn.LoopDepth == 0 && _fn.SwitchDepth == 0) Error("'break' is only allowed inside a loop or switch.", b.Span);
                break;
            case ContinueStmt c:
                if (_fn.LoopDepth == 0) Error("'continue' is only allowed inside a loop.", c.Span);
                break;

            case FuncDecl fd: BindNestedFunction(fd); break;

            case ClassDecl or InterfaceDecl or StructDecl or EnumDecl:
                Error("Types can only be declared at the top level of a module.", stmt.Span);
                break;
            case ImportStmt imp:
                Error("'import' is only allowed at the top level of a module.", imp.Span);
                break;
            default:
                Error($"Unsupported statement '{stmt.GetType().Name}'.", stmt.Span);
                break;
        }
    }

    private void BindCondition(IExpr cond)
    {
        var t = BindExpr(cond);
        if (!t.IsError && !t.IsDynamic && t.Kind != TypeKind.Bool)
            Error($"Condition must be of type bool, got '{t}'.", cond.Span);
    }

    private void BindVarDecl(VarDecl v)
    {
        var isVar = v.Type is { Name: "var", IsArray: false, IsNullable: false, TypeArgs.Count: 0 } && LookupInScope("var", _scope) is null;
        BoundType declared;

        if (isVar)
        {
            if (v.Init is null)
            {
                Error($"Cannot infer the type of '{v.Name}' without an initializer.", v.Span);
                declared = BoundType.Error;
            }
            else
            {
                var t = BindExpr(v.Init);
                declared = t.Kind switch
                {
                    TypeKind.Null => Fail($"Cannot infer the type of '{v.Name}' from 'null'.", v.Span),
                    TypeKind.Void => Fail($"Cannot assign a void value to '{v.Name}'.", v.Span),
                    _ => t
                };
                if (declared.IsStruct) MarkStructCopy(v.Init);
            }
        }
        else
        {
            declared = ResolveValueType(v.Type, _scope, v.Span);
            if (v.Init is not null) BindExprCoerced(v.Init, declared, v.Span, Use.Init, v.Name);
        }

        var sym = new VariableSymbol(v.Name, declared)
            { Span = v.Span, Access = v.Access, Module = _mod.Name };
        if (ReferenceEquals(_scope, _mod.Scope))
        {
            sym.IsGlobal = true;
            sym.Slot = _globalCount++;
        }
        else sym.Slot = _fn.Alloc();

        Declare(sym, "Variable");
        _model.Declarations[v] = sym;
    }

    private BoundType Fail(string message, Span span)
    {
        Error(message, span);
        return BoundType.Error;
    }

    private void BindConstDecl(ConstDecl c)
    {
        BoundType type;
        if (c.Type is { Name: "var", IsArray: false, TypeArgs.Count: 0 })
        {
            var t = BindExpr(c.Value);
            type = t.Kind is TypeKind.Null or TypeKind.Void ? Fail($"Cannot infer the type of constant '{c.Name}'.", c.Span) : t;
        }
        else type = ResolveValueType(c.Type, _scope, c.Span);

        Value value;
        if (c.Type.Name == "var" && !type.IsError)
        {
            if (!_model.Constants.TryGetValue(c.Value, out value))
                Error($"The value of constant '{c.Name}' must be a constant expression.", c.Span);
        }
        else value = EvaluateConst(c.Value, type, c.Name, c.Span);

        var sym = new ConstantSymbol(c.Name, type, value) { Span = c.Span, Access = c.Access, Module = _mod.Name };
        Declare(sym, "Constant");
        _model.Declarations[c] = sym;
    }

    private void BindReturn(ReturnStmt r)
    {
        if (_fn.IsEntry)
        {
            Error("Cannot use 'return' outside a function.", r.Span);
            if (r.Value is not null) BindExpr(r.Value);
            return;
        }

        var expected = _fn.ReturnType;
        if (r.Value is null)
        {
            if (expected.Kind != TypeKind.Void && !expected.IsError)
                Error($"A return value of type '{expected}' is required.", r.Span);
            return;
        }

        if (expected.Kind == TypeKind.Void)
        {
            Error("Cannot return a value from a void function.", r.Span);
            BindExpr(r.Value);
            return;
        }

        BindExprCoerced(r.Value, expected, r.Span, Use.Return, _fn.Symbol.Name);
    }

    private void BindFor(ForStmt f)
    {
        var saved = EnterScope();
        if (f.Init is not null) BindStmt(f.Init);
        if (f.Condition is not null) BindCondition(f.Condition);
        if (f.Step is not null) BindExpr(f.Step, allowVoid: true);
        _fn.LoopDepth++;
        BindBlock(f.Body);
        _fn.LoopDepth--;
        ExitScope(saved);
    }

    private void BindForeach(ForeachStmt f)
    {
        var collType = BindExpr(f.Collection);
        BoundType elem;
        switch (collType.Kind)
        {
            case TypeKind.Array: elem = collType.Element!; break;
            case TypeKind.String: elem = BoundType.Char; break;
            case TypeKind.Dynamic: elem = BoundType.Dynamic; break;
            case TypeKind.Error: elem = BoundType.Error; break;
            default:
                Error($"Cannot iterate over '{collType}': foreach requires an array or a string.", f.Collection.Span);
                elem = BoundType.Error;
                break;
        }

        var saved = EnterScope();
        var collSlot = _fn.Alloc();
        var indexSlot = _fn.Alloc();

        BoundType itemType;
        var conv = Conversion.None;
        if (f.ItemType is { Name: "var", IsArray: false, TypeArgs.Count: 0 })
            itemType = elem;
        else
        {
            itemType = ResolveValueType(f.ItemType, _scope, f.Span);
            if (!TypeRules.IsAssignable(elem, itemType, out conv))
                Error($"Cannot convert '{elem}' to '{itemType}' in foreach variable '{f.ItemName}'.", f.Span);
        }

        var item = new VariableSymbol(f.ItemName, itemType) { Slot = _fn.Alloc(), Span = f.Span, Module = _mod.Name };
        Declare(item, "Variable");
        _model.Foreach[f] = new ForeachInfo(item, collSlot, indexSlot, conv);

        _fn.LoopDepth++;
        BindBlock(f.Body);
        _fn.LoopDepth--;
        ExitScope(saved);
    }

    private void BindSwitch(SwitchStmt sw)
    {
        var valueType = BindExpr(sw.Value);
        var saved = EnterScope();
        _model.Switches[sw] = new SwitchInfo(_fn.Alloc());

        var seen = new HashSet<Value>();
        var hasDefault = false;
        foreach (var c in sw.Cases)
        {
            if (c.Value is null)
            {
                if (hasDefault) Error("A switch can have only one 'default' label.", sw.Span);
                hasDefault = true;
            }
            else
            {
                var ct = BindExpr(c.Value);
                if (!valueType.IsError && !ct.IsError && !valueType.IsDynamic && !ct.IsDynamic)
                {
                    if (TypeRules.IsAssignable(ct, valueType, out _)) Coerce(c.Value, ct, valueType, c.Value.Span, Use.Other, "case");
                    else if (!TypeRules.IsAssignable(valueType, ct, out _))
                        Error($"Case value of type '{ct}' cannot be compared with '{valueType}'.", c.Value.Span);
                }
                if (_model.Constants.TryGetValue(c.Value, out var cv) && !seen.Add(cv))
                    Error($"Duplicate case value '{cv}'.", c.Value.Span);
            }

            _fn.SwitchDepth++;
            BindBlock(c.Body);
            _fn.SwitchDepth--;
        }
        ExitScope(saved);
    }

    private void BindNestedFunction(FuncDecl fd)
    {
        var f = MakeFunction(fd, null, new Scope(_mod.Scope));
        f.Id = $"{_fn.Symbol.Id}.{fd.Name}#{++_nestedFunctionCounter}";
        if (!Declare(f, "Function")) return;
        _model.Declarations[fd] = f;
        _mod.Bound.Functions.Add(f);
        BindFunctionBody(f, declareSelf: true); // sees only globals and itself – there are no closures
    }
}
