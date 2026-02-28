using SabakaLang.Compiler;
using SabakaLang.Types;
using System.Globalization;

namespace SabakaLang.VM;

public class VirtualMachine
{
    private class ExecutionContext
    {
        public Stack<Value> Stack = new();
        public Stack<Dictionary<string, Value>> Scopes = new();
        public Stack<int> CallStack = new();
        public Stack<int> ScopeDepthStack = new();
        public Stack<int> StackDepthStack = new();
        public Stack<Value> ThisStack = new();
        public Stack<bool> MethodCallStack = new();
        public int Ip = 0;
    }

    private readonly Dictionary<string, FunctionInfo> _functions = new();
    private readonly Dictionary<string, string> _inheritance = new();
    private readonly Dictionary<string, Value> _globalScope = new();
    private readonly object _globalScopeLock = new();
    private readonly List<System.Threading.Thread> _activeThreads = new();
    
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
        _externals = new Dictionary<string, Func<Value[], Value>>();
    }

    public void Execute(List<Instruction> instructions)
    {
        // Pre-scan for functions and inheritance
        for (int i = 0; i < instructions.Count; i++)
        {
            if (instructions[i].OpCode == OpCode.Function)
            {
                var info = new FunctionInfo
                {
                    Address = i + 1,
                    Parameters = UnwrapStringList(instructions[i].Extra)
                };

                _functions[instructions[i].Name!] = info;
            }
            else if (instructions[i].OpCode == OpCode.Inherit)
            {
                _inheritance[instructions[i].Name!] = UnwrapString(instructions[i].Operand);
            }
        }

        var mainContext = new ExecutionContext();
        mainContext.Scopes.Push(_globalScope);
        
        ExecuteInternal(mainContext, instructions);
        
        // Wait for all threads to finish
        while (true)
        {
            System.Threading.Thread[] threads;
            lock(_activeThreads)
            {
                threads = _activeThreads.Where(t => t.IsAlive).ToArray();
            }
            if (threads.Length == 0) break;
            foreach(var t in threads) t.Join(100);
        }
    }

    private void ExecuteInternal(ExecutionContext ctx, List<Instruction> instructions)
    {
        while (ctx.Ip < instructions.Count)
        {
            var instruction = instructions[ctx.Ip];

            switch (instruction.OpCode)
            {
                case OpCode.CreateObject:
                {
                    var fieldNames = UnwrapStringList(instruction.Extra);
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

                    ctx.Stack.Push(obj);
                    break;
                }

                case OpCode.PushThis:
                {
                    if (ctx.ThisStack.Count == 0) throw new Exception("No 'this' in current context");
                    ctx.Stack.Push(ctx.ThisStack.Peek());
                    break;
                }

                case OpCode.Dup:
                {
                    if (ctx.Stack.Count == 0) throw new Exception("Stack empty in Dup");
                    ctx.Stack.Push(ctx.Stack.Peek());
                    break;
                }

                case OpCode.Pop:
                {
                    if (ctx.Stack.Count == 0) throw new Exception("Stack empty in Pop");
                    ctx.Stack.Pop();
                    break;
                }

                case OpCode.Push:
                    ctx.Stack.Push(UnwrapValue(instruction.Operand));
                    break;

                case OpCode.CallExternal:
                {
                    int argCount = UnwrapInt(instruction.Operand);
                    var args = new List<Value>();
                    for (int i = 0; i < argCount; i++)
                    {
                        if (ctx.Stack.Count == 0) throw new Exception("Stack empty in CallExternal");
                        args.Add(ctx.Stack.Pop());
                    }
                    args.Reverse();

                    string name = instruction.Name!;
                    if (!_externals.TryGetValue(name, out var nativeFunc))
                        throw new Exception($"External function '{name}' not registered");

                    var result = nativeFunc(args.ToArray());
                    ctx.Stack.Push(result);
                    break;
                }
                
                case OpCode.Add:
                    if (ctx.Stack.Count < 2) throw new Exception("Stack empty in Add");
                    
                    if (IsStringAtTop(ctx))
                    {
                        var ba = ctx.Stack.Pop();
                        var ab = ctx.Stack.Pop();
                        ctx.Stack.Push(Value.FromString(ab.ToString() + ba.ToString()));
                    }
                    else
                    {
                        BinaryNumeric(ctx, (a, b) => a + b);
                    }

                    break;

                case OpCode.Sub:
                    if (ctx.Stack.Count < 2) throw new Exception("Stack empty in Sub");
                    BinaryNumeric(ctx, (a, b) => a - b);
                    break;

                case OpCode.Mul:
                    if (ctx.Stack.Count < 2) throw new Exception("Stack empty in Mul");
                    BinaryNumeric(ctx, (a, b) => a * b);
                    break;

                case OpCode.Div:
                    if (ctx.Stack.Count < 2) throw new Exception("Stack empty in Div");
                    BinaryNumeric(ctx, (a, b) => a / b);
                    break;

                case OpCode.Print:
                {
                    if (ctx.Stack.Count == 0) throw new Exception("Stack empty in Print");
                    var value = ctx.Stack.Pop();
                    _output.WriteLine(value.ToString());
                    break;
                }

                case OpCode.Sleep:
                {
                    if (ctx.Stack.Count == 0) throw new Exception("Stack empty in Sleep");
                    var value = ctx.Stack.Pop();
                    double seconds = value.Type == SabakaType.Int ? value.Int : value.Float;
                    System.Threading.Thread.Sleep((int)(seconds * 1000f));
                    break;
                }

                case OpCode.Input:
                {
                    var line = _input.ReadLine() ?? "";
                    if (int.TryParse(line, out int intVal))
                    {
                        ctx.Stack.Push(Value.FromInt(intVal));
                    }
                    else if (double.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out double floatVal))
                    {
                        ctx.Stack.Push(Value.FromFloat(floatVal));
                    }
                    else
                    {
                        ctx.Stack.Push(Value.FromString(line));
                    }
                    break;
                }

                case OpCode.LoadField:
                {
                    var obj = ctx.Stack.Pop();
                    if (obj.Type != SabakaType.Object && obj.Type != SabakaType.Struct)
                    {
                        throw new Exception("Cannot load field from non-object/struct");
                    }

                    var fields = obj.Type == SabakaType.Object ? obj.ObjectFields : obj.Struct;
                    if (fields == null) throw new Exception("Fields dictionary is null");

                    if (fields.TryGetValue(instruction.Name!, out var value))
                        ctx.Stack.Push(value);
                    else
                        ctx.Stack.Push(Value.FromInt(0)); // Default value

                    break;
                }

                case OpCode.StoreField:
                {
                    var value = ctx.Stack.Pop();
                    var obj = ctx.Stack.Pop();

                    if (obj.Type != SabakaType.Object && obj.Type != SabakaType.Struct)
                        throw new Exception("Cannot store field in non-object/struct");

                    var fields = obj.Type == SabakaType.Object ? obj.ObjectFields : obj.Struct;
                    if (fields == null) throw new Exception("Fields dictionary is null");

                    fields[instruction.Name!] = value;
                    break;
                }

                case OpCode.CallMethod:
                {
                    int argCount = UnwrapInt(instruction.Operand);
                    var args = new List<Value>();

                    for (int i = 0; i < argCount; i++)
                    {
                        if (ctx.Stack.Count == 0) throw new Exception("Stack empty in CallMethod (args)");
                        args.Add(ctx.Stack.Pop());
                    }

                    args.Reverse();

                    if (ctx.Stack.Count == 0) throw new Exception("Stack empty in CallMethod (object)");
                    var obj = ctx.Stack.Pop();

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
                        ctx.Stack.Push(extResult);
                        break;
                    }

                    var function = ResolveMethod(startClassName, instruction.Name!);

                    ctx.CallStack.Push(ctx.Ip + 1);
                    ctx.ScopeDepthStack.Push(ctx.Scopes.Count);
                    ctx.StackDepthStack.Push(ctx.Stack.Count);
                    ctx.ThisStack.Push(obj);
                    ctx.MethodCallStack.Push(true);

                    EnterScope(ctx);

                    for (int i = 0; i < function.Parameters.Count; i++)
                    {
                        ctx.Scopes.Peek()[function.Parameters[i]] = args[i];
                    }

                    ctx.Ip = function.Address;
                    continue;
                }

                case OpCode.Store:
                {
                    if (ctx.Stack.Count == 0) throw new Exception("Stack empty in Store");
                    Assign(ctx, instruction.Name!, ctx.Stack.Pop());
                    break;
                }

                case OpCode.Load:
                    ctx.Stack.Push(Resolve(ctx, instruction.Name!));
                    break;

                case OpCode.Declare:
                {
                    if (ctx.Stack.Count == 0) throw new Exception("Stack empty in Declare");
                    var value = ctx.Stack.Pop();
                    var currentScope = ctx.Scopes.Peek();
                    if (currentScope.ContainsKey(instruction.Name!))
                        throw new Exception("Variable already declared in this scope");
                    currentScope[instruction.Name!] = value;
                    break;
                }

                case OpCode.Jump:
                    ctx.Ip = UnwrapInt(instruction.Operand);
                    continue;

                case OpCode.JumpIfFalse:
                {
                    if (ctx.Stack.Count == 0) throw new Exception("Stack empty in JumpIfFalse");
                    var condition = ctx.Stack.Pop();
                    if (condition.Type != SabakaType.Bool) throw new Exception("Condition must be bool");
                    if (!condition.Bool)
                    {
                        ctx.Ip = UnwrapInt(instruction.Operand);
                        continue;
                    }

                    break;
                }

                case OpCode.Equal:
                    Compare(ctx, (a, b) =>
                    {
                        if (a.Type != b.Type) return false;
                        return a.Type switch
                        {
                            SabakaType.Int => a.Int == b.Int,
                            SabakaType.Float => a.Float == b.Float,
                            SabakaType.Bool => a.Bool == b.Bool,
                            SabakaType.String => a.String == b.String,
                            _ => false
                        };
                    });
                    break;

                case OpCode.NotEqual:
                    Compare(ctx, (a, b) =>
                    {
                        if (a.Type != b.Type) return true;
                        return a.Type switch
                        {
                            SabakaType.Int => a.Int != b.Int,
                            SabakaType.Float => a.Float != b.Float,
                            SabakaType.Bool => a.Bool != b.Bool,
                            SabakaType.String => a.String != b.String,
                            _ => true
                        };
                    });
                    break;

                case OpCode.Greater:
                    CompareNumeric(ctx, (a, b) => a > b);
                    break;
                case OpCode.Less:
                    CompareNumeric(ctx, (a, b) => a < b);
                    break;
                case OpCode.GreaterEqual:
                    CompareNumeric(ctx, (a, b) => a >= b);
                    break;
                case OpCode.LessEqual:
                    CompareNumeric(ctx, (a, b) => a <= b);
                    break;

                case OpCode.Negate:
                {
                    if (ctx.Stack.Count == 0) throw new Exception("Stack empty in Negate");
                    var val = ctx.Stack.Pop();
                    if (val.Type == SabakaType.Int) ctx.Stack.Push(Value.FromInt(-val.Int));
                    else if (val.Type == SabakaType.Float) ctx.Stack.Push(Value.FromFloat(-val.Float));
                    else throw new Exception("Negate requires numeric");
                    break;
                }

                case OpCode.Call:
                {
                    int argCount = UnwrapInt(instruction.Operand);
                    var args = new List<Value>();
                    for (int i = 0; i < argCount; i++) 
                    {
                        if (ctx.Stack.Count == 0) throw new Exception("Stack empty in Call");
                        args.Add(ctx.Stack.Pop());
                    }
                    args.Reverse();

                    string name = instruction.Name!;
                    if (_externals.TryGetValue(name, out var extFunc))
                    {
                        ctx.Stack.Push(extFunc(args.ToArray()));
                        break;
                    }

                    if (!_functions.TryGetValue(name, out var function))
                        throw new Exception($"Function '{name}' not found");

                    ctx.CallStack.Push(ctx.Ip + 1);
                    ctx.ScopeDepthStack.Push(ctx.Scopes.Count);
                    ctx.StackDepthStack.Push(ctx.Stack.Count);
                    ctx.MethodCallStack.Push(false);

                    EnterScope(ctx);
                    for (int i = 0; i < function.Parameters.Count; i++)
                    {
                        ctx.Scopes.Peek()[function.Parameters[i]] = args[i];
                    }

                    ctx.Ip = function.Address;
                    continue;
                }

                case OpCode.Return:
                {
                    Value result = Value.FromInt(0);
                    int targetStackDepth = ctx.StackDepthStack.Count > 0 ? ctx.StackDepthStack.Pop() : 0;
                    if (ctx.Stack.Count > targetStackDepth)
                    {
                        result = ctx.Stack.Pop();
                        while (ctx.Stack.Count > targetStackDepth) ctx.Stack.Pop();
                    }
                    
                    if (ctx.CallStack.Count == 0) return; // End of program or thread

                    int returnAddr = ctx.CallStack.Pop();
                    int targetScopeDepth = ctx.ScopeDepthStack.Pop();
                    bool wasMethodCall = ctx.MethodCallStack.Pop();

                    while (ctx.Scopes.Count > targetScopeDepth) ExitScope(ctx);

                    if (wasMethodCall) ctx.ThisStack.Pop();

                    ctx.Stack.Push(result);
                    ctx.Ip = returnAddr;
                    continue;
                }

                case OpCode.EnterScope:
                    EnterScope(ctx);
                    break;

                case OpCode.ExitScope:
                    ExitScope(ctx);
                    break;

                case OpCode.Not:
                {
                    if (ctx.Stack.Count == 0) throw new Exception("Stack empty in Not");
                    var a = ctx.Stack.Pop();
                    if (a.Type != SabakaType.Bool) throw new Exception("! requires bool");
                    ctx.Stack.Push(Value.FromBool(!a.Bool));
                    break;
                }

                case OpCode.JumpIfTrue:
                {
                    if (ctx.Stack.Count == 0) throw new Exception("Stack empty in JumpIfTrue");
                    var condition = ctx.Stack.Pop();
                    if (condition.Type != SabakaType.Bool) throw new Exception("Condition must be bool");
                    if (condition.Bool)
                    {
                        ctx.Ip = UnwrapInt(instruction.Operand);
                        continue;
                    }
                    break;
                }

                case OpCode.Function:
                {
                    // Skip over function body
                    string name = instruction.Name!;
                    int skip = 0;
                    for (int i = ctx.Ip + 1; i < instructions.Count; i++)
                    {
                        if (instructions[i].OpCode == OpCode.Return)
                        {
                            skip = i - ctx.Ip;
                            break;
                        }
                    }

                    ctx.Ip += skip + 1;
                    continue;
                }

                case OpCode.ArrayLength:
                {
                    if (ctx.Stack.Count == 0) throw new Exception("Stack empty in ArrayLength");
                    var arr = ctx.Stack.Pop();
                    if (arr.Type != SabakaType.Array) throw new Exception("ArrayLength requires array");
                    ctx.Stack.Push(Value.FromInt(arr.Array!.Count));
                    break;
                }

                case OpCode.CreateStruct:
                {
                    var fields = UnwrapStringList(instruction.Extra);
                    var structData = new Dictionary<string, Value>();
                    foreach (var field in fields) structData[field] = Value.FromInt(0);
                    ctx.Stack.Push(Value.FromStruct(structData));
                    break;
                }

                case OpCode.SpawnThread:
                {
                    var funcName = UnwrapString(instruction.Operand);
                    if (!_functions.TryGetValue(funcName, out var func))
                        throw new Exception($"Function '{funcName}' not found for SpawnThread");

                    var thread = new System.Threading.Thread(() => {
                        var newCtx = new ExecutionContext { Ip = func.Address };
                        newCtx.Scopes.Push(_globalScope);
                        newCtx.Scopes.Push(new Dictionary<string, Value>());
                        
                        try {
                            ExecuteInternal(newCtx, instructions);
                        } catch (Exception ex) {
                            _output.WriteLine($"Thread error: {ex.Message}");
                        }
                    });
                    
                    lock(_activeThreads) _activeThreads.Add(thread);
                    thread.Start();
                    ctx.Stack.Push(Value.FromThread(thread));
                    break;
                }

                case OpCode.JoinThread:
                {
                    if (ctx.Stack.Count == 0) throw new Exception("Stack empty in JoinThread");
                    var val = ctx.Stack.Pop();
                    if (val.Type != SabakaType.Thread) throw new Exception("JoinThread on non-thread value");
                    val.Thread!.Join();
                    break;
                }

                default:
                    throw new Exception($"Unknown opcode {instruction.OpCode}");
            }

            ctx.Ip++;
        }
    }

    private int UnwrapInt(object? operand)
    {
        if (operand is int i) return i;
        if (operand is Value v && v.Type == SabakaType.Int) return v.Int;
        if (operand is double d) return (int)d;
        return 0;
    }

    private string UnwrapString(object? operand)
    {
        if (operand is string s) return s;
        if (operand is Value v && v.Type == SabakaType.String) return v.String;
        return operand?.ToString() ?? "";
    }

    private Value UnwrapValue(object? operand)
    {
        if (operand is Value v) return v;
        if (operand is int i) return Value.FromInt(i);
        if (operand is double d) return Value.FromFloat(d);
        if (operand is bool b) return Value.FromBool(b);
        if (operand is string s) return Value.FromString(s);
        return default;
    }

    private List<string> UnwrapStringList(object? extra)
    {
        if (extra is List<string> list) return list;
        return new List<string>();
    }

    private bool IsStringAtTop(ExecutionContext ctx)
    {
        if (ctx.Stack.Count < 2) return false;
        var top = ctx.Stack.ToArray();
        return top[0].Type == SabakaType.String || top[1].Type == SabakaType.String;
    }

    private void BinaryNumeric(ExecutionContext ctx, Func<double, double, double> operation)
    {
        if (ctx.Stack.Count < 2) throw new Exception("Stack empty in BinaryNumeric");
        var b = ctx.Stack.Pop();
        var a = ctx.Stack.Pop();

        if (!IsNumber(a) || !IsNumber(b)) throw new Exception("Operation requires numbers");

        if (a.Type == SabakaType.Int && b.Type == SabakaType.Int)
        {
            int result = (int)operation(a.Int, b.Int);
            ctx.Stack.Push(Value.FromInt(result));
        }
        else
        {
            ctx.Stack.Push(Value.FromFloat(operation(ToDouble(a), ToDouble(b))));
        }
    }

    private void Compare(ExecutionContext ctx, Func<Value, Value, bool> comparison)
    {
        if (ctx.Stack.Count < 2) throw new Exception("Stack empty in Compare");
        var b = ctx.Stack.Pop();
        var a = ctx.Stack.Pop();
        ctx.Stack.Push(Value.FromBool(comparison(a, b)));
    }

    private void CompareNumeric(ExecutionContext ctx, Func<double, double, bool> comparison)
    {
        if (ctx.Stack.Count < 2) throw new Exception("Stack empty in CompareNumeric");
        var b = ctx.Stack.Pop();
        var a = ctx.Stack.Pop();
        if (!IsNumber(a) || !IsNumber(b)) throw new Exception("Comparison requires numbers");
        ctx.Stack.Push(Value.FromBool(comparison(ToDouble(a), ToDouble(b))));
    }

    private bool IsNumber(Value v) => v.Type == SabakaType.Int || v.Type == SabakaType.Float;

    private double ToDouble(Value v) => v.Type switch
    {
        SabakaType.Int => v.Int,
        SabakaType.Float => v.Float,
        _ => throw new Exception("Not a number")
    };

    private void EnterScope(ExecutionContext ctx) => ctx.Scopes.Push(new Dictionary<string, Value>());
    private void ExitScope(ExecutionContext ctx) => ctx.Scopes.Pop();

    private Value Resolve(ExecutionContext ctx, string name)
    {
        foreach (var scope in ctx.Scopes)
        {
            if (scope == _globalScope)
            {
                lock (_globalScopeLock)
                {
                    if (scope.TryGetValue(name, out var value)) return value;
                }
            }
            else if (scope.TryGetValue(name, out var value)) return value;
        }

        if (ctx.ThisStack.Count > 0)
        {
            var obj = ctx.ThisStack.Peek();
            if (obj.ObjectFields != null && obj.ObjectFields.TryGetValue(name, out var value))
                return value;
        }

        throw new Exception($"Undefined variable '{name}'");
    }

    private void Assign(ExecutionContext ctx, string name, Value value)
    {
        foreach (var scope in ctx.Scopes)
        {
            if (scope == _globalScope)
            {
                lock (_globalScopeLock)
                {
                    if (scope.ContainsKey(name))
                    {
                        scope[name] = value;
                        return;
                    }
                }
            }
            else if (scope.ContainsKey(name))
            {
                scope[name] = value;
                return;
            }
        }

        if (ctx.ThisStack.Count > 0)
        {
            var obj = ctx.ThisStack.Peek();
            if (obj.ObjectFields != null && obj.ObjectFields.ContainsKey(name))
            {
                obj.ObjectFields[name] = value;
                return;
            }
        }

        // Declare in current scope
        var top = ctx.Scopes.Peek();
        if (top == _globalScope)
        {
            lock (_globalScopeLock) top[name] = value;
        }
        else top[name] = value;
    }

    private FunctionInfo ResolveMethod(string className, string methodName)
    {
        string current = className;
        while (current != null)
        {
            string key = $"{current}.{methodName}";
            if (_functions.TryGetValue(key, out var func)) return func;
            if (_inheritance.TryGetValue(current, out var baseClass)) current = baseClass;
            else break;
        }
        throw new Exception($"Method '{methodName}' not found in '{className}'");
    }
}

public class FunctionInfo
{
    public int Address { get; set; }
    public List<string> Parameters { get; set; } = new();
}
