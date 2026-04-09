namespace SabakaLang.Compiler;

public enum OpCode
{
    Push,
    Pop,
    Dup,
    
    Add, Sub, Mul, Div, Mod,
    
    Equal, NotEqual, Less, GreaterEqual, LessEqual,
    
    And, Or, Not, Negate,
    
    Declare,
    Load,
    Store,
    
    EnterScope,
    ExitScope,
    
    Jump,
    JumpIfFalse,
    JumpIfTrue,
    
    Function,
    Call,
    Return,
    
    CreateObject,
    CallMethod,
    LoadField,
    StoreField,
    PushThis,
    Inherit,
    
    CreateArray,
    ArrayLoad,
    ArrayStore,
    ArrayLength,
    
    CreateStruct,
    
    PushEnum,
    
    Print,
    Input,
    Sleep,
    ReadFile, WriteFile, AppendFile, FileExists, DeleteFile, ReadLines,
    Time, TimeMs,
    HttpGet, HttpPost, HttpPostJson,
    Ord, Chr,
 
    CallExternal
}

public sealed class Instruction
{
    public OpCode OpCode { get; }
    public object? Operand { get; }
    public string? Name { get; }
    public object? Extra { get; }
    
    public Instruction(OpCode opCode, object? operand = null, string? name = null, object? extra = null)
    {
        OpCode = opCode;
        Operand = operand;
        Name = name;
        Extra = extra;
    }
    
    public override string ToString()
    {
        var parts = new List<string> { OpCode.ToString() };
        if (Name    is not null) parts.Add($"'{Name}'");
        if (Operand is not null) parts.Add(Operand.ToString()!);
        return string.Join(" ", parts);
    }
}

internal sealed class ClassMeta
{
    public string  Name       { get; }
    public string? Base       { get; }
    public List<string>         Fields  { get; } = [];
    public List<VarDecl>        FieldDecls { get; } = [];
    public List<FuncDecl>       Methods { get; } = [];
    public List<string>         Interfaces { get; } = [];
    public ClassMeta(string name, string? @base) { Name = name; Base = @base; }
}

public readonly record struct CompileError(string Message, Position Position)
{
    public override string ToString() => $"[{Position.Line}:{Position.Column}] {Message}";
}

public sealed class CompileResult
{
    public IReadOnlyList<Instruction>  Code   { get; }
    public IReadOnlyList<CompileError> Errors { get; }
    public bool HasErrors => Errors.Count > 0;
 
    public CompileResult(IReadOnlyList<Instruction> code, IReadOnlyList<CompileError> errors)
    {
        Code   = code;
        Errors = errors;
    }
}

public sealed class Compiler
{
    
}