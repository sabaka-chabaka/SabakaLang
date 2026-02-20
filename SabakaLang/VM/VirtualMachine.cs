using SabakaLang.Compiler;
using SabakaLang.Types;
using System.Globalization;

namespace SabakaLang.VM;

public class VirtualMachine
{
    private readonly Stack<Value> _stack = new();
    private readonly Stack<Dictionary<string, Value>> _scopes = new();
    private Stack<int> _callStack = new();
    private Stack<int> _scopeDepthStack = new();
    private Stack<int> _stackDepthStack = new();
    private Stack<Value> _thisStack = new();
    private Stack<bool> _methodCallStack = new();
    private readonly Dictionary<string, FunctionInfo> _functions = new();
    private readonly Dictionary<string, string> _inheritance = new();
    private readonly TextReader _input;
    private readonly TextWriter _output;
    
    private readonly IReadOnlyDictionary<string, Func<Value[], Value>> _externals;

    public VirtualMachine(TextReader? input = null, TextWriter? output = null,
        IReadOnlyDictionary<string, Func<Value[], Value>>? externals = null)
    {
        _input = input ?? Console.In;
        _output = output ?? Console.Out;
        _externals = externals ?? new Dictionary<string, Func<Value[], Value>>();
    }

    public VirtualMachine(TextReader? input = null, TextWriter? output = null)
    {
        _input = input ?? Console.In;
        _output = output ?? Console.Out;
    }

    public void Execute(List<Instruction> instructions)
    {
        _scopes.Push(new Dictionary<string, Value>());
        int ip = 0;

        // Сканируем инструкции и находим функции и наследование
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
            else if (instructions[i].OpCode == OpCode.Inherit)
            {
                _inheritance[instructions[i].Name!] = (string)instructions[i].Operand!;
            }
        }

        
        while (ip < instructions.Count)
        {
            var instruction = instructions[ip];

            switch (instruction.OpCode)
            {
                case OpCode.CreateObject:
                {
                    var fieldNames = (List<string>?)instruction.Extra;
                    var fields = new Dictionary<string, Value>();
                    if (fieldNames != null)
                    {
                        foreach (var name in fieldNames)
                        {
                            fields[name] = Value.FromInt(0);
                        }
                    }

                    var obj = new Value
                    {
                        Type = SabakaType.Object,
                        ObjectFields = fields,
                        ClassName = instruction.Name
                    };

                    _stack.Push(obj);
                    break;
                }

                case OpCode.PushThis:
                {
                    if (_thisStack.Count == 0) throw new Exception("No 'this' in current context");
                    _stack.Push(_thisStack.Peek());
                    break;
                }

                case OpCode.Dup:
                {
                    if (_stack.Count == 0) throw new Exception("Stack empty in Dup");
                    _stack.Push(_stack.Peek());
                    break;
                }

                case OpCode.Pop:
                {
                    if (_stack.Count == 0) throw new Exception("Stack empty in Pop");
                    _stack.Pop();
                    break;
                }

                case OpCode.Push:
                    _stack.Push((Value)instruction.Operand!);
                    break;

                case OpCode.CallExternal:
                {
                    int argCount = (int)instruction.Operand!;
                    var args = new List<Value>();
                    for (int i = 0; i < argCount; i++)
                    {
                        if (_stack.Count == 0) throw new Exception("Stack empty in CallExternal");
                        args.Add(_stack.Pop());
                    }
                    args.Reverse();

                    string name = instruction.Name!;
                    if (!_externals.TryGetValue(name, out var nativeFunc))
                        throw new Exception($"External function '{name}' not registered");

                    var result = nativeFunc(args.ToArray());
                    _stack.Push(result);
                    break;
                }
                
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
                    _output.WriteLine(value.ToString());
                    break;
                }

                case OpCode.Input:
                {
                    var line = _input.ReadLine() ?? "";
                    if (int.TryParse(line, out int intVal))
                    {
                        _stack.Push(Value.FromInt(intVal));
                    }
                    else if (double.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out double floatVal))
                    {
                        _stack.Push(Value.FromFloat(floatVal));
                    }
                    else
                    {
                        _stack.Push(Value.FromString(line));
                    }
                    break;
                }

                case OpCode.LoadField:
                {
                    var obj = _stack.Pop();
                    if (obj.Type != SabakaType.Object && obj.Type != SabakaType.Struct)
                    {
                        throw new Exception("Cannot load field from non-object/struct");
                    }

                    var fields = obj.Type == SabakaType.Object ? obj.ObjectFields : obj.Struct;
                    if (fields == null) throw new Exception("Fields dictionary is null");

                    if (fields.TryGetValue(instruction.Name!, out var value))
                        _stack.Push(value);
                    else
                        _stack.Push(Value.FromInt(0)); // Default value

                    break;
                }

                case OpCode.StoreField:
                {
                    var value = _stack.Pop();
                    var obj = _stack.Pop();

                    if (obj.Type != SabakaType.Object && obj.Type != SabakaType.Struct)
                        throw new Exception("Cannot store field in non-object/struct");

                    var fields = obj.Type == SabakaType.Object ? obj.ObjectFields : obj.Struct;
                    if (fields == null) throw new Exception("Fields dictionary is null");

                    fields[instruction.Name!] = value;
                    break;
                }

                case OpCode.CallMethod:
                {
                    int argCount = (int)instruction.Operand!;
                    var args = new List<Value>();

                    for (int i = 0; i < argCount; i++)
                    {
                        if (_stack.Count == 0) throw new Exception("Stack empty in CallMethod (args)");
                        args.Add(_stack.Pop());
                    }

                    args.Reverse();

                    if (_stack.Count == 0) throw new Exception("Stack empty in CallMethod (object)");
                    var obj = _stack.Pop();

                    if (obj.Type != SabakaType.Object)
                        throw new Exception("Cannot call method on non-object");

                    string startClassName = obj.ClassName!;
                    if (instruction.Extra is string baseClassName)
                    {
                        startClassName = baseClassName;
                    }
                    
                    string extKey = $"{startClassName.ToLower()}.{instruction.Name!.ToLower()}";
                    if (_externals.TryGetValue(extKey, out var extMethod))
                    {
                        var extResult = extMethod(args.ToArray());
                        _stack.Push(extResult);
                        break;
                    }

                    var function = ResolveMethod(startClassName, instruction.Name!);

                    _callStack.Push(ip + 1);
                    _scopeDepthStack.Push(_scopes.Count);
                    _stackDepthStack.Push(_stack.Count);
                    _thisStack.Push(obj);
                    _methodCallStack.Push(true);

                    EnterScope();

                    for (int i = 0; i < function.Parameters.Count; i++)
                    {
                        _scopes.Peek()[function.Parameters[i]] = args[i];
                    }

                    ip = function.Address;
                    continue;
                }

                case OpCode.Inherit:
                {
                    // Already handled in pre-scan
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
                    _stackDepthStack.Push(_stack.Count);
                    _methodCallStack.Push(false);

                    EnterScope();

                    for (int i = 0; i < function.Parameters.Count; i++)
                    {
                        _scopes.Peek()[function.Parameters[i]] = args[i];
                    }

                    ip = function.Address;
                    continue;
                }


                case OpCode.CreateArray:
                {
                    int count = (int)instruction.Operand!;
                    var list = new List<Value>();

                    for (int i = 0; i < count; i++)
                        list.Add(_stack.Pop());

                    list.Reverse();

                    _stack.Push(Value.FromArray(list));
                    break;
                }

                
                case OpCode.ArrayLoad:
                {
                    var index = _stack.Pop();
                    var array = _stack.Pop();

                    if (array.Type != SabakaType.Array)
                        throw new Exception("Not an array");

                    if (index.Type != SabakaType.Int)
                        throw new Exception("Index must be int");

                    _stack.Push(array.Array![index.Int]);
                    break;
                }

                case OpCode.ArrayStore:
                {
                    var value = _stack.Pop();
                    var index = _stack.Pop();
                    var array = _stack.Pop();

                    if (array.Type != SabakaType.Array)
                        throw new Exception("Not an array");

                    if (index.Type != SabakaType.Int)
                        throw new Exception("Index must be int");

                    array.Array![index.Int] = value;
                    break;
                }


                
                case OpCode.Return:
                {
                    Value returnValue = Value.FromInt(0); // Default for void
                    int targetStackDepth = _stackDepthStack.Count > 0 ? _stackDepthStack.Pop() : 0;

                    if (_stack.Count > targetStackDepth)
                    {
                        returnValue = _stack.Pop();
                        while (_stack.Count > targetStackDepth)
                        {
                            _stack.Pop();
                        }
                    }

                    if (_methodCallStack.Count > 0 && _methodCallStack.Pop())
                    {
                        if (_thisStack.Count > 0)
                        {
                            _thisStack.Pop();
                        }
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

                    if (IsNumber(a) && IsNumber(b))
                    {
                        _stack.Push(Value.FromBool(ToDouble(a) == ToDouble(b)));
                    }
                    else if (a.Type != b.Type)
                    {
                        throw new Exception("Type mismatch in ==");
                    }
                    else
                    {
                        bool result = a.Type switch
                        {
                            SabakaType.Bool => a.Bool == b.Bool,
                            SabakaType.String => a.String == b.String,
                            SabakaType.Array => a.Array == b.Array, // Reference equality for now
                            _ => throw new Exception("Invalid type for ==")
                        };
                        _stack.Push(Value.FromBool(result));
                    }
                    break;
                }


                case OpCode.NotEqual:
                {
                    if (_stack.Count < 2) throw new Exception("Stack empty in NotEqual");
                    var b = _stack.Pop();
                    var a = _stack.Pop();

                    if (IsNumber(a) && IsNumber(b))
                    {
                        _stack.Push(Value.FromBool(ToDouble(a) != ToDouble(b)));
                    }
                    else if (a.Type != b.Type)
                    {
                        throw new Exception("Type mismatch in !=");
                    }
                    else
                    {
                        bool result = a.Type switch
                        {
                            SabakaType.Bool => a.Bool != b.Bool,
                            SabakaType.String => a.String != b.String,
                            SabakaType.Array => a.Array != b.Array,
                            _ => throw new Exception("Invalid type for !=")
                        };
                        _stack.Push(Value.FromBool(result));
                    }
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

                case OpCode.ArrayLength:
                {
                    var arr = _stack.Pop();

                    if (arr.Type != SabakaType.Array)
                        throw new Exception("ArrayLength requires array");

                    _stack.Push(Value.FromInt(arr.Array!.Count));
                    break;
                }

                case OpCode.CreateStruct:
                {
                    var fields = (List<string>)instruction.Extra!;
                    var structData = new Dictionary<string, Value>();
                    foreach (var field in fields)
                    {
                        structData[field] = Value.FromInt(0);
                    }
                    _stack.Push(Value.FromStruct(structData));
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

        if (_thisStack.Count > 0)
        {
            var obj = _thisStack.Peek();
            if (obj.ObjectFields != null && obj.ObjectFields.TryGetValue(name, out var value))
                return value;
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

        if (_thisStack.Count > 0)
        {
            var obj = _thisStack.Peek();
            if (obj.ObjectFields != null && obj.ObjectFields.ContainsKey(name))
            {
                obj.ObjectFields[name] = value;
                return;
            }
        }

        throw new Exception($"Undefined variable '{name}'");
    }

    private FunctionInfo ResolveMethod(string className, string methodName)
    {
        string fqn = $"{className}.{methodName}";
        if (_functions.TryGetValue(fqn, out var function))
            return function;

        if (_inheritance.TryGetValue(className, out var baseClassName))
        {
            // If it's a constructor call (method name matches the class name),
            // we should look for the base constructor in the base class.
            string nextMethodName = methodName;
            if (methodName == className)
                nextMethodName = baseClassName;

            return ResolveMethod(baseClassName, nextMethodName);
        }

        throw new Exception($"Undefined method '{methodName}' in class '{className}'");
    }

}

public class FunctionInfo
{
    public int Address { get; set; }
    public List<string> Parameters { get; set; } = new();
}
