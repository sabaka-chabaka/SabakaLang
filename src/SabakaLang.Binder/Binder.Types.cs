using SabakaLang.AST;
using SabakaLang.AST.Interfaces;
using SabakaLang.AST.Structures;
using SabakaLang.Extensions;
using SabakaLang.Runtime;

namespace SabakaLang.Binder;

public sealed partial class Binder
{
    // ------------------------------------------------------------------ module orchestration

    private void BindModule(ModuleInput input)
    {
        var importScope = new Scope(_autoScope);
        var scope = new Scope(importScope);
        var entry = new FunctionSymbol("<module>")
        {
            Id = input.IsRoot ? "<main>" : $"{input.Name}::<main>", Module = input.Name, IsNative = false
        };
        var bound = new BoundModule { Name = input.Name, IsRoot = input.IsRoot, Statements = input.Statements, Entry = entry };
        _bound.Add(bound);

        _mod = new ModuleCtx(input, bound, importScope, scope);
        _mod.ExtensionModules.AddRange(_autoExtensionModules);
        _scope = scope;
        _fn = new FuncCtx(entry, BoundType.Void, scope, isEntry: true);
        _curType = null;

        // 1. imports are hoisted: they apply to the whole module
        foreach (var imp in input.Statements.OfType<ImportStmt>()) BindImport(imp);

        // 2. declare all types so declarations can refer to each other in any order
        var types = new List<TypeSymbol>();
        foreach (var st in input.Statements)
            if (st is ClassDecl or InterfaceDecl or StructDecl or EnumDecl && DeclareType(st) is { } t)
                types.Add(t);

        // 3. headers, members, validation
        foreach (var t in types) ResolveHeader(t);
        foreach (var t in types) DeclareMembers(t);
        foreach (var t in types) ValidateType(t);

        // 4. top-level functions (signatures only)
        var functions = new List<FunctionSymbol>();
        foreach (var st in input.Statements)
            if (st is FuncDecl fd && DeclareFunction(fd) is { } f)
                functions.Add(f);

        // 5. top-level code, in order
        foreach (var st in input.Statements)
        {
            if (st is FuncDecl or ClassDecl or InterfaceDecl or StructDecl or EnumDecl or ImportStmt) continue;
            BindStmt(st);
        }
        entry.LocalCount = _fn.Max;
        var entryCtx = _fn;

        // 6. bodies (they can see every top-level declaration, including variables declared later in the file)
        foreach (var f in functions) BindFunctionBody(f);
        foreach (var t in types) BindTypeBodies(t);

        _fn = entryCtx;
        _scope = scope;

        // 7. exports
        var exports = new ModuleSymbol(input.Name) { ModuleName = input.Name, Module = input.Name };
        foreach (var (name, sym) in _mod.Declared)
            if (sym.Access != AccessMod.Private) exports.Exports[name] = sym;
        _sourceModules[input.Name] = exports;
    }

    // ------------------------------------------------------------------ type resolution

    private Scope TypeParamScope(TypeSymbol? t, FunctionSymbol? f = null)
    {
        var s = new Scope(_mod.Scope);
        if (t is not null) foreach (var p in t.TypeParams) s.TryDeclare(p);
        if (f is not null) foreach (var p in f.TypeParams) s.TryDeclare(p);
        return s;
    }

    private Symbol? LookupInScope(string name, Scope scope)
    {
        var dot = name.IndexOf('.');
        if (dot < 0) return scope.Lookup(name);
        var head = scope.Lookup(name[..dot]);
        return head is ModuleSymbol m && m.Exports.TryGetValue(name[(dot + 1)..], out var member) ? member : null;
    }

    private BoundType ResolveSyntax(TypeSyntax ts, Span span, Scope scope)
    {
        BoundType core;
        switch (ts.Name)
        {
            case "int": core = BoundType.Int; break;
            case "float": core = BoundType.Float; break;
            case "bool": core = BoundType.Bool; break;
            case "string": core = BoundType.String; break;
            case "char": core = BoundType.Char; break;
            case "void": core = BoundType.Void; break;
            case "dynamic": core = BoundType.Dynamic; break;
            default:
            {
                var sym = LookupInScope(ts.Name, scope);
                switch (sym)
                {
                    case TypeSymbol t:
                    {
                        var args = ts.Args.Select(a => ResolveSyntax(a, span, scope)).ToList();
                        if (args.Count > 0 && args.Count != t.TypeParams.Count)
                        {
                            Error(t.TypeParams.Count == 0
                                ? $"Type '{t.Name}' is not generic."
                                : $"Type '{t.Name}' expects {t.TypeParams.Count} type argument(s), got {args.Count}.", span);
                            args = [];
                        }
                        core = BoundType.Of(t, args);
                        break;
                    }
                    case TypeParamSymbol p:
                        core = BoundType.Param(p);
                        break;
                    case null:
                        Error($"Unknown type '{ts.Name}'.", span);
                        core = BoundType.Error;
                        break;
                    default:
                        Error($"'{ts.Name}' is not a type.", span);
                        core = BoundType.Error;
                        break;
                }
                break;
            }
        }

        for (var i = 0; i < ts.ArrayDepth; i++) core = BoundType.ArrayOf(core);
        return ts.IsNullable ? core.WithNullable(true) : core;
    }

    private BoundType ResolveTypeText(string text, Span span, Scope scope)
    {
        var ts = TypeSyntax.TryParse(text);
        if (ts is null)
        {
            Error($"Invalid type '{text}'.", span);
            return BoundType.Error;
        }
        return ResolveSyntax(ts, span, scope);
    }

    private BoundType ResolveTypeRef(TypeRef tr, Scope scope, Span fallback)
    {
        var span = tr.Span.Start.Line > 0 ? tr.Span : fallback;
        var args = new List<TypeSyntax>();
        foreach (var a in tr.TypeArgs)
        {
            var parsed = TypeSyntax.TryParse(a);
            if (parsed is null) { Error($"Invalid type argument '{a}'.", span); return BoundType.Error; }
            args.Add(parsed);
        }
        var depth = tr.ArrayDepth > 0 ? tr.ArrayDepth : tr.IsArray ? 1 : 0;
        return ResolveSyntax(new TypeSyntax(tr.Name, args, depth, tr.IsNullable), span, scope);
    }

    /// <summary>Like <see cref="ResolveTypeRef"/> but rejects <c>void</c> (variables, fields, parameters).</summary>
    private BoundType ResolveValueType(TypeRef tr, Scope scope, Span fallback)
    {
        var t = ResolveTypeRef(tr, scope, fallback);
        if (t.Kind == TypeKind.Void)
        {
            Error("'void' cannot be used as the type of a value.", tr.Span.Start.Line > 0 ? tr.Span : fallback);
            return BoundType.Error;
        }
        return t;
    }

    // ------------------------------------------------------------------ declarations of types

    private TypeSymbol? DeclareType(IStmt stmt)
    {
        var (name, kind, tps, span) = stmt switch
        {
            ClassDecl c => (c.Name, SymbolKind.Class, c.TypeParams, c.Span),
            InterfaceDecl i => (i.Name, SymbolKind.Interface, i.TypeParams, i.Span),
            StructDecl s => (s.Name, SymbolKind.Struct, [], s.Span),
            EnumDecl e => (e.Name, SymbolKind.Enum, [], e.Span),
            _ => throw new InvalidOperationException()
        };

        var sym = new TypeSymbol(name, kind, Qualify(name)) { Decl = stmt, Span = span, Module = _mod.Name };
        foreach (var tp in tps) sym.TypeParams.Add(new TypeParamSymbol(tp.Name) { Span = span, Module = _mod.Name });

        if (!Declare(sym, kind.ToString())) return null;
        _model.Declarations[stmt] = sym;
        _mod.Bound.Types.Add(sym);
        return sym;
    }

    private void ResolveHeader(TypeSymbol t)
    {
        var scope = TypeParamScope(t);
        switch (t.Decl)
        {
            case ClassDecl c:
            {
                var refs = new List<string>();
                if (c.Base is not null) refs.Add(c.Base);
                refs.AddRange(c.Interfaces);
                for (var i = 0; i < refs.Count; i++)
                {
                    var bt = ResolveTypeText(refs[i], c.Span, scope);
                    if (bt.IsError) continue;
                    if (bt.Symbol is not TypeSymbol bs) { Error($"'{refs[i]}' is not a class or interface.", c.Span); continue; }
                    if (bs.IsNative) { Error($"Cannot inherit from native class '{bs.Name}'.", c.Span); continue; }
                    switch (bs.Kind)
                    {
                        case SymbolKind.Interface:
                            t.Interfaces.Add(bt);
                            break;
                        case SymbolKind.Class when i == 0 && c.Base is not null:
                            t.BaseType = bt;
                            break;
                        case SymbolKind.Class:
                            Error($"'{bs.Name}' is a class: a class may have one base class and it must be listed first.", c.Span);
                            break;
                        default:
                            Error($"Cannot inherit from {bs.Kind.ToString().ToLowerInvariant()} '{bs.Name}'.", c.Span);
                            break;
                    }
                }
                break;
            }
            case InterfaceDecl i:
                foreach (var p in i.Parents)
                {
                    var bt = ResolveTypeText(p, i.Span, scope);
                    if (bt.IsError) continue;
                    if (bt.Symbol is TypeSymbol { Kind: SymbolKind.Interface }) t.Interfaces.Add(bt);
                    else Error($"'{p}' is not an interface.", i.Span);
                }
                break;
        }
    }

    private void DeclareMembers(TypeSymbol t)
    {
        var scope = TypeParamScope(t);
        switch (t.Decl)
        {
            case ClassDecl c:
                foreach (var fd in c.Fields) DeclareField(t, fd, scope);
                foreach (var md in c.Methods) DeclareMethod(t, md, scope, c.Name);
                break;
            case StructDecl s:
                foreach (var fd in s.Fields) DeclareField(t, fd, scope);
                break;
            case InterfaceDecl i:
                foreach (var md in i.Methods)
                {
                    var f = MakeFunction(md, t, scope);
                    f.IsAbstract = true;
                    f.Id = $"{t.Id}.{md.Name}";
                    AddMember(t, f, md.Span);
                    t.Methods.Add(f);
                    _model.Declarations[md] = f;
                }
                break;
            case EnumDecl e:
                for (var k = 0; k < e.Members.Count; k++)
                {
                    var m = new EnumMemberSymbol(e.Members[k], t, k) { Span = e.Span, Module = _mod.Name };
                    if (t.Members.ContainsKey(m.Name)) Error($"Enum member '{m.Name}' is already declared in '{t.Name}'.", e.Span);
                    else
                    {
                        t.Members[m.Name] = m;
                        t.EnumMembers.Add(m.Name);
                    }
                }
                break;
        }
    }

    private void AddMember(TypeSymbol t, Symbol member, Span span)
    {
        if (t.Members.ContainsKey(member.Name))
        {
            Error($"Member '{member.Name}' is already declared in '{t.Name}'.", span);
            return;
        }
        t.Members[member.Name] = member;
        _symbols.Add(member);
    }

    private void DeclareField(TypeSymbol t, VarDecl fd, Scope scope)
    {
        BoundType type;
        if (fd.Type.Name == "var" && fd.Type.TypeArgs.Count == 0 && !fd.Type.IsArray)
        {
            Error("'var' cannot be used for fields; declare the type explicitly.", fd.Span);
            type = BoundType.Error;
        }
        else type = ResolveValueType(fd.Type, scope, fd.Span);

        var f = new FieldSymbol(fd.Name, type, t)
            { Decl = fd, Span = fd.Span, Access = fd.Access, IsConst = fd.IsConst, Module = _mod.Name };
        AddMember(t, f, fd.Span);
        t.Fields.Add(f);
        _model.Declarations[fd] = f;
    }

    private void DeclareMethod(TypeSymbol t, FuncDecl md, Scope scope, string className)
    {
        var f = MakeFunction(md, t, scope);
        _model.Declarations[md] = f;
        _mod.Bound.Functions.Add(f);

        if (md.Name == className)
        {
            f.IsConstructor = true;
            f.Id = $"{t.Id}.<ctor>";
            if (md.ReturnType.Name != "void" || md.ReturnType.IsArray)
                Error($"Constructor '{md.Name}' must be declared 'void'.", md.Span);
            if (md.IsOverride) Error("A constructor cannot be marked 'override'.", md.Span);
            if (t.Constructor is not null)
                Error($"Class '{t.Name}' already has a constructor (constructor overloading is not supported).", md.Span);
            else t.Constructor = f;
            return;
        }

        f.Id = $"{t.Id}.{md.Name}";
        AddMember(t, f, md.Span);
        t.Methods.Add(f);
    }

    /// <summary>Creates the symbol for a function/method declaration and resolves its signature.</summary>
    private FunctionSymbol MakeFunction(FuncDecl d, TypeSymbol? owner, Scope typeScope)
    {
        var f = new FunctionSymbol(d.Name)
        {
            Decl = d, Span = d.Span, Module = _mod.Name, Access = d.Access, Owner = owner, IsOverride = d.IsOverride,
            Id = Qualify(d.Name)
        };
        var fnScope = new Scope(typeScope);
        foreach (var tp in d.TypeParams)
        {
            var p = new TypeParamSymbol(tp.Name) { Span = d.Span, Module = _mod.Name };
            if (!fnScope.TryDeclare(p)) Error($"Type parameter '{tp.Name}' is already declared.", d.Span);
            else f.TypeParams.Add(p);
        }

        f.ReturnType = ResolveTypeRef(d.ReturnType, fnScope, d.Span);
        var names = new HashSet<string>();
        foreach (var prm in d.Params)
        {
            var span = prm.Span.Start.Line > 0 ? prm.Span : d.Span;
            var type = ResolveValueType(prm.Type, fnScope, span);
            if (!names.Add(prm.Name)) Error($"Parameter '{prm.Name}' is already declared in '{d.Name}'.", span);
            f.Parameters.Add(new VariableSymbol(prm.Name, type, true) { Span = span, Module = _mod.Name });
        }
        return f;
    }

    private FunctionSymbol? DeclareFunction(FuncDecl d)
    {
        if (d.IsOverride)
        {
            Error("The 'override' modifier is only valid on class methods.", d.Span);
        }

        var f = MakeFunction(d, null, new Scope(_mod.Scope));
        if (!Declare(f, "Function")) return null;
        _model.Declarations[d] = f;
        _mod.Bound.Functions.Add(f);
        return f;
    }

    // ------------------------------------------------------------------ member lookup

    /// <summary>Finds a field/method/property by name in <paramref name="t"/>, its base classes and interfaces.
    /// Also returns the owner type as seen from <paramref name="view"/> (for generic substitution).</summary>
    private (Symbol? Symbol, BoundType? Owner) FindMember(TypeSymbol t, BoundType view, string name, HashSet<TypeSymbol>? seen = null)
    {
        seen ??= new HashSet<TypeSymbol>(ReferenceEqualityComparer.Instance);
        if (!seen.Add(t)) return (null, null);
        if (t.Members.TryGetValue(name, out var s)) return (s, view);

        var map = TypeRules.MapFor(t.TypeParams, view.Args);
        if (t.BaseType is { Symbol: TypeSymbol bs } bt)
        {
            var r = FindMember(bs, TypeRules.Apply(bt, map), name, seen);
            if (r.Symbol is not null) return r;
        }
        foreach (var it in t.Interfaces)
        {
            if (it.Symbol is not TypeSymbol its) continue;
            var r = FindMember(its, TypeRules.Apply(it, map), name, seen);
            if (r.Symbol is not null) return r;
        }
        return (null, null);
    }

    /// <summary>Type of a member as seen through an instantiated owner (<c>Box&lt;int&gt;.value</c> → <c>int</c>).</summary>
    private static BoundType MemberType(BoundType declared, BoundType? ownerView, TypeSymbol? owner)
    {
        if (ownerView is null || owner is null || owner.TypeParams.Count == 0) return declared;
        return TypeRules.Apply(declared, TypeRules.MapFor(owner.TypeParams, ownerView.Args));
    }

    /// <summary>Constructor of the class or, when it has none, of the nearest base class that does.</summary>
    private FunctionSymbol? FindConstructor(TypeSymbol t)
    {
        for (var c = t; c is not null; c = c.BaseSymbol)
            if (c.Constructor is not null) return c.Constructor;
        return null;
    }

    private FunctionSymbol? FindMethodInBases(TypeSymbol t, string name)
    {
        for (var c = t.BaseSymbol; c is not null; c = c.BaseSymbol)
            if (c.Members.TryGetValue(name, out var m) && m is FunctionSymbol f) return f;
        return null;
    }

    private bool CheckAccess(Symbol member, TypeSymbol? owner, Span span)
    {
        if (member.Access == AccessMod.Public || owner is null) return true;
        var ok = member.Access switch
        {
            AccessMod.Private => ReferenceEquals(_curType, owner),
            AccessMod.Protected => _curType is not null && (_curType.IsSubclassOf(owner) || owner.IsSubclassOf(_curType)),
            _ => true
        };
        if (!ok) Error($"'{member.Name}' is {member.Access.ToString().ToLowerInvariant()} in '{owner.Name}'.", span);
        return ok;
    }

    // ------------------------------------------------------------------ validation of declarations

    private void ValidateType(TypeSymbol t)
    {
        // inheritance cycles
        var seen = new HashSet<TypeSymbol>(ReferenceEqualityComparer.Instance) { t };
        for (var c = t.BaseSymbol; c is not null; c = c.BaseSymbol)
        {
            if (!seen.Add(c))
            {
                Error($"Cyclic inheritance involving '{t.Name}'.", t.Span);
                t.BaseType = null;
                break;
            }
        }

        if (t.Kind == SymbolKind.Interface && HasInterfaceCycle(t, new HashSet<TypeSymbol>(ReferenceEqualityComparer.Instance)))
        {
            Error($"Cyclic inheritance involving '{t.Name}'.", t.Span);
            t.Interfaces.Clear();
        }

        if (t.Kind != SymbolKind.Class) return;

        foreach (var m in t.Methods)
        {
            var baseMethod = FindMethodInBases(t, m.Name);
            if (m.IsOverride)
            {
                if (baseMethod is null && !ImplementsInterfaceMethod(t, m.Name))
                    Error($"Method '{t.Name}.{m.Name}' is marked 'override' but there is no base method to override.", m.Span);
                else if (baseMethod is not null && !SameSignature(baseMethod, m))
                    Error($"Method '{t.Name}.{m.Name}' does not match the signature of the overridden '{baseMethod.Owner?.Name}.{m.Name}'.", m.Span);
            }
            else if (baseMethod is not null && !baseMethod.IsConstructor)
            {
                Warn($"Method '{t.Name}.{m.Name}' hides an inherited method; add 'override' to override it.", m.Span);
            }
        }

        foreach (var iface in t.AllInterfaces().ToList())
        {
            foreach (var im in iface.Methods)
            {
                var impl = FindImplementation(t, im.Name);
                if (impl is null)
                    Error($"Class '{t.Name}' does not implement '{iface.Name}.{im.Name}'.", t.Span);
                else if (!SameSignature(im, impl))
                    Error($"Method '{t.Name}.{im.Name}' does not match the signature of '{iface.Name}.{im.Name}'.", impl.Span);
            }
        }
    }

    private static bool HasInterfaceCycle(TypeSymbol t, HashSet<TypeSymbol> path)
    {
        if (!path.Add(t)) return true;
        foreach (var p in t.Interfaces)
            if (p.Symbol is TypeSymbol ps && HasInterfaceCycle(ps, path)) return true;
        path.Remove(t);
        return false;
    }

    private static FunctionSymbol? FindImplementation(TypeSymbol t, string name)
    {
        for (var c = t; c is not null; c = c.BaseSymbol)
            if (c.Members.TryGetValue(name, out var m) && m is FunctionSymbol { IsAbstract: false } f) return f;
        return null;
    }

    private static bool ImplementsInterfaceMethod(TypeSymbol t, string name) =>
        t.AllInterfaces().Any(i => i.Members.ContainsKey(name));

    private static bool SameSignature(FunctionSymbol a, FunctionSymbol b)
    {
        if (a.Parameters.Count != b.Parameters.Count) return false;
        for (var i = 0; i < a.Parameters.Count; i++)
        {
            var pa = a.Parameters[i].Type;
            var pb = b.Parameters[i].Type;
            if (pa.IsError || pb.IsError || HasTypeParam(pa) || HasTypeParam(pb)) continue;
            if (!pa.Same(pb)) return false;
        }
        var ra = a.ReturnType;
        var rb = b.ReturnType;
        return ra.IsError || rb.IsError || HasTypeParam(ra) || HasTypeParam(rb) || ra.Same(rb);
    }

    private static bool HasTypeParam(BoundType t) => t.Kind switch
    {
        TypeKind.TypeParam => true,
        TypeKind.Array => HasTypeParam(t.Element!),
        _ => t.Args.Any(HasTypeParam)
    };
}
