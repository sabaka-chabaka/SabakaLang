namespace SabakaLang.Compiler;

public class Instruction
{
    public OpCode Code { get; }
    public double Operand { get; }

    public Instruction(OpCode code, double operand = 0)
    {
        Code = code;
        Operand = operand;
    }

    public override string ToString()
    {
        return Code == OpCode.Push
            ? $"Push {Operand}"
            : Code.ToString();
    }
}