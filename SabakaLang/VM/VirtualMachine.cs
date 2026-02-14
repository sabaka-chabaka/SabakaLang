using SabakaLang.Compiler;

namespace SabakaLang.VM;

public class VirtualMachine
{
    private readonly Stack<double> _stack = new();

    public double Execute(List<Instruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            switch (instruction.Code)
            {
                case OpCode.Push:
                    _stack.Push(instruction.Operand);
                    break;

                case OpCode.Add:
                    _stack.Push(_stack.Pop() + _stack.Pop());
                    break;

                case OpCode.Sub:
                {
                    var b = _stack.Pop();
                    var a = _stack.Pop();
                    _stack.Push(a - b);
                    break;
                }

                case OpCode.Mul:
                    _stack.Push(_stack.Pop() * _stack.Pop());
                    break;

                case OpCode.Div:
                {
                    var b = _stack.Pop();
                    var a = _stack.Pop();
                    _stack.Push(a / b);
                    break;
                }
                case OpCode.Print:
                {
                    var value = _stack.Pop();
                    Console.WriteLine(value);
                    break;
                }
            }
        }

        return _stack.Count > 0 ? _stack.Peek() : 0;
    }
}
