namespace SabakaLang.Runtime;

/// <summary>
/// An instance of a script class/struct or of a native (C#) class.
/// Script data lives in <see cref="Fields"/>; native classes may additionally keep arbitrary C# state in <see cref="Native"/>.
/// </summary>
public sealed class SabakaObject(string className)
{
    /// <summary>Unique class id. For classes of imported source modules this is <c>module::Name</c>.</summary>
    public string ClassName { get; } = className;

    public Dictionary<string, Value> Fields { get; } = new();

    /// <summary>C# state behind a native class instance (the object an extension class wraps).</summary>
    public object? Native { get; set; }

    /// <summary>Structs have value semantics: they are copied on assignment, argument passing and return.</summary>
    public bool IsStruct { get; init; }

    /// <summary>Class name without the module prefix.</summary>
    public string ShortName => DisplayName(ClassName);

    public static string DisplayName(string className)
    {
        var i = className.LastIndexOf("::", StringComparison.Ordinal);
        return i < 0 ? className : className[(i + 2)..];
    }

    public SabakaObject Clone()
    {
        var c = new SabakaObject(ClassName) { IsStruct = IsStruct, Native = Native };
        foreach (var kv in Fields)
            c.Fields[kv.Key] = kv.Value.Object is { IsStruct: true } nested ? Value.FromObject(nested.Clone()) : kv.Value;
        return c;
    }

    public T? NativeAs<T>() where T : class => Native as T;

    public override string ToString() =>
        Native is not null && Fields.Count == 0
            ? Native.ToString() ?? ShortName
            : $"{ShortName} {{ {string.Join(", ", Fields.Select(kv => $"{kv.Key}: {kv.Value}"))} }}";
}
