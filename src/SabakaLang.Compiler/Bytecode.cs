using System.Text;
using SabakaLang.Runtime;

namespace SabakaLang.Compiler;

/// <summary>
/// Stack-machine instructions. Every expression leaves exactly one value on the stack; statements leave none.
/// Local variables live in numbered frame slots (slot 0 is <c>this</c> in methods), module-level variables in a global table.
/// </summary>
public enum OpCode : byte
{
    Nop,

    // stack & constants
    Push,         // Arg = Value
    Pop,
    Dup,
    Dup2,         // duplicate the two top values: a b -> a b a b

    // variables
    LoadLocal,    // A = slot
    StoreLocal,   // A = slot, pops the value
    LoadGlobal,   // A = global index
    StoreGlobal,  // A = global index, pops the value

    // objects & arrays
    LoadField,    // Arg = name; pops object (also: array/string 'length', native properties)
    StoreField,   // Arg = name; pops value, object; B = 1 keeps the value as result
    LoadIndex,    // pops index, array/string
    StoreIndex,   // pops value, index, array; B = 1 keeps the value as result
    Length,       // pops array/string, pushes its length
    BuildArray,   // A = element count
    NewArray,     // pops size; Arg = default element Value; A = struct class index or -1
    NewObject,    // A = class index; allocates, applies field defaults and initialisers
    CloneStruct,

    // arithmetic & logic
    Add, Sub, Mul, Div, Mod, Neg, Not,
    Eq, Ne, Lt, Gt, Le, Ge,

    // control flow (A = target instruction index inside the function)
    Jump,
    JumpIfFalse,      // pops condition
    JumpIfTrue,       // pops condition
    JumpIfFalseKeep,  // '&&': if top is false jump keeping it, else pop it
    JumpIfTrueKeep,   // '||'
    JumpIfNotNullKeep,// '??'

    // calls (B = number of arguments, receiver included where noted)
    Call,         // A = function index; B = args (incl. receiver for methods)
    CallDirect,   // like Call: non-virtual method / base constructor call
    CallVirtual,  // Arg = method name; B = args excluding receiver; receiver below the args
    CallDynamic,  // Arg = name; B = args excluding receiver; receiver type unknown at compile time
    CallNative,   // A = native index; B = args (incl. receiver for instance natives)
    NewNative,    // A = native constructor index; B = args; allocates the native class instance
    Return,       // pops the return value

    // conversions & tests
    ToFloat,
    CharToString,
    Stringify,    // value -> string (honours user toString())
    Concat,       // A = number of strings on the stack
    IsType,       // Arg = TypeTest

    Halt
}

public readonly record struct Instruction(OpCode Op, int A = 0, int B = 0, object? Arg = null, int Line = 0)
{
    public override string ToString() =>
        $"{Op}{(A != 0 ? " " + A : "")}{(B != 0 ? " ," + B : "")}{(Arg is not null ? " " + (Arg is Value v && v.Type == SabakaType.String ? "\"" + v + "\"" : Arg) : "")}";
}

/// <summary>Runtime type test used by <c>is</c>.</summary>
public sealed record TypeTest(string Kind, string? Name = null, bool Nullable = false)
{
    public override string ToString() => Name is null ? Kind : $"{Kind}:{Name}";
}

public sealed class CompiledFunction
{
    public required string Name { get; init; }

    /// <summary>Number of values the caller passes (receiver included for methods).</summary>
    public required int ArgCount { get; init; }

    public int LocalCount { get; set; }
    public Instruction[] Code { get; set; } = [];
}

public sealed class FieldInfo
{
    public required string Name { get; init; }
    public Value Default { get; init; }

    /// <summary>Class index of a struct-typed field: every instance gets its own default struct.</summary>
    public int StructClass { get; init; } = -1;
}

public sealed class CompiledClass
{
    public required string Id { get; init; }
    public string? Base { get; init; }
    public bool IsStruct { get; init; }
    public IReadOnlyList<string> Interfaces { get; init; } = [];
    public List<FieldInfo> Fields { get; } = [];
    public Dictionary<string, int> Methods { get; } = new();
    public int Constructor { get; set; } = -1;
    public int Initializer { get; set; } = -1;
}

/// <summary>Reference to a native member, resolved against the extension registry when the program is loaded.</summary>
public sealed record NativeRef(string Key, string? ClassName = null);

public sealed class CompiledProgram
{
    public List<CompiledFunction> Functions { get; } = [];
    public List<CompiledClass> Classes { get; } = [];
    public List<NativeRef> Natives { get; } = [];

    /// <summary>Initial values of the global table (typed defaults).</summary>
    public List<Value> Globals { get; } = [];

    /// <summary>Indices of the module entry functions in execution order (dependencies first).</summary>
    public List<int> Entries { get; } = [];

    public string Disassemble()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < Functions.Count; i++)
        {
            var f = Functions[i];
            sb.AppendLine($"function #{i} {f.Name}  (args {f.ArgCount}, locals {f.LocalCount})");
            for (var j = 0; j < f.Code.Length; j++) sb.AppendLine($"  {j,4}: {f.Code[j]}");
        }
        for (var i = 0; i < Classes.Count; i++)
        {
            var c = Classes[i];
            sb.AppendLine($"class #{i} {c.Id}{(c.Base is null ? "" : " : " + c.Base)} fields=[{string.Join(", ", c.Fields.Select(f => f.Name))}] methods=[{string.Join(", ", c.Methods.Keys)}]");
        }
        return sb.ToString();
    }
}
