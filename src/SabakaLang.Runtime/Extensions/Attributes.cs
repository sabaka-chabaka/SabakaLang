namespace SabakaLang.Extensions;

/// <summary>Marks a (static) class as a SabakaLang module; its tagged members become importable via <c>import "name";</c>.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
public sealed class SabakaModuleAttribute(string name) : Attribute
{
    public string Name { get; } = name;
    public string? Description { get; set; }

    /// <summary>Members are visible in every script without <c>import</c>.</summary>
    public bool AutoImport { get; set; }
}

/// <summary>Static method exposed as a global function. Name defaults to the camelCased C# name.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SabakaFunctionAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;

    /// <summary>Override the inferred signature, e.g. <c>"T first(T[] items)"</c> for generic functions.</summary>
    public string? Signature { get; set; }
    public string? Description { get; set; }
}

/// <summary>C# class exposed as a SabakaLang class. Nest it in a [SabakaModule] class or set <see cref="Module"/>.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SabakaClassAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
    public string? Module { get; set; }
    public string? Description { get; set; }
}

/// <summary>Public constructor available to scripts via <c>new</c>. If no constructor is tagged, all public ones are exposed.</summary>
[AttributeUsage(AttributeTargets.Constructor)]
public sealed class SabakaConstructorAttribute : Attribute;

/// <summary>Instance method (or static method → static member) of a [SabakaClass].</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SabakaMethodAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
    public string? Signature { get; set; }
}

/// <summary>C# property or field exposed as a property of a [SabakaClass].</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class SabakaPropertyAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
}

/// <summary>
/// Static method that adds a method to a built-in type. The first parameter (after an optional <see cref="NativeContext"/>)
/// is the receiver: <c>[SabakaExtension("string")] static string Shout(string self)</c> enables <c>"hi".shout()</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SabakaExtensionAttribute(string target, string? name = null) : Attribute
{
    public string Target { get; } = target;
    public string? Name { get; } = name;
    public string? Signature { get; set; }

    /// <summary>Expose as a property (no parentheses). The method must take only the receiver.</summary>
    public bool IsProperty { get; set; }
}

/// <summary>Static field/property/const exposed as a module constant.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class SabakaConstantAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
}

/// <summary>C# enum exposed as a SabakaLang enum.</summary>
[AttributeUsage(AttributeTargets.Enum)]
public sealed class SabakaEnumAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
    public string? Module { get; set; }
}
