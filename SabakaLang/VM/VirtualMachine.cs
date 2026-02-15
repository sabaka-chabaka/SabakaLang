using SabakaLang.Compiler;

namespace SabakaLang.VM;

public class VirtualMachine
{
    private readonly Stack<double> _stack = new();
    private Dictionary<string, double> _variables = new();

    public void Execute(List<Instruction> instructions)
    {
        int ip = 0;

        while (ip < instructions.Count)
        {
            var instruction = instructions[ip];

            switch (instruction.OpCode)
            {
                case OpCode.Push:
                    _stack.Push(instruction.Operand);
                    break;

                case OpCode.Add:
                {
                    var b = _stack.Pop();
                    var a = _stack.Pop();
                    _stack.Push(a + b);
                    break;
                }

                case OpCode.Store:
                {
                    var value = _stack.Pop();
                    _variables[instruction.Name!] = value;
                    break;
                }

                case OpCode.Load:
                {
                    var value = _variables[instruction.Name!];
                    _stack.Push(value);
                    break;
                }

                case OpCode.Print:
                {
                    var value = _stack.Pop();
                    Console.WriteLine(value);
                    break;
                }

                case OpCode.Jump:
                    ip = (int)instruction.Operand;
                    continue;

                case OpCode.JumpIfFalse:
                {
                    var condition = _stack.Pop();

                    if (condition == 0)
                    {
                        ip = (int)instruction.Operand;
                        continue;
                    }

                    break;
                }

                
                case OpCode.Equal:
                {
                    var b = _stack.Pop();
                    var a = _stack.Pop();
                    _stack.Push(a == b ? 1 : 0);
                    break;
                }

                case OpCode.NotEqual:
                {
                    var right = _stack.Pop();
                    var left = _stack.Pop();

                    _stack.Push(left != right ? 1 : 0);
                    break;
                }

                case OpCode.Greater:
                {
                    var b = _stack.Pop();
                    var a = _stack.Pop();
                    _stack.Push(a > b ? 1 : 0);
                    break;
                }

                case OpCode.Less:
                {
                    var b = _stack.Pop();
                    var a = _stack.Pop();
                    _stack.Push(a < b ? 1 : 0);
                    break;
                }

                case OpCode.GreaterEqual:
                {
                    var b = _stack.Pop();
                    var a = _stack.Pop();
                    _stack.Push(a >= b ? 1 : 0);
                    break;
                }

                case OpCode.LessEqual:
                {
                    var b = _stack.Pop();
                    var a = _stack.Pop();
                    _stack.Push(a <= b ? 1 : 0);
                    break;
                }

            }

            ip++;
        }
    }
}
