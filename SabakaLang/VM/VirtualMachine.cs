using SabakaLang.Compiler;
using SabakaLang.Types;

namespace SabakaLang.VM;

public class VirtualMachine
{
    private readonly Stack<Value> _stack = new();
    private readonly Dictionary<string, Value> _variables = new();

    public void Execute(List<Instruction> instructions)
    {
        int ip = 0;

        while (ip < instructions.Count)
        {
            var instruction = instructions[ip];

            switch (instruction.OpCode)
            {
                case OpCode.Push:
                    _stack.Push((Value)instruction.Operand!);
                    break;

                case OpCode.Add:
                    BinaryNumeric((a, b) => a + b);
                    break;

                case OpCode.Sub:
                    BinaryNumeric((a, b) => a - b);
                    break;

                case OpCode.Mul:
                    BinaryNumeric((a, b) => a * b);
                    break;

                case OpCode.Div:
                    BinaryNumeric((a, b) => a / b);
                    break;

                case OpCode.Store:
                {
                    var value = _stack.Pop();
                    _variables[instruction.Name!] = value;
                    break;
                }

                case OpCode.Load:
                {
                    if (!_variables.TryGetValue(instruction.Name!, out var value))
                        throw new Exception($"Undefined variable '{instruction.Name}'");

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
                    ip = (int)instruction.Operand!;
                    continue;

                case OpCode.JumpIfFalse:
                {
                    var condition = _stack.Pop();

                    if (condition.Type != SabakaType.Bool)
                        throw new Exception("Condition must be bool");

                    if (!condition.Bool)
                    {
                        ip = (int)instruction.Operand!;
                        continue;
                    }

                    break;
                }

                case OpCode.Equal:
                {
                    var b = _stack.Pop();
                    var a = _stack.Pop();

                    if (a.Type != b.Type)
                        throw new Exception("Type mismatch in ==");

                    bool result = a.Type switch
                    {
                        SabakaType.Int => a.Int == b.Int,
                        SabakaType.Float => a.Float == b.Float,
                        SabakaType.Bool => a.Bool == b.Bool,
                        _ => throw new Exception("Invalid type for ==")
                    };

                    _stack.Push(Value.FromBool(result));
                    break;
                }


                case OpCode.NotEqual:
                {
                    var b = _stack.Pop();
                    var a = _stack.Pop();

                    if (a.Type != b.Type)
                        throw new Exception("Type mismatch in !=");

                    bool result = a.Type switch
                    {
                        SabakaType.Int => a.Int != b.Int,
                        SabakaType.Float => a.Float != b.Float,
                        SabakaType.Bool => a.Bool != b.Bool,
                        _ => throw new Exception("Invalid type for !=")
                    };

                    _stack.Push(Value.FromBool(result));
                    break;
                }


                case OpCode.Greater:
                    CompareNumeric((a, b) => a > b);
                    break;

                case OpCode.Less:
                    CompareNumeric((a, b) => a < b);
                    break;

                case OpCode.GreaterEqual:
                    CompareNumeric((a, b) => a >= b);
                    break;

                case OpCode.LessEqual:
                    CompareNumeric((a, b) => a <= b);
                    break;

                case OpCode.Negate:
                {
                    var value = _stack.Pop();

                    if (value.Type == SabakaType.Int)
                        _stack.Push(Value.FromInt(-value.Int));
                    else if (value.Type == SabakaType.Float)
                        _stack.Push(Value.FromFloat(-value.Float));
                    else
                        throw new Exception("Negate requires numeric type");

                    break;
                }

                default:
                    throw new Exception($"Unknown opcode {instruction.OpCode}");
            }

            ip++;
        }
    }

    // ================================
    // 🔥 Helpers
    // ================================

    private void BinaryNumeric(Func<double, double, double> operation)
    {
        var b = _stack.Pop();
        var a = _stack.Pop();

        if (!IsNumber(a) || !IsNumber(b))
            throw new Exception("Operation requires numbers");

        // int + int → int
        if (a.Type == SabakaType.Int && b.Type == SabakaType.Int)
        {
            int result = (int)operation(a.Int, b.Int);
            _stack.Push(Value.FromInt(result));
        }
        else
        {
            double left = ToDouble(a);
            double right = ToDouble(b);
            _stack.Push(Value.FromFloat(operation(left, right)));
        }
    }

    private void Compare(Func<Value, Value, bool> comparison)
    {
        var b = _stack.Pop();
        var a = _stack.Pop();

        if (a.Type != b.Type)
            throw new Exception("Type mismatch in comparison");

        _stack.Push(Value.FromBool(comparison(a, b)));
    }

    private void CompareNumeric(Func<double, double, bool> comparison)
    {
        var b = _stack.Pop();
        var a = _stack.Pop();

        if (!IsNumber(a) || !IsNumber(b))
            throw new Exception("Comparison requires numbers");

        double left = ToDouble(a);
        double right = ToDouble(b);

        _stack.Push(Value.FromBool(comparison(left, right)));
    }

    private bool IsNumber(Value v)
    {
        return v.Type == SabakaType.Int || v.Type == SabakaType.Float;
    }

    private double ToDouble(Value v)
    {
        return v.Type switch
        {
            SabakaType.Int => v.Int,
            SabakaType.Float => v.Float,
            _ => throw new Exception("Not a number")
        };
    }
}