namespace SabakaLang.Extensions;

/// <summary>
/// What a native function sees of the running VM: standard streams, the host object, and the ability to call back into script code.
/// </summary>
public abstract class NativeContext
{
    public abstract TextWriter Out { get; }
    public abstract TextReader In { get; }
    public abstract ExtensionRegistry Registry { get; }

    /// <summary>Arbitrary object supplied by the embedding application (e.g. your game/app services).</summary>
    public abstract object? Host { get; }

    /// <summary>Calls a global script function (or native function) by name.</summary>
    public abstract Value CallFunction(string name, params Value[] args);

    /// <summary>Calls a method on a script/native object or an extension method on a built-in value.</summary>
    public abstract Value CallMethod(Value target, string name, params Value[] args);

    /// <summary>Instantiates a script or native class by name.</summary>
    public abstract Value NewObject(string className, params Value[] args);

    /// <summary>String form of a value; honours a user-defined <c>toString()</c> method.</summary>
    public abstract string Stringify(Value value);

    /// <summary>Allocates an object tracked by the VM's garbage collector.</summary>
    protected abstract SabakaObject Allocate(string className);

    /// <summary>Wraps a C# instance of a registered native class so scripts can use it.</summary>
    public SabakaObject Wrap(object native)
    {
        var cls = Registry.FindClassByClrType(native.GetType())
                  ?? throw new RuntimeException($"C# type '{native.GetType().Name}' is not registered as a SabakaLang class (missing [SabakaClass]?)");
        var obj = Allocate(cls.Name);
        foreach (var f in cls.Fields) obj.Fields[f.Name] = f.Default;
        obj.Native = native;
        return obj;
    }

    /// <summary>Creates a native-class instance without running a constructor (state is set by the caller).</summary>
    public SabakaObject NewNativeObject(NativeClass cls, object? state = null)
    {
        var obj = Allocate(cls.Name);
        foreach (var f in cls.Fields) obj.Fields[f.Name] = f.Default;
        obj.Native = state;
        return obj;
    }

    public RuntimeException Error(string message) => new(message);
}
