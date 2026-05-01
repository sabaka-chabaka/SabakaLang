namespace SabakaLang.Compiler.Compiling;

public sealed class Instruction(
    OpCode opCode,
    object? operand = null,
    string? name = null,
    object? extra = null)
{
    public OpCode  OpCode  { get; } = opCode;
    public object? Operand { get; set; } = operand;
    public string? Name    { get; } = name;
    public object? Extra   { get; } = extra;

    public override string ToString()
    {
        var parts = new List<string> { OpCode.ToString() };
        if (Name    is not null) parts.Add($"'{Name}'");
        if (Operand is not null) parts.Add(Operand.ToString()!);
        return string.Join(" ", parts);
    }
}
