using SabakaLang.AST.Interfaces;
using SabakaLang.AST;
using SabakaLang.AST.Structures;
using SabakaLang.Runtime;

namespace SabakaLang.Binder;

public sealed partial class Binder
{
    private static readonly IReadOnlyDictionary<TypeParamSymbol, BoundType> EmptyMap = new Dictionary<TypeParamSymbol, BoundType>();
    private readonly Dictionary<string, FunctionSymbol> _extensionSymbols = new();

    private enum TargetKind { Value, Type, Module }
    private readonly record struct Target(TargetKind Kind, Symbol? Symbol, BoundType? Type);

    /// <summary>Classifies the left side of <c>a.b</c>: a value, a type name (static access) or a module.</summary>
    private Target ResolveTarget(IExpr e)
    {
        switch (e)
        {
            case NameExpr n:
                switch (LookupName(n.Name))
                {
                    case TypeSymbol ts: _model.Names[n] = ts; return new Target(TargetKind.Type, ts, null);
                    case ModuleSymbol ms: _model.Names[n] = ms; return new Target(TargetKind.Module, ms, null);
                }
                break;
            case MemberExpr { Object: NameExpr mn } me when LookupName(mn.Name) is ModuleSymbol ms2
                                                         && ms2.Exports.TryGetValue(me.Member, out var member) && member is TypeSymbol ts2:
                _model.Names[mn] = ms2;
                return new Target(TargetKind.Type, ts2, null);
        }
        return new Target(TargetKind.Value, null, BindExpr(e));
    }

    // ------------------------------------------------------------------ member access (no call)

    private BoundType BindMember(MemberExpr m)
    {
        if (m.Object is SuperExpr)
        {
            Error("'super' can only be used to call a base class method: super.method(...).", m.Span);
            return BoundType.Error;
        }

        var target = ResolveTarget(m.Object);
        return target.Kind switch
        {
            TargetKind.Module => BindModuleMember((ModuleSymbol)target.Symbol!, m),
            TargetKind.Type => BindStaticMember((TypeSymbol)target.Symbol!, m),
            _ => BindInstanceMember(target.Type!, m)
        };
    }

    private BoundType BindModuleMember(ModuleSymbol ms, MemberExpr m)
    {
        if (!ms.Exports.TryGetValue(m.Member, out var sym))
        {
            Error($"Module '{ms.ModuleName}' has no member '{m.Member}'.", m.Span);
            return BoundType.Error;
        }
        switch (sym)
        {
            case ConstantSymbol c:
                _model.Members[m] = new MemberBinding(MemberKind.Constant, c);
                return Const(m, c.Value, c.Type);
            case VariableSymbol v:
                _model.Members[m] = new MemberBinding(MemberKind.ModuleVariable, v);
                return v.Type;
            default:
                Error($"'{m.Member}' of module '{ms.ModuleName}' is not a value.", m.Span);
                return BoundType.Error;
        }
    }

    private BoundType BindStaticMember(TypeSymbol ts, MemberExpr m)
    {
        if (ts.Members.TryGetValue(m.Member, out var sym))
        {
            switch (sym)
            {
                case EnumMemberSymbol em:
                    _model.Members[m] = new MemberBinding(MemberKind.EnumMember, em);
                    return Const(m, Value.FromEnum(new EnumMember(ts.Id, em.Name, em.Ordinal)), BoundType.Of(ts));
                case FieldSymbol { IsConst: true } f when !f.IsNative:
                    CheckAccess(f, ts, m.Span);
                    _model.Members[m] = new MemberBinding(MemberKind.Constant, f);
                    return Const(m, EnsureFieldConst(f), f.Type);
            }
        }
        Error($"Type '{ts.Name}' has no static member '{m.Member}'.", m.Span);
        return BoundType.Error;
    }

    private BoundType BindInstanceMember(BoundType ot, MemberExpr m)
    {
        if (ot.IsError) return ot;
        if (ot.IsDynamic || ot.Kind == TypeKind.TypeParam)
        {
            _model.Members[m] = new MemberBinding(MemberKind.Dynamic);
            return BoundType.Dynamic;
        }

        if (m.Member == "length" && ot.Kind is TypeKind.Array or TypeKind.String)
        {
            _model.Members[m] = new MemberBinding(MemberKind.Length);
            return BoundType.Int;
        }

        if (ot.TypeSymbol is { } ts && ot.Kind is TypeKind.Class or TypeKind.Struct or TypeKind.Interface)
        {
            var (sym, view) = FindMember(ts, ot, m.Member);
            switch (sym)
            {
                case FieldSymbol f:
                    if (!CheckAccess(f, f.Owner, m.Span)) return BoundType.Error;
                    _model.Members[m] = new MemberBinding(MemberKind.Field, f);
                    var ft = MemberType(f.Type, view, f.Owner);
                    return f.IsConst && !f.IsNative ? Const(m, EnsureFieldConst(f), ft) : ft;
                case PropertySymbol p:
                    _model.Members[m] = new MemberBinding(MemberKind.NativeProperty, p, p.Native.GetAccessor.Key);
                    return p.Type;
                case FunctionSymbol or FunctionGroupSymbol:
                    Error($"'{m.Member}' is a method; call it with parentheses.", m.Span);
                    return BoundType.Error;
            }
        }

        if (FindExtensions(ot, m.Member, property: true).FirstOrDefault() is { } ext)
        {
            var map = new Dictionary<TypeParamSymbol, BoundType>();
            TypeRules.Unify(ext.ReceiverPattern!, ot.NonNull, map, ext.TypeParams.ToHashSet());
            _model.Members[m] = new MemberBinding(MemberKind.ExtensionProperty, ext, ext.NativeKey);
            return Finalize(TypeRules.Apply(ext.ReturnType, map), ext);
        }

        Error($"Type '{ot}' has no member '{m.Member}'.", m.Span);
        return BoundType.Error;
    }

    // ------------------------------------------------------------------ extension methods on built-in types

    private FunctionSymbol GetExtensionSymbol(Extensions.NativeExtension e)
    {
        if (!_extensionSymbols.TryGetValue(e.Key, out var f))
            _extensionSymbols[e.Key] = f = MakeNativeExtension(e);
        return f;
    }

    /// <summary>Extension members named <paramref name="name"/> applicable to a receiver of type <paramref name="recv"/>.</summary>
    private List<FunctionSymbol> FindExtensions(BoundType recv, string name, bool property)
    {
        var result = new List<FunctionSymbol>();
        foreach (var module in _mod.ExtensionModules)
        {
            foreach (var e in module.Extensions)
            {
                if (e.Name != name || e.IsProperty != property) continue;
                var f = GetExtensionSymbol(e);
                if (PatternMatches(f.ReceiverPattern!, recv.NonNull, f.TypeParams.ToHashSet())) result.Add(f);
            }
        }
        // most specific receiver pattern first (string before dynamic, int[] before T[])
        return result.OrderBy(f => Specificity(f.ReceiverPattern!)).ToList();
    }

    private static int Specificity(BoundType p) => p.Kind switch
    {
        TypeKind.Dynamic => 3,
        TypeKind.TypeParam => 2,
        TypeKind.Array => 1 + Specificity(p.Element!) / 4,
        _ => 0
    };

    private static bool PatternMatches(BoundType pattern, BoundType recv, ISet<TypeParamSymbol> vars)
    {
        if (recv.IsError) return false;
        switch (pattern.Kind)
        {
            case TypeKind.Dynamic: return true;
            case TypeKind.TypeParam: return vars.Contains((TypeParamSymbol)pattern.Symbol!);
            case TypeKind.Array:
                return recv.Kind == TypeKind.Array && (recv.Element!.IsDynamic || PatternMatches(pattern.Element!, recv.Element!, vars));
            default:
                return TypeRules.IsAssignable(recv, pattern, out var conv) && conv == Conversion.None && !recv.IsDynamic;
        }
    }

    // ------------------------------------------------------------------ calls

    private BoundType BindCall(CallExpr c)
    {
        switch (c.Callee)
        {
            case SuperExpr: return BindSuperCtorCall(c);
            case NameExpr n: return BindNameCall(c, n);
            case MemberExpr { Object: SuperExpr } m when IsBaseConstructorName(m.Member): return BindSuperCtorCall(c);
            case MemberExpr { Object: SuperExpr } m: return BindSuperMethodCall(c, m);
            case MemberExpr m: return BindMemberCall(c, m);
            default:
                Error("This expression cannot be called.", c.Span);
                foreach (var a in c.Args) BindExpr(a);
                return BoundType.Error;
        }
    }

    private BoundType Bad(CallExpr c, string message)
    {
        Error(message, c.Span);
        foreach (var a in c.Args) BindExpr(a);
        return BoundType.Error;
    }

    private BoundType BindNameCall(CallExpr c, NameExpr n)
    {
        var sym = LookupName(n.Name);
        switch (sym)
        {
            case null:
                return Bad(c, $"Undefined symbol '{n.Name}'.");

            case FunctionSymbol f when f.Owner is not null && !f.IsNative: // method called on the implicit 'this'
            {
                _model.Names[n] = f;
                CheckAccess(f, f.Owner, c.Span);
                var (_, view) = FindMember(_curType!, ThisType(_curType!), n.Name);
                var map = TypeRules.MapFor(f.Owner.TypeParams, view?.Args ?? []);
                var ret = BindInvocation([f], c.Args, c.Span, n.Name, map, out var chosen);
                _model.Calls[c] = new CallBinding(CallKind.Method, chosen ?? f, null, true, n.Name);
                return ret;
            }

            case FunctionSymbol f:
            {
                _model.Names[n] = f;
                var ret = BindInvocation([f], c.Args, c.Span, n.Name, null, out var chosen);
                _model.Calls[c] = new CallBinding(CallKind.Function, chosen ?? f, null, false, n.Name);
                return ret;
            }

            case FunctionGroupSymbol g:
            {
                var ret = BindInvocation(g.Overloads, c.Args, c.Span, n.Name, null, out var chosen);
                if (chosen is not null) _model.Calls[c] = new CallBinding(CallKind.Native, chosen, chosen.NativeKey, false, n.Name);
                return ret;
            }

            case TypeSymbol t:
                return Bad(c, $"Use 'new {t.Name}(...)' to create an instance of '{t.Name}'.");

            default:
                return Bad(c, $"'{n.Name}' is not a function.");
        }
    }

    private BoundType BindMemberCall(CallExpr c, MemberExpr m)
    {
        var target = ResolveTarget(m.Object);

        if (target.Kind == TargetKind.Module)
        {
            var ms = (ModuleSymbol)target.Symbol!;
            if (!ms.Exports.TryGetValue(m.Member, out var member))
                return Bad(c, $"Module '{ms.ModuleName}' has no member '{m.Member}'.");
            switch (member)
            {
                case FunctionSymbol f:
                {
                    var ret = BindInvocation([f], c.Args, c.Span, m.Member, null, out var chosen);
                    _model.Calls[c] = new CallBinding(CallKind.Function, chosen ?? f, null, false, m.Member);
                    return ret;
                }
                case FunctionGroupSymbol g:
                {
                    var ret = BindInvocation(g.Overloads, c.Args, c.Span, m.Member, null, out var chosen);
                    if (chosen is not null) _model.Calls[c] = new CallBinding(CallKind.Native, chosen, chosen.NativeKey, false, m.Member);
                    return ret;
                }
                case TypeSymbol t: return Bad(c, $"Use 'new {t.Name}(...)' to create an instance of '{t.Name}'.");
                default: return Bad(c, $"'{m.Member}' of module '{ms.ModuleName}' is not a function.");
            }
        }

        if (target.Kind == TargetKind.Type)
        {
            var ts = (TypeSymbol)target.Symbol!;
            if (ts.Members.TryGetValue(m.Member, out var sm) && sm is FunctionGroupSymbol sg)
            {
                var statics = sg.Overloads.Where(o => o.NativeKind == NativeBinding.StaticMethod).ToList();
                if (statics.Count > 0)
                {
                    var ret = BindInvocation(statics, c.Args, c.Span, m.Member, null, out var chosen);
                    if (chosen is not null) _model.Calls[c] = new CallBinding(CallKind.Native, chosen, chosen.NativeKey, false, m.Member);
                    return ret;
                }
            }
            return Bad(c, $"Type '{ts.Name}' has no static method '{m.Member}'.");
        }

        var ot = target.Type!;
        if (ot.IsError) return Bad(c, "");
        if (ot.IsDynamic || ot.Kind == TypeKind.TypeParam)
        {
            foreach (var a in c.Args) BindExpr(a);
            _model.Calls[c] = new CallBinding(CallKind.Dynamic, null, null, false, m.Member);
            return BoundType.Dynamic;
        }

        if (ot.TypeSymbol is { } tsym && ot.Kind is TypeKind.Class or TypeKind.Struct or TypeKind.Interface)
        {
            var (sym, view) = FindMember(tsym, ot, m.Member);
            switch (sym)
            {
                case FunctionSymbol f:
                {
                    if (!CheckAccess(f, f.Owner, m.Span)) { foreach (var a in c.Args) BindExpr(a); return BoundType.Error; }
                    var map = TypeRules.MapFor(f.Owner!.TypeParams, view?.Args ?? []);
                    var ret = BindInvocation([f], c.Args, c.Span, m.Member, map, out var chosen);
                    _model.Calls[c] = new CallBinding(CallKind.Method, chosen ?? f, null, false, m.Member);
                    return ret;
                }
                case FunctionGroupSymbol g:
                {
                    var instance = g.Overloads.Where(o => o.NativeKind == NativeBinding.Method).ToList();
                    if (instance.Count == 0) return Bad(c, $"'{m.Member}' is a static method; call it as {tsym.Name}.{m.Member}(...).");
                    var ret = BindInvocation(instance, c.Args, c.Span, m.Member, null, out var chosen);
                    if (chosen is not null) _model.Calls[c] = new CallBinding(CallKind.NativeMethod, chosen, chosen.NativeKey, false, m.Member);
                    return ret;
                }
                case not null:
                    return Bad(c, $"'{m.Member}' is not a method.");
            }
        }

        var exts = FindExtensions(ot, m.Member, property: false);
        if (exts.Count > 0)
        {
            var ret = BindInvocation(exts, c.Args, c.Span, m.Member, null, out var chosen, ot);
            if (chosen is not null) _model.Calls[c] = new CallBinding(CallKind.NativeMethod, chosen, chosen.NativeKey, false, m.Member);
            return ret;
        }

        return Bad(c, $"Type '{ot}' has no method '{m.Member}'.");
    }

    /// <summary>Legacy syntax <c>super.Base(args)</c> inside a constructor means <c>super(args)</c>.</summary>
    private bool IsBaseConstructorName(string member) =>
        _fn.Symbol.IsConstructor && _curType?.BaseSymbol is { } bs && FindConstructor(bs)?.Owner?.Name == member;

    private BoundType BindSuperCtorCall(CallExpr c)
    {
        if (_curType?.BaseSymbol is not { } bs || !_fn.Symbol.IsConstructor)
        {
            Bad(c, "'super(...)' can only be called inside a constructor of a class that has a base class.");
            return BoundType.Void;
        }

        var baseCtor = FindConstructor(bs);
        if (baseCtor is null)
        {
            if (c.Args.Count > 0) Error($"Base class '{bs.Name}' has no constructor but received {c.Args.Count} argument(s).", c.Span);
            foreach (var a in c.Args) BindExpr(a);
            _model.Calls[c] = new CallBinding(CallKind.SuperConstructor, null, null, false, bs.Name);
            return BoundType.Void;
        }

        var map = TypeRules.MapFor(baseCtor.Owner!.TypeParams, BaseArgsFor(_curType, ThisType(_curType).Args, baseCtor.Owner));
        BindInvocation([baseCtor], c.Args, c.Span, bs.Name, map, out _);
        _model.Calls[c] = new CallBinding(CallKind.SuperConstructor, baseCtor, null, false, bs.Name);
        return BoundType.Void;
    }

    private BoundType BindSuperMethodCall(CallExpr c, MemberExpr m)
    {
        if (_curType?.BaseType is not { Symbol: TypeSymbol bs } bt)
            return Bad(c, "'super' can only be used inside a class that has a base class.");

        var map0 = TypeRules.MapFor(_curType.TypeParams, ThisType(_curType).Args);
        var view = TypeRules.Apply(bt, map0);
        var (sym, ownerView) = FindMember(bs, view, m.Member);
        if (sym is FunctionSymbol { IsAbstract: false } f)
        {
            var map = TypeRules.MapFor(f.Owner!.TypeParams, ownerView?.Args ?? []);
            var ret = BindInvocation([f], c.Args, c.Span, m.Member, map, out var chosen);
            _model.Calls[c] = new CallBinding(CallKind.SuperMethod, chosen ?? f, null, false, m.Member);
            return ret;
        }
        return Bad(c, $"Base class '{bs.Name}' has no method '{m.Member}' that can be called with 'super'.");
    }

    // ------------------------------------------------------------------ argument checking / overload resolution

    private static BoundType Finalize(BoundType t, FunctionSymbol f)
    {
        if (f.TypeParams.Count == 0) return t;
        var map = f.TypeParams.ToDictionary(p => p, _ => BoundType.Dynamic);
        return TypeRules.Apply(t, map);
    }

    private static VariableSymbol ParamAt(FunctionSymbol f, int i) =>
        f.IsVariadic && i >= f.Parameters.Count - 1 ? f.Parameters[^1] : f.Parameters[i];

    private static bool ArityOk(FunctionSymbol f, int count) =>
        f.IsVariadic ? count >= f.Parameters.Count - 1 : count == f.Parameters.Count;

    private static string Plural(FunctionSymbol f) =>
        f.IsVariadic ? $"at least {f.Parameters.Count - 1}" : f.Parameters.Count.ToString();

    private BoundType BindInvocation(IReadOnlyList<FunctionSymbol> cands, List<IExpr> args, Span span, string name,
        IReadOnlyDictionary<TypeParamSymbol, BoundType>? classMap, out FunctionSymbol? chosen, BoundType? receiver = null)
    {
        chosen = null;
        classMap ??= EmptyMap;
        var single = cands.Count == 1;

        // 1. bind the arguments (a single candidate lets array literals adopt the parameter type)
        var argTypes = new List<BoundType>(args.Count);
        for (var i = 0; i < args.Count; i++)
        {
            BoundType? expected = null;
            if (single && ArityOk(cands[0], args.Count))
            {
                var pt = TypeRules.Apply(ParamAt(cands[0], i).Type, classMap);
                if (!TypeRules.ContainsTypeParam(pt, cands[0].TypeParams.ToHashSet())) expected = pt;
            }
            argTypes.Add(BindExpr(args[i], expected: expected));
        }

        // 2. applicable candidates
        var applicable = new List<(FunctionSymbol F, Dictionary<TypeParamSymbol, BoundType> Map, int Score)>();
        foreach (var f in cands)
        {
            if (!ArityOk(f, args.Count)) continue;
            var map = new Dictionary<TypeParamSymbol, BoundType>();
            var infer = f.TypeParams.ToHashSet();
            if (receiver is not null && f.ReceiverPattern is not null) TypeRules.Unify(f.ReceiverPattern, receiver.NonNull, map, infer);

            var ok = true;
            for (var i = 0; i < args.Count && ok; i++)
                ok = TypeRules.Unify(TypeRules.Apply(ParamAt(f, i).Type, classMap), argTypes[i], map, infer);
            if (!ok) continue;

            var score = 0;
            for (var i = 0; i < args.Count && ok; i++)
            {
                var pt = Finalize(TypeRules.Apply(TypeRules.Apply(ParamAt(f, i).Type, classMap), map), f);
                if (!argTypes[i].IsError && !TypeRules.IsAssignable(argTypes[i], pt, out _)) ok = false;
                else if (argTypes[i].Same(pt)) score++;
            }
            if (ok) applicable.Add((f, map, score));
        }

        // 3. choose
        (FunctionSymbol F, Dictionary<TypeParamSymbol, BoundType> Map, int Score) pick;
        if (applicable.Count > 0)
            pick = applicable.OrderByDescending(a => a.Score).First();
        else if (single && ArityOk(cands[0], args.Count))
        {
            // report each mismatching argument precisely
            var f = cands[0];
            var map = new Dictionary<TypeParamSymbol, BoundType>();
            if (receiver is not null && f.ReceiverPattern is not null) TypeRules.Unify(f.ReceiverPattern, receiver.NonNull, map, f.TypeParams.ToHashSet());
            for (var i = 0; i < args.Count; i++) TypeRules.Unify(TypeRules.Apply(ParamAt(f, i).Type, classMap), argTypes[i], map, f.TypeParams.ToHashSet());
            pick = (f, map, 0);
        }
        else
        {
            if (argTypes.Any(t => t.IsError)) { }
            else if (single)
                Error($"'{name}' expects {Plural(cands[0])} argument(s), got {args.Count}.", span);
            else
                Error($"No overload of '{name}' accepts ({string.Join(", ", argTypes)}).", span);
            return BoundType.Error;
        }

        chosen = pick.F;
        if (!chosen.IsNative) CheckAccess(chosen, chosen.Owner, span);

        // 4. record conversions / report mismatches
        for (var i = 0; i < args.Count; i++)
        {
            var p = ParamAt(chosen, i);
            var pt = Finalize(TypeRules.Apply(TypeRules.Apply(p.Type, classMap), pick.Map), chosen);
            Coerce(args[i], argTypes[i], pt, args[i].Span, Use.Argument, p.Name);
        }

        return Finalize(TypeRules.Apply(TypeRules.Apply(chosen.ReturnType, classMap), pick.Map), chosen);
    }
}
