using SabakaLang.AST;
using SabakaLang.AST.Interfaces;
using SabakaLang.AST.Structures;
using SabakaLang.Runtime;
using SabakaLang.Extensions;

namespace SabakaLang.Binder;

public enum SymbolKind
{
    Variable, Parameter, Field, Property, Function, FunctionGroup,
    Class, Interface, Struct, Enum, EnumMember, TypeParam, Module, Constant
}

public abstract class Symbol(string name, SymbolKind kind)
{
    public string Name { get; } = name;
    public SymbolKind Kind { get; } = kind;
    public Span Span { get; set; }

    /// <summary>Name of the module that declares this symbol.</summary>
    public string Module { get; set; } = "";

    public AccessMod Access { get; set; } = AccessMod.Public;
    public bool IsNative { get; set; }

    public override string ToString() => $"{Kind} {Name}";
}

public sealed class VariableSymbol(string name, BoundType type, bool isParameter = false)
    : Symbol(name, isParameter ? SymbolKind.Parameter : SymbolKind.Variable)
{
    public BoundType Type { get; set; } = type;
    public bool IsConst { get; set; }

    /// <summary>Module-level variable stored in the global table (visible to every function of the program).</summary>
    public bool IsGlobal { get; set; }

    /// <summary>Local slot in the current frame, or the program-wide global index when <see cref="IsGlobal"/>.</summary>
    public int Slot { get; set; }
}

public sealed class ConstantSymbol(string name, BoundType type, Value value) : Symbol(name, SymbolKind.Constant)
{
    public BoundType Type { get; } = type;
    public Value Value { get; } = value;
}

public sealed class FieldSymbol(string name, BoundType type, TypeSymbol owner) : Symbol(name, SymbolKind.Field)
{
    public BoundType Type { get; set; } = type;
    public TypeSymbol Owner { get; } = owner;
    public VarDecl? Decl { get; set; }
    public bool IsConst { get; set; }
    public NativeField? NativeField { get; set; }

    // lazily evaluated constant value (class-level const)
    public Value ConstValue { get; set; }
    public bool ConstEvaluated { get; set; }
    public bool ConstEvaluating { get; set; }
}

/// <summary>Computed property of a native class (C# getter/setter).</summary>
public sealed class PropertySymbol(string name, BoundType type, TypeSymbol owner, NativeProperty native)
    : Symbol(name, SymbolKind.Property)
{
    public BoundType Type { get; } = type;
    public TypeSymbol Owner { get; } = owner;
    public NativeProperty Native { get; } = native;
}

public enum NativeBinding { None, Function, Method, StaticMethod, Constructor, Extension }

public sealed class FunctionSymbol(string name) : Symbol(name, SymbolKind.Function)
{
    public BoundType ReturnType { get; set; } = BoundType.Void;
    public List<VariableSymbol> Parameters { get; } = [];
    public List<TypeParamSymbol> TypeParams { get; } = [];
    public TypeSymbol? Owner { get; set; }
    public FuncDecl? Decl { get; set; }
    public bool IsConstructor { get; set; }
    public bool IsOverride { get; set; }
    public bool IsAbstract { get; set; }
    public bool IsVariadic { get; set; }

    /// <summary>Unique name used in bytecode: <c>foo</c>, <c>util::foo</c>, <c>Dog.bark</c>.</summary>
    public string Id { get; set; } = name;

    public NativeBinding NativeKind { get; set; }
    public string? NativeKey { get; set; }

    /// <summary>Number of local slots (parameters, locals and hidden temporaries) the function needs. Filled by the binder.</summary>
    public int LocalCount { get; set; }

    /// <summary>Set for <c>NativeBinding.Extension</c>: the receiver pattern.</summary>
    public string? ExtensionTarget { get; set; }
    public BoundType? ReceiverPattern { get; set; }
    public bool IsExtensionProperty { get; set; }

    public string Signature =>
        $"{ReturnType} {Name}({string.Join(", ", Parameters.Select((p, i) => (IsVariadic && i == Parameters.Count - 1 ? p.Type + "..." : p.Type.ToString()) + " " + p.Name))})";
}

/// <summary>Overload set (native functions and methods; script functions never overload).</summary>
public sealed class FunctionGroupSymbol(string name) : Symbol(name, SymbolKind.FunctionGroup)
{
    public List<FunctionSymbol> Overloads { get; } = [];
}

public sealed class TypeParamSymbol(string name) : Symbol(name, SymbolKind.TypeParam)
{
    private static int _next;
    public int Uid { get; } = Interlocked.Increment(ref _next);
}

public sealed class EnumMemberSymbol(string name, TypeSymbol owner, int ordinal) : Symbol(name, SymbolKind.EnumMember)
{
    public TypeSymbol Owner { get; } = owner;
    public int Ordinal { get; } = ordinal;
}

/// <summary>Class, struct, interface or enum (script-defined or native).</summary>
public sealed class TypeSymbol(string name, SymbolKind kind, string id) : Symbol(name, kind)
{
    /// <summary>Unique runtime id (<c>Dog</c> or <c>module::Dog</c>).</summary>
    public string Id { get; } = id;

    public IStmt? Decl { get; set; }
    public List<TypeParamSymbol> TypeParams { get; } = [];
    public BoundType? BaseType { get; set; }
    public List<BoundType> Interfaces { get; } = [];

    public Dictionary<string, Symbol> Members { get; } = new();
    public List<FieldSymbol> Fields { get; } = [];
    public List<FunctionSymbol> Methods { get; } = [];
    public FunctionSymbol? Constructor { get; set; }

    /// <summary>Synthetic function that runs field initialisers (this = slot 0). Null when there is nothing to run.</summary>
    public FunctionSymbol? Initializer { get; set; }
    public List<string> EnumMembers { get; } = [];
    public NativeClass? Native { get; set; }

    public TypeSymbol? BaseSymbol => BaseType?.Symbol as TypeSymbol;

    public bool IsSubclassOf(TypeSymbol other)
    {
        for (var t = this; t is not null; t = t.BaseSymbol)
            if (ReferenceEquals(t, other)) return true;
        return false;
    }

    /// <summary>All interfaces implemented directly or through base classes / parent interfaces.</summary>
    public IEnumerable<TypeSymbol> AllInterfaces()
    {
        var seen = new HashSet<TypeSymbol>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<TypeSymbol>();
        for (var t = this; t is not null; t = t.BaseSymbol)
        {
            foreach (var i in t.Interfaces)
                if (i.Symbol is TypeSymbol ts) stack.Push(ts);
        }
        while (stack.Count > 0)
        {
            var i = stack.Pop();
            if (!seen.Add(i)) continue;
            yield return i;
            foreach (var p in i.Interfaces)
                if (p.Symbol is TypeSymbol ps) stack.Push(ps);
        }
    }

    public bool Implements(TypeSymbol iface) =>
        ReferenceEquals(this, iface) || AllInterfaces().Any(i => ReferenceEquals(i, iface));
}

public sealed class ModuleSymbol(string name) : Symbol(name, SymbolKind.Module)
{
    public Dictionary<string, Symbol> Exports { get; } = new();

    /// <summary>The module's own name (differs from <see cref="Symbol.Name"/> when imported with an alias).</summary>
    public string ModuleName { get; set; } = name;
}
