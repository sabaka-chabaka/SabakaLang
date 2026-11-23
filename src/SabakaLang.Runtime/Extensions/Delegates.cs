namespace SabakaLang.Extensions;

/// <summary>Implementation of a native function. <paramref name="args"/> holds exactly the declared arguments
/// (all trailing arguments for a variadic signature).</summary>
public delegate Value NativeFn(NativeContext ctx, Value[] args);

/// <summary>Implementation of a native instance method / extension method. <paramref name="self"/> is the receiver.</summary>
public delegate Value NativeMethodFn(NativeContext ctx, Value self, Value[] args);

/// <summary>Native constructor: initialise <paramref name="self"/> (set <see cref="SabakaObject.Native"/> and/or fields).</summary>
public delegate void NativeCtorFn(NativeContext ctx, SabakaObject self, Value[] args);

/// <summary>Anything the VM can call by its stable key.</summary>
public interface INativeCallable
{
    /// <summary>Stable identifier stored in compiled bytecode, e.g. <c>math.abs(int)</c>.</summary>
    string Key { get; }

    /// <summary>Invokes the member. For instance members <c>args[0]</c> is the receiver.</summary>
    Value Invoke(NativeContext ctx, Value[] args);
}
