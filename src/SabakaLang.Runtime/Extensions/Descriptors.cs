namespace SabakaLang.Extensions;

/// <summary>A global function contributed by a module: <c>sqrt(2.0)</c>.</summary>
public sealed class NativeFunction : INativeCallable
{
    public required string Module { get; init; }
    public required NativeSignature Signature { get; init; }
    public required NativeFn Implementation { get; init; }
    public string? Description { get; init; }
    public string Name => Signature.Name;
    public string Key => $"{Module}.{Name}({Signature.ParameterKey})";
    public Value Invoke(NativeContext ctx, Value[] args) => Implementation(ctx, args);
}

/// <summary>Constructor of a native class. <c>args[0]</c> is the freshly allocated instance.</summary>
public sealed class NativeConstructor : INativeCallable
{
    public required string Class { get; init; }
    public required NativeSignature Signature { get; init; }
    public required NativeCtorFn Implementation { get; init; }
    public string Key => $"{Class}.new({Signature.ParameterKey})";

    public Value Invoke(NativeContext ctx, Value[] args)
    {
        var self = args[0].Object ?? throw new RuntimeException($"Constructor of '{Class}' called without an instance");
        Implementation(ctx, self, args[1..]);
        return args[0];
    }
}

/// <summary>Instance or static method of a native class.</summary>
public sealed class NativeMethod : INativeCallable
{
    public required string Class { get; init; }
    public required NativeSignature Signature { get; init; }
    public required NativeMethodFn Implementation { get; init; }
    public bool IsStatic { get; init; }
    public string Name => Signature.Name;
    public string Key => $"{Class}.{Name}({Signature.ParameterKey})";

    public Value Invoke(NativeContext ctx, Value[] args) =>
        IsStatic ? Implementation(ctx, Value.Null, args) : Implementation(ctx, args[0], args[1..]);
}

/// <summary>Computed property of a native class (<c>v.length</c>), backed by C# getter/setter delegates.</summary>
public sealed class NativeProperty
{
    public required string Class { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required Func<NativeContext, Value, Value> Getter { get; init; }
    public Action<NativeContext, Value, Value>? Setter { get; init; }
    public bool CanWrite => Setter is not null;
    public INativeCallable GetAccessor => new Accessor($"{Class}.get_{Name}", (c, a) => Getter(c, a[0]));
    public INativeCallable? SetAccessor => Setter is null ? null : new Accessor($"{Class}.set_{Name}", (c, a) => { Setter(c, a[0], a[1]); return a[1]; });
}

/// <summary>Plain script-visible field of a native class, stored in <see cref="SabakaObject.Fields"/>.</summary>
public sealed record NativeField(string Name, string Type, Value Default = default);

public sealed class NativeClass
{
    public required string Module { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }

    /// <summary>The C# type this class wraps, when it was created via attributes/reflection.</summary>
    public Type? ClrType { get; init; }

    public List<NativeConstructor> Constructors { get; } = [];
    public List<NativeMethod> Methods { get; } = [];
    public List<NativeProperty> Properties { get; } = [];
    public List<NativeField> Fields { get; } = [];
}

public sealed record NativeConstant(string Name, string Type, Value Value);

public sealed record NativeEnum(string Name, IReadOnlyList<string> Members, Type? ClrType = null);

/// <summary>
/// A method (or property) added to a built-in type: <c>"abc".shout()</c>, <c>list.first()</c>.
/// <see cref="Target"/> is a type pattern: <c>string</c>, <c>int</c>, <c>T[]</c> (any array), <c>int[]</c>, <c>dynamic</c> (anything).
/// <c>args[0]</c> is always the receiver.
/// </summary>
public sealed class NativeExtension : INativeCallable
{
    public required string Module { get; init; }
    public required string Target { get; init; }
    public required NativeSignature Signature { get; init; }
    public required NativeMethodFn Implementation { get; init; }

    /// <summary>Accessed without parentheses (<c>text.shout</c>) – like the built-in <c>length</c>.</summary>
    public bool IsProperty { get; init; }

    public string Name => Signature.Name;
    public string Key => $"{Module}.ext[{Target}].{Name}({Signature.ParameterKey})";
    public Value Invoke(NativeContext ctx, Value[] args) => Implementation(ctx, args[0], args[1..]);

    /// <summary>Runtime test used for calls on <c>dynamic</c> receivers.</summary>
    public bool AcceptsRuntime(Value v)
    {
        var t = TypeSyntax.TryParse(Target);
        if (t is null) return false;
        if (t.ArrayDepth > 0) return v.Type == SabakaType.Array;
        return t.Name switch
        {
            "dynamic" or "any" => true,
            "int" => v.Type == SabakaType.Int,
            "float" => v.Type is SabakaType.Float or SabakaType.Int,
            "bool" => v.Type == SabakaType.Bool,
            "string" => v.Type == SabakaType.String,
            "char" => v.Type == SabakaType.Char,
            _ => v.Type == SabakaType.Object && v.Object!.ClassName == t.Name || v.Type == SabakaType.Enum && v.Enum!.EnumName == t.Name
        };
    }
}

internal sealed class Accessor(string key, Func<NativeContext, Value[], Value> fn) : INativeCallable
{
    public string Key { get; } = key;
    public Value Invoke(NativeContext ctx, Value[] args) => fn(ctx, args);
}

/// <summary>Everything a module contributes to the language.</summary>
public sealed class NativeModule
{
    public required string Name { get; init; }
    public string? Description { get; init; }

    /// <summary>Visible in every script without an <c>import</c> (the standard library uses this).</summary>
    public bool AutoImport { get; init; }

    public IReadOnlyList<NativeFunction> Functions { get; init; } = [];
    public IReadOnlyList<NativeClass> Classes { get; init; } = [];
    public IReadOnlyList<NativeConstant> Constants { get; init; } = [];
    public IReadOnlyList<NativeEnum> Enums { get; init; } = [];
    public IReadOnlyList<NativeExtension> Extensions { get; init; } = [];

    /// <summary>All callables of the module (used by the registry for bytecode linking).</summary>
    public IEnumerable<INativeCallable> AllCallables()
    {
        foreach (var f in Functions) yield return f;
        foreach (var e in Extensions) yield return e;
        foreach (var c in Classes)
        {
            foreach (var k in c.Constructors) yield return k;
            foreach (var m in c.Methods) yield return m;
            foreach (var p in c.Properties)
            {
                yield return p.GetAccessor;
                if (p.SetAccessor is { } s) yield return s;
            }
        }
    }
}
