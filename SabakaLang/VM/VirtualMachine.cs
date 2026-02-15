using SabakaLang.Compiler;
using SabakaLang.Types;

namespace SabakaLang.VM;

public class VirtualMachine
{
    private readonly Stack<Value> _stack = new();
    private readonly Stack<Dictionary<string, Value>> _scopes = new();
    private Stack<int> _callStack = new();
    private Stack<int> _scopeDepthStack = new();
    private readonly Dictionary<string, FunctionInfo> _functions = new();



    public void Execute(List<Instruction> instructions)
    {
        _scopes.Push(new Dictionary<string, Value>());
        int ip = 0;

        // Сканируем инструкции и находим функции
        for (int i = 0; i < instructions.Count; i++)
        {
            if (instructions[i].OpCode == OpCode.Function)
            {
                var info = new FunctionInfo
                {
                    Address = i + 1,
                    Parameters = (List<string>)instructions[i].Extra!
                };

                _functions[instructions[i].Name!] = info;
            }

        }

        
        while (ip < instructions.Count)
        {
            var instruction = instructions[ip];

            switch (instruction.OpCode)
            {
                case OpCode.Push:
                    _stack.Push((Value)instruction.Operand!);
                    break;

                case OpCode.Add:
                    if (_stack.Count < 2) throw new Exception("Stack empty in Add");
                    
                    if (IsStringAtTop())
                    {
                        var ba = _stack.Pop();
                        var ab = _stack.Pop();
                        _stack.Push(Value.FromString(ab.ToString() + ba.ToString()));
                    }
                    else
                    {
                        BinaryNumeric((a, b) => a + b);
                    }

                    break;

                case OpCode.Sub:
                    if (_stack.Count < 2) throw new Exception("Stack empty in Sub");
                    BinaryNumeric((a, b) => a - b);
                    break;

                case OpCode.Mul:
                    if (_stack.Count < 2) throw new Exception("Stack empty in Mul");
                    BinaryNumeric((a, b) => a * b);
                    break;

                case OpCode.Div:
                    if (_stack.Count < 2) throw new Exception("Stack empty in Div");
                    BinaryNumeric((a, b) => a / b);
                    break;

                case OpCode.Store:
                {
                    if (_stack.Count == 0) throw new Exception("Stack empty in Store");
                    var value = _stack.Pop();
                    var name = instruction.Name!;
                    Assign(name, value);
                    break;
                }



                case OpCode.Load:
                {
                    var name = instruction.Name!;
                    var value = Resolve(name);
                    _stack.Push(value);
                    break;
                }



                case OpCode.Print:
                {
                    if (_stack.Count == 0) throw new Exception("Stack empty in Print");
                    var value = _stack.Pop();
                    Console.WriteLine(value);
                    break;
                }

                case OpCode.Call:
                {
                    int argCount = (int)instruction.Operand!;
                    var args = new List<Value>();

                    for (int i = 0; i < argCount; i++)
                    {
                        if (_stack.Count == 0) throw new Exception("Stack empty in Call");
                        args.Add(_stack.Pop());
                    }

                    args.Reverse();

                    if (!_functions.TryGetValue(instruction.Name!, out var function))
                        throw new Exception($"Undefined function '{instruction.Name}'");

                    _callStack.Push(ip + 1);
                    _scopeDepthStack.Push(_scopes.Count);

                    EnterScope();

                    for (int i = 0; i < function.Parameters.Count; i++)
                    {
                        // Ensure the parameter is declared in the new scope
                        _scopes.Peek()[function.Parameters[i]] = args[i];
                    }

                    ip = function.Address;
                    continue;
                }




                
                case OpCode.Return:
                {
                    Value returnValue = Value.FromInt(0); // Default for void
                    if (_stack.Count > 0)
                    {
                        returnValue = _stack.Pop();
                    }

                    if (_scopeDepthStack.Count > 0)
                    {
                        int targetDepth = _scopeDepthStack.Pop();
                        while (_scopes.Count > targetDepth)
                        {
                            ExitScope();
                        }
                    }
                    else
                    {
                        ExitScope();
                    }

                    if (_callStack.Count == 0)
                        return;

                    ip = _callStack.Pop();
                    _stack.Push(returnValue);
                    continue;
                }



                case OpCode.Jump:
                    ip = (int)instruction.Operand!;
                    continue;

                case OpCode.JumpIfFalse:
                {
                    if (_stack.Count == 0) throw new Exception("Stack empty in JumpIfFalse");
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
                    if (_stack.Count < 2) throw new Exception("Stack empty in Equal");
                    var b = _stack.Pop();
                    var a = _stack.Pop();

                    if (a.Type != b.Type)
                        throw new Exception("Type mismatch in ==");

                    bool result = a.Type switch
                    {
                        SabakaType.Int => a.Int == b.Int,
                        SabakaType.Float => a.Float == b.Float,
                        SabakaType.Bool => a.Bool == b.Bool,
                        SabakaType.String => a.String == b.String,
                        _ => throw new Exception("Invalid type for ==")
                    };

                    _stack.Push(Value.FromBool(result));
                    break;
                }


                case OpCode.NotEqual:
                {
                    if (_stack.Count < 2) throw new Exception("Stack empty in NotEqual");
                    var b = _stack.Pop();
                    var a = _stack.Pop();

                    if (a.Type != b.Type)
                        throw new Exception("Type mismatch in !=");

                    bool result = a.Type switch
                    {
                        SabakaType.Int => a.Int != b.Int,
                        SabakaType.Float => a.Float != b.Float,
                        SabakaType.Bool => a.Bool != b.Bool,
                        SabakaType.String => a.String != b.String,
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
                    if (_stack.Count == 0) throw new Exception("Stack empty in Negate");
                    var value = _stack.Pop();

                    if (value.Type == SabakaType.Int)
                        _stack.Push(Value.FromInt(-value.Int));
                    else if (value.Type == SabakaType.Float)
                        _stack.Push(Value.FromFloat(-value.Float));
                    else
                        throw new Exception("Negate requires numeric type");

                    break;
                }
                
                case OpCode.Declare:
                {
                    if (_stack.Count == 0) throw new Exception("Stack empty in Declare");
                    var value = _stack.Pop();
                    var currentScope = _scopes.Peek();

                    if (currentScope.ContainsKey(instruction.Name!))
                        throw new Exception("Variable already declared in this scope");

                    _scopes.Peek()[instruction.Name!] = value;
                    break;
                }


                case OpCode.EnterScope:
                    EnterScope();
                    break;

                case OpCode.ExitScope:
                    ExitScope();
                    break;

                case OpCode.Not:
                {
                    if (_stack.Count == 0) throw new Exception("Stack empty in Not");
                    var a = _stack.Pop();

                    if (a.Type != SabakaType.Bool)
                        throw new Exception("! requires bool");

                    _stack.Push(Value.FromBool(!a.Bool));
                    break;
                }

                case OpCode.JumpIfTrue:
                {
                    if (_stack.Count == 0) throw new Exception("Stack empty in JumpIfTrue");
                    var condition = _stack.Pop();

                    if (condition.Type != SabakaType.Bool)
                        throw new Exception("Condition must be bool");

                    if (condition.Bool)
                    {
                        ip = (int)instruction.Operand!;
                        continue;
                    }

                    break;
                }

                case OpCode.Function:
                {
                    ip = (int)instruction.Operand!;
                    continue;
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

    private bool IsStringAtTop()
    {
        if (_stack.Count < 2) return false;
        var top = _stack.ToArray();
        return top[0].Type == SabakaType.String || top[1].Type == SabakaType.String;
    }

    private void BinaryNumeric(Func<double, double, double> operation)
    {
        if (_stack.Count < 2) throw new Exception("Stack empty in BinaryNumeric");
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
        if (_stack.Count < 2) throw new Exception("Stack empty in Compare");
        var b = _stack.Pop();
        var a = _stack.Pop();

        if (a.Type != b.Type)
            throw new Exception("Type mismatch in comparison");

        _stack.Push(Value.FromBool(comparison(a, b)));
    }

    private void CompareNumeric(Func<double, double, bool> comparison)
    {
        if (_stack.Count < 2) throw new Exception("Stack empty in CompareNumeric");
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

    private void EnterScope()
    {
        _scopes.Push(new Dictionary<string, Value>());
    }

    private void ExitScope()
    {
        _scopes.Pop();
    }
    
    private Value GetVariable(string name)
    {
        foreach (var scope in _scopes)
        {
            if (scope.TryGetValue(name, out var value))
                return value;
        }

        throw new Exception($"Undefined variable '{name}'");
    }

    private Value Resolve(string name)
    {
        foreach (var scope in _scopes)
        {
            if (scope.ContainsKey(name))
                return scope[name];
        }

        throw new Exception($"Undefined variable '{name}'");
    }

    private void Assign(string name, Value value)
    {
        foreach (var scope in _scopes)
        {
            if (scope.ContainsKey(name))
            {
                scope[name] = value;
                return;
            }
        }

        throw new Exception($"Undefined variable '{name}'");
    }

}

public class FunctionInfo
{
    public int Address { get; set; }
    public List<string> Parameters { get; set; } = new();
}
