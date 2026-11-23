namespace SabakaLang.Extensions;

/// <summary>Implement this in your assembly to expose a module without attributes; the engine finds it by scanning the assembly.</summary>
public interface ISabakaExtension
{
    string Name { get; }
    void Configure(ModuleBuilder module);
}

/// <summary>Fluent API for describing a native module.</summary>
public sealed class ModuleBuilder(string name)
{
    private string? _description;
    private bool _autoImport;
    private readonly List<NativeFunction> _functions = [];
    private readonly List<NativeClass> _classes = [];
    private readonly List<NativeConstant> _constants = [];
    private readonly List<NativeEnum> _enums = [];
    private readonly List<NativeExtension> _extensions = [];

    public string Name { get; } = name;

    public ModuleBuilder Description(string text) { _description = text; return this; }

    /// <summary>Make the module's members visible in every script without <c>import</c>.</summary>
    public ModuleBuilder AutoImport(bool value = true) { _autoImport = value; return this; }

    /// <summary>Global function. <c>signature</c> example: <c>"float pow(float x, float y)"</c>. Call again with the same name for overloads.</summary>
    public ModuleBuilder Function(string signature, NativeFn implementation, string? description = null)
    {
        _functions.Add(new NativeFunction
        {
            Module = Name, Signature = NativeSignature.Parse(signature), Implementation = implementation, Description = description
        });
        return this;
    }

    /// <summary>Constant: <c>.Constant("float PI", Value.FromFloat(Math.PI))</c>.</summary>
    public ModuleBuilder Constant(string typeAndName, Value value)
    {
        var (type, n) = ParseMember(typeAndName);
        _constants.Add(new NativeConstant(n, type, value));
        return this;
    }

    public ModuleBuilder Enum(string enumName, params string[] members)
    {
        _enums.Add(new NativeEnum(enumName, members));
        return this;
    }

    /// <summary>Native class with constructor, fields, properties, instance and static methods.</summary>
    public ModuleBuilder Class(string className, Action<ClassBuilder> configure, string? description = null)
    {
        var cb = new ClassBuilder(Name, className, null, description);
        configure(cb);
        _classes.Add(cb.Build());
        return this;
    }

    /// <summary>Adds a method to a built-in type. <c>target</c>: <c>string</c>, <c>int</c>, <c>T[]</c>, <c>dynamic</c>...</summary>
    public ModuleBuilder Extension(string target, string signature, NativeMethodFn implementation)
    {
        _extensions.Add(new NativeExtension
        {
            Module = Name, Target = TypeSyntax.Parse(target).ToString(), Signature = NativeSignature.Parse(signature), Implementation = implementation
        });
        return this;
    }

    /// <summary>Adds a property (no parentheses) to a built-in type: <c>.ExtensionProperty("string", "bool isEmpty", ...)</c>.</summary>
    public ModuleBuilder ExtensionProperty(string target, string typeAndName, NativeMethodFn getter)
    {
        var (type, n) = ParseMember(typeAndName);
        _extensions.Add(new NativeExtension
        {
            Module = Name, Target = TypeSyntax.Parse(target).ToString(), IsProperty = true,
            Signature = new NativeSignature(n, type, []), Implementation = getter
        });
        return this;
    }

    internal ModuleBuilder AddFunction(NativeFunction f) { _functions.Add(f); return this; }
    internal ModuleBuilder AddClass(NativeClass c) { _classes.Add(c); return this; }
    internal ModuleBuilder AddConstant(NativeConstant c) { _constants.Add(c); return this; }
    internal ModuleBuilder AddEnum(NativeEnum e) { _enums.Add(e); return this; }
    internal ModuleBuilder AddExtension(NativeExtension e) { _extensions.Add(e); return this; }

    public NativeModule Build() => new()
    {
        Name = Name, Description = _description, AutoImport = _autoImport,
        Functions = _functions.ToList(), Classes = _classes.ToList(), Constants = _constants.ToList(),
        Enums = _enums.ToList(), Extensions = _extensions.ToList()
    };

    internal static (string Type, string Name) ParseMember(string text)
    {
        var t = text.Trim();
        var i = t.Length;
        while (i > 0 && (char.IsLetterOrDigit(t[i - 1]) || t[i - 1] == '_')) i--;
        if (i == 0 || i == t.Length) throw new ArgumentException($"Expected 'type name', got '{text}'.");
        return (TypeSyntax.Parse(t[..i].Trim()).ToString(), t[i..]);
    }
}

public sealed class ClassBuilder
{
    private readonly NativeClass _class;

    internal ClassBuilder(string module, string name, Type? clr, string? description) =>
        _class = new NativeClass { Module = module, Name = name, ClrType = clr, Description = description };

    /// <summary>Script-visible field, stored in the object's field table (<c>self.Fields["name"]</c>).</summary>
    public ClassBuilder Field(string typeAndName, Value defaultValue = default)
    {
        var (type, n) = ModuleBuilder.ParseMember(typeAndName);
        _class.Fields.Add(new NativeField(n, type, defaultValue));
        return this;
    }

    /// <summary>Constructor: <c>.Constructor("(string name, int age)", (ctx, self, a) => { ... })</c>.</summary>
    public ClassBuilder Constructor(string signature, NativeCtorFn implementation)
    {
        _class.Constructors.Add(new NativeConstructor
        {
            Class = _class.Name, Signature = NativeSignature.Parse(signature), Implementation = implementation
        });
        return this;
    }

    public ClassBuilder Method(string signature, NativeMethodFn implementation)
    {
        _class.Methods.Add(new NativeMethod { Class = _class.Name, Signature = NativeSignature.Parse(signature), Implementation = implementation });
        return this;
    }

    /// <summary>Static method, called as <c>ClassName.method(...)</c>.</summary>
    public ClassBuilder StaticMethod(string signature, NativeMethodFn implementation)
    {
        _class.Methods.Add(new NativeMethod { Class = _class.Name, Signature = NativeSignature.Parse(signature), Implementation = implementation, IsStatic = true });
        return this;
    }

    /// <summary>Computed property. <c>setter</c> may be null for read-only.</summary>
    public ClassBuilder Property(string typeAndName, Func<NativeContext, Value, Value> getter, Action<NativeContext, Value, Value>? setter = null)
    {
        var (type, n) = ModuleBuilder.ParseMember(typeAndName);
        _class.Properties.Add(new NativeProperty { Class = _class.Name, Name = n, Type = type, Getter = getter, Setter = setter });
        return this;
    }

    internal NativeClass Build() => _class;
    internal NativeClass Class => _class;
}
