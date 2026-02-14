namespace SabakaLang.Compiler;

public class Instruction
{
    public OpCode OpCode { get; }
    public double Operand { get; set; }
    public string? Name { get; set; }

    public Instruction(OpCode opCode, double operand = 0)
    {
        OpCode = opCode;
        Operand = operand;
    }
}