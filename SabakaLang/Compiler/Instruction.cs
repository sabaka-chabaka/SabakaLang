using SabakaLang.Types;

namespace SabakaLang.Compiler;

public class Instruction
{
    public OpCode OpCode { get; set; }
    public object? Operand { get; set; }
    public string? Name { get; set; }
    public object? Extra { get; set; }


    public Instruction(OpCode opCode, object? operand = null, string? name = null)
    {
        OpCode = opCode;
        Operand = operand;
        Name = name;
    }

    public Instruction()
    {
    }
}
