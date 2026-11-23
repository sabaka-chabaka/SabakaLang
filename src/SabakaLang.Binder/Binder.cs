using System.Text.RegularExpressions;
using SabakaLang.AST;
using SabakaLang.AST.Interfaces;
using SabakaLang.AST.Structures;
using SabakaLang.Extensions;
using SabakaLang.Lexer.Structures;
using SabakaLang.Runtime;

namespace SabakaLang.Binder;

/// <summary>
/// Semantic analysis: name resolution, static typing, overload/generic resolution, access control and
/// slot allocation. Produces a <see cref="SemanticModel"/> that the compiler turns into bytecode.
/// </summary>
public sealed partial class Binder(ExtensionRegistry? registry = null)
{
    private readonly ExtensionRegistry _registry = registry ?? new ExtensionRegistry();
    private readonly SemanticModel _model = new();
    private readonly List<Diagnostic> _errors = [];
    private readonly List<Diagnostic> _warnings = [];
    private readonly List<Symbol> _symbols = [];
    private readonly List<BoundModule> _bound = [];
    private readonly Dictionary<string, ModuleSymbol> _sourceModules = new();
    private readonly Dictionary<string, ModuleSymbol> _nativeModuleSymbols = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TypeSymbol> _nativeTypes = new();
    private readonly Dictionary<string, NativeEnum> _nativeEnums = new();
    private readonly List<NativeModule> _autoExtensionModules = [];
    private Scope _autoScope = new();
    private int _globalCount;
    private int _nestedFunctionCounter;

    private ModuleCtx _mod = null!;
    private FuncCtx _fn = null!;
    private Scope _scope = null!;
    private TypeSymbol? _curType;

    private sealed class ModuleCtx(ModuleInput input, BoundModule bound, Scope importScope, Scope scope)
    {
        public ModuleInput Input { get; } = input;
        public BoundModule Bound { get; } = bound;
        public Scope ImportScope { get; } = importScope;
        public Scope Scope { get; } = scope;
        public List<NativeModule> ExtensionModules { get; } = [];
        public Dictionary<string, Symbol> Declared { get; } = new();
        public string Name => Input.Name;
        public bool IsRoot => Input.IsRoot;
    }

    private sealed class FuncCtx(FunctionSymbol symbol, BoundType returnType, Scope root, bool isEntry)
    {
        public FunctionSymbol Symbol { get; } = symbol;
        public BoundType ReturnType { get; } = returnType;
        public Scope Root { get; } = root;
        public bool IsEntry { get; } = isEntry;
        public int Next { get; set; }
        public int Max { get; set; }
        public int LoopDepth { get; set; }
        public int SwitchDepth { get; set; }

        public int Alloc()
        {
            var s = Next++;
            if (Next > Max) Max = Next;
            return s;
        }
    }

    // ------------------------------------------------------------------ public API

    public BindResult Bind(IReadOnlyList<IStmt> statements) =>
        Bind([new ModuleInput { Name = "main", IsRoot = true, Statements = statements }]);

    /// <summary>Binds modules in the given order (dependencies first, entry module last).</summary>
    public BindResult Bind(IReadOnlyList<ModuleInput> modules)
    {
        BuildAutoScope();
        foreach (var m in modules) BindModule(m);

        return new BindResult
        {
            Model = _model,
            Modules = _bound,
            Errors = _errors,
            Warnings = _warnings,
            AllSymbols = _symbols,
            GlobalCount = _globalCount
        };
    }

    // ------------------------------------------------------------------ diagnostics

    private string ModuleLabel => _mod is null || _mod.IsRoot ? "" : _mod.Name;

    private void Error(string message, Span span) => _errors.Add(new Diagnostic(message, span.Start, ModuleLabel));
    private void Error(string message, Position pos) => _errors.Add(new Diagnostic(message, pos, ModuleLabel));
    private void Warn(string message, Span span) => _warnings.Add(new Diagnostic(message, span.Start, ModuleLabel, true));

    private string Qualify(string name) => _mod.IsRoot ? name : $"{_mod.Name}::{name}";

    // ------------------------------------------------------------------ scopes and slots

    private (Scope Scope, int Next) EnterScope()
    {
        var saved = (_scope, _fn.Next);
        _scope = new Scope(_scope);
        return saved;
    }

    private void ExitScope((Scope Scope, int Next) saved)
    {
        _scope = saved.Scope;
        _fn.Next = saved.Next; // slots of the block are reused by later blocks
    }

    /// <summary>Declares <paramref name="s"/> in the current scope, reporting duplicates.</summary>
    private bool Declare(Symbol s, string what = "Symbol")
    {
        if (!_scope.TryDeclare(s))
        {
            Error($"{what} '{s.Name}' is already declared in this scope.", s.Span);
            return false;
        }
        _symbols.Add(s);
        if (ReferenceEquals(_scope, _mod.Scope)) _mod.Declared[s.Name] = s;
        return true;
    }

    private Symbol? LookupName(string name)
    {
        for (var s = _scope; s is not null; s = s.Parent)
        {
            if (s.LookupLocal(name) is { } sym) return sym;
            if (ReferenceEquals(s, _fn.Root) && _curType is not null)
            {
                var (member, _) = FindMember(_curType, ThisType(_curType), name);
                if (member is not null) return member;
            }
        }
        return null;
    }

    private static BoundType ThisType(TypeSymbol t) =>
        BoundType.Of(t, t.TypeParams.Select(BoundType.Param).ToList());

    // ------------------------------------------------------------------ native modules

    private static readonly Regex TypeVarName = new("^[A-Z][A-Za-z0-9]?$", RegexOptions.Compiled);

    private void BuildAutoScope()
    {
        foreach (var m in _registry.Modules)
        {
            foreach (var e in m.Enums) _nativeEnums[e.Name] = e;
        }

        _autoScope = new Scope();
        foreach (var m in _registry.AutoImported)
        {
            _autoExtensionModules.Add(m);
            var ms = GetNativeModuleSymbol(m);
            InjectExports(_autoScope, ms);
            _autoScope.TryDeclare(new ModuleSymbol(m.Name) { Exports = { }, ModuleName = m.Name }.WithExports(ms));
        }
    }

    private static void InjectExports(Scope target, ModuleSymbol ms)
    {
        foreach (var (name, sym) in ms.Exports)
            InjectSymbol(target, name, sym);
    }

    private static void InjectSymbol(Scope target, string name, Symbol sym)
    {
        if (sym is FunctionGroupSymbol g && target.LookupLocal(name) is FunctionGroupSymbol existing)
        {
            // merge overloads of the same name coming from different modules
            var merged = new FunctionGroupSymbol(name) { IsNative = true };
            merged.Overloads.AddRange(existing.Overloads);
            merged.Overloads.AddRange(g.Overloads.Where(o => existing.Overloads.All(x => x.NativeKey != o.NativeKey)));
            target.Replace(merged);
        }
        else
        {
            target.Replace(sym);
        }
    }

    private ModuleSymbol GetNativeModuleSymbol(NativeModule m)
    {
        if (_nativeModuleSymbols.TryGetValue(m.Name, out var cached)) return cached;
        var ms = new ModuleSymbol(m.Name) { IsNative = true, ModuleName = m.Name, Module = m.Name };
        _nativeModuleSymbols[m.Name] = ms;

        foreach (var f in m.Functions)
        {
            if (!ms.Exports.TryGetValue(f.Name, out var g))
                ms.Exports[f.Name] = g = new FunctionGroupSymbol(f.Name) { IsNative = true, Module = m.Name };
            ((FunctionGroupSymbol)g).Overloads.Add(
                MakeNativeFunction(f.Signature, f.Key, NativeBinding.Function, null, m.Name));
        }

        foreach (var c in m.Classes) ms.Exports[c.Name] = GetNativeType(c);

        foreach (var k in m.Constants)
        {
            var type = ResolveNative(k.Type, new Dictionary<string, TypeParamSymbol>(), k.Name);
            ms.Exports[k.Name] = new ConstantSymbol(k.Name, type, k.Value) { IsNative = true, Module = m.Name };
        }

        foreach (var e in m.Enums) ms.Exports[e.Name] = GetNativeEnumType(e, m.Name);
        return ms;
    }

    private TypeSymbol GetNativeEnumType(NativeEnum e, string module)
    {
        if (_nativeTypes.TryGetValue(e.Name, out var cached)) return cached;
        var t = new TypeSymbol(e.Name, SymbolKind.Enum, e.Name) { IsNative = true, Module = module };
        _nativeTypes[e.Name] = t;
        for (var i = 0; i < e.Members.Count; i++)
        {
            var member = new EnumMemberSymbol(e.Members[i], t, i) { IsNative = true, Module = module };
            t.Members[member.Name] = member;
            t.EnumMembers.Add(member.Name);
        }
        return t;
    }

    private TypeSymbol GetNativeType(NativeClass c)
    {
        if (_nativeTypes.TryGetValue(c.Name, out var cached)) return cached;
        var t = new TypeSymbol(c.Name, SymbolKind.Class, c.Name) { IsNative = true, Native = c, Module = c.Module };
        _nativeTypes[c.Name] = t; // register before members: signatures may refer back to the class

        foreach (var f in c.Fields)
            t.Members[f.Name] = new FieldSymbol(f.Name, ResolveNative(f.Type, [], f.Name), t)
                { IsNative = true, NativeField = f, Module = c.Module };

        foreach (var p in c.Properties)
            t.Members[p.Name] = new PropertySymbol(p.Name, ResolveNative(p.Type, [], p.Name), t, p) { IsNative = true, Module = c.Module };

        foreach (var m in c.Methods)
        {
            if (!t.Members.TryGetValue(m.Name, out var g) || g is not FunctionGroupSymbol)
                t.Members[m.Name] = g = new FunctionGroupSymbol(m.Name) { IsNative = true, Module = c.Module };
            ((FunctionGroupSymbol)g).Overloads.Add(MakeNativeFunction(m.Signature, m.Key,
                m.IsStatic ? NativeBinding.StaticMethod : NativeBinding.Method, t, c.Module));
        }

        if (c.Constructors.Count > 0)
        {
            var g = new FunctionGroupSymbol("<ctor>") { IsNative = true, Module = c.Module };
            foreach (var k in c.Constructors)
                g.Overloads.Add(MakeNativeFunction(k.Signature, k.Key, NativeBinding.Constructor, t, c.Module));
            t.Members["<ctor>"] = g;
        }
        return t;
    }

    private FunctionSymbol MakeNativeFunction(NativeSignature sig, string key, NativeBinding binding, TypeSymbol? owner, string module)
    {
        var vars = new Dictionary<string, TypeParamSymbol>();
        var f = new FunctionSymbol(sig.Name)
        {
            IsNative = true, NativeKind = binding, NativeKey = key, Owner = owner, IsVariadic = sig.IsVariadic,
            Id = key, Module = module
        };
        f.ReturnType = ResolveNative(sig.ReturnType, vars, key);
        foreach (var p in sig.Parameters)
            f.Parameters.Add(new VariableSymbol(p.Name, ResolveNative(p.Type, vars, key), true));
        f.TypeParams.AddRange(vars.Values);
        return f;
    }

    private FunctionSymbol MakeNativeExtension(NativeExtension e)
    {
        var vars = new Dictionary<string, TypeParamSymbol>();
        var f = MakeNativeFunctionWithVars(e.Signature, e.Key, vars, e.Module);
        f.NativeKind = NativeBinding.Extension;
        f.ExtensionTarget = e.Target;
        f.IsExtensionProperty = e.IsProperty;
        f.ReceiverPattern = ResolveNative(e.Target, vars, e.Key);
        f.TypeParams.Clear();
        f.TypeParams.AddRange(vars.Values);
        return f;
    }

    private FunctionSymbol MakeNativeFunctionWithVars(NativeSignature sig, string key, Dictionary<string, TypeParamSymbol> vars, string module)
    {
        var f = new FunctionSymbol(sig.Name) { IsNative = true, NativeKey = key, IsVariadic = sig.IsVariadic, Id = key, Module = module };
        f.ReturnType = ResolveNative(sig.ReturnType, vars, key);
        foreach (var p in sig.Parameters)
            f.Parameters.Add(new VariableSymbol(p.Name, ResolveNative(p.Type, vars, key), true));
        return f;
    }

    private BoundType ResolveNative(string text, Dictionary<string, TypeParamSymbol> vars, string context)
    {
        var ts = TypeSyntax.TryParse(text);
        if (ts is null)
        {
            Error($"Native member '{context}': invalid type '{text}'.", default(Span));
            return BoundType.Dynamic;
        }
        return ResolveNativeSyntax(ts, vars, context);
    }

    private BoundType ResolveNativeSyntax(TypeSyntax ts, Dictionary<string, TypeParamSymbol> vars, string context)
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
            case "dynamic" or "any": core = BoundType.Dynamic; break;
            default:
                if (_registry.FindClass(ts.Name) is { } cls) core = BoundType.Of(GetNativeType(cls));
                else if (_nativeEnums.TryGetValue(ts.Name, out var en)) core = BoundType.Of(GetNativeEnumType(en, ""));
                else if (TypeVarName.IsMatch(ts.Name))
                {
                    if (!vars.TryGetValue(ts.Name, out var tv))
                        vars[ts.Name] = tv = new TypeParamSymbol(ts.Name) { IsNative = true };
                    core = BoundType.Param(tv);
                }
                else
                {
                    Error($"Native member '{context}' references unknown type '{ts.Name}'.", default(Span));
                    core = BoundType.Dynamic;
                }
                break;
        }
        for (var i = 0; i < ts.ArrayDepth; i++) core = BoundType.ArrayOf(core);
        return ts.IsNullable ? core.WithNullable(true) : core;
    }

    // ------------------------------------------------------------------ imports

    private void BindImport(ImportStmt imp)
    {
        if (!_mod.Input.Imports.TryGetValue(imp.Path, out var target))
        {
            Error($"Cannot resolve import '{imp.Path}'.", imp.Span);
            return;
        }

        ModuleSymbol? ms = null;
        switch (target)
        {
            case NativeImport n:
            {
                var nm = _registry.FindModule(n.Module);
                if (nm is null) { Error($"Native module '{n.Module}' is not registered.", imp.Span); return; }
                ms = GetNativeModuleSymbol(nm);
                if (!_mod.ExtensionModules.Contains(nm)) _mod.ExtensionModules.Add(nm);
                break;
            }
            case SourceImport s:
                if (!_sourceModules.TryGetValue(s.Module, out ms))
                {
                    Error($"Module '{s.Module}' has not been loaded (circular import?).", imp.Span);
                    return;
                }
                break;
        }
        if (ms is null) return;

        var baseName = BaseName(imp.Path);

        if (imp.Names.Count > 0)
        {
            foreach (var name in imp.Names)
            {
                if (ms.Exports.TryGetValue(name, out var sym)) InjectSymbol(_mod.ImportScope, name, sym);
                else Error($"Module '{imp.Path}' has no member '{name}'.", imp.Span);
            }
        }
        else if (imp.Alias is null)
        {
            InjectExports(_mod.ImportScope, ms);
        }

        var moduleName = imp.Alias ?? (imp.Names.Count == 0 ? baseName : null);
        if (moduleName is not null)
        {
            var alias = new ModuleSymbol(moduleName) { ModuleName = ms.ModuleName, IsNative = ms.IsNative, Span = imp.Span, Module = _mod.Name };
            foreach (var kv in ms.Exports) alias.Exports[kv.Key] = kv.Value;
            _mod.ImportScope.Replace(alias);
            _symbols.Add(alias);
        }
    }

    private static string BaseName(string path)
    {
        var name = path.Replace('\\', '/');
        var slash = name.LastIndexOf('/');
        if (slash >= 0) name = name[(slash + 1)..];
        var dot = name.LastIndexOf('.');
        if (dot > 0 && name[(dot + 1)..].Equals("sabaka", StringComparison.OrdinalIgnoreCase)) name = name[..dot];
        else if (dot > 0 && !name.Contains('/')) name = name[(dot + 1)..]; // "std.math" -> "math"
        return name;
    }
}

internal static class SymbolExtensions
{
    public static ModuleSymbol WithExports(this ModuleSymbol target, ModuleSymbol source)
    {
        foreach (var kv in source.Exports) target.Exports[kv.Key] = kv.Value;
        return target;
    }
}
