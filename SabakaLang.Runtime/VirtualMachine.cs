using System.Text;
using SabakaLang.Compiler;

namespace SabakaLang.Runtime;

public sealed class VirtualMachine
{
    public readonly TextReader _input;
    public readonly TextWriter _output;
    private readonly IReadOnlyDictionary<string, Func<Value[], Value>> _externals;
    
    private readonly Stack<Value>                          _stack      = new();
    private readonly Stack<Dictionary<string, Value>>     _scopes     = new();
    private readonly Stack<int>                            _callStack  = new();
    private readonly Stack<int>                            _scopeDepth = new();
    private readonly Stack<int>                            _stackDepth = new();
    private readonly Stack<Value>                          _thisStack  = new();
    private readonly Stack<bool>                           _isMethod   = new();
 
    private readonly Dictionary<string, FunctionInfo>     _functions  = new();
    private readonly Dictionary<string, string>           _inheritance= new();
    
    private List<Instruction> _instructions = new();
 
    private readonly GarbageCollector _gc;
 
    private static readonly HttpClient _http = new();
    
    public VirtualMachine(
        TextReader?  input     = null,
        TextWriter?  output    = null,
        IReadOnlyDictionary<string, Func<Value[], Value>>? externals = null,
        int gcThreshold = 512)
    {
        _input     = input     ?? Console.In;
        _output    = output    ?? Console.Out;
        _externals = externals ?? new Dictionary<string, Func<Value[], Value>>();
        _gc        = new GarbageCollector(gcThreshold, () => CollectRoots());
    }
    
    public void Execute(List<Instruction> instructions)
    {
        _instructions = instructions;
        _scopes.Push(new Dictionary<string, Value>());
 
        PreScan(instructions);
 
        int ip = 0;
        RunLoop(ref ip);
    }
    
    public Value CallFunction(string name, Value[] args)
    {
        if (!_functions.TryGetValue(name, out var func))
            throw new RuntimeException($"CallFunction: '{name}' not found");
 
        if (_instructions.Count == 0)
            throw new RuntimeException("CallFunction: Execute() not called yet");
 
        _callStack.Push(_instructions.Count); 
        _scopeDepth.Push(_scopes.Count);
        _stackDepth.Push(_stack.Count);
        _isMethod.Push(false);
 
        EnterScope();
        BindParams(func, args);
 
        int ip = func.Address;
        RunLoop(ref ip);
 
        return _stack.Count > 0 ? _stack.Pop() : Value.Null;
    }
    
    private void PreScan(List<Instruction> ins)
    {
        for (int i = 0; i < ins.Count; i++)
        {
            var instr = ins[i];
            if (instr.OpCode == OpCode.Function)
                _functions[instr.Name!] = new FunctionInfo(i + 1, UnwrapStringList(instr.Extra));
            else if (instr.OpCode == OpCode.Inherit)
                _inheritance[instr.Name!] = UnwrapString(instr.Operand);
        }
    }
    
    private void RunLoop(ref int ip)
    {
        var ins = _instructions;
 
        while (ip < ins.Count)
        {
            var instr = ins[ip];
            try
            {
                switch (instr.OpCode)
                {
                    case OpCode.Push:
                        Push(UnwrapValue(instr.Operand));
                        break;
 
                    case OpCode.Pop:
                        RequireStack(1, "Pop");
                        _stack.Pop();
                        break;
 
                    case OpCode.Dup:
                        RequireStack(1, "Dup");
                        Push(_stack.Peek());
                        break;
 
                    case OpCode.Swap:
                    {
                        RequireStack(2, "Swap");
                        var top  = _stack.Pop();
                        var next = _stack.Pop();
                        Push(top);
                        Push(next);
                        break;
                    }
 
                    case OpCode.Add:
                        RequireStack(2, "Add");
                        ExecAdd();
                        break;
 
                    case OpCode.Sub:
                        BinaryNumeric((a, b) => a - b, "Sub");
                        break;
 
                    case OpCode.Mul:
                        BinaryNumeric((a, b) => a * b, "Mul");
                        break;
 
                    case OpCode.Div:
                        BinaryNumeric((a, b) =>
                        {
                            if (b == 0) throw new RuntimeException("Division by zero");
                            return a / b;
                        }, "Div");
                        break;
 
                    case OpCode.Mod:
                        BinaryNumericInt((a, b) =>
                        {
                            if (b == 0) throw new RuntimeException("Modulo by zero");
                            return a % b;
                        }, "Mod");
                        break;
 
                    case OpCode.Negate:
                    {
                        RequireStack(1, "Negate");
                        var v = _stack.Pop();
                        Push(v.Type == SabakaType.Int
                            ? Value.FromInt(-v.Int)
                            : v.Type == SabakaType.Float
                                ? Value.FromFloat(-v.Float)
                                : throw new RuntimeException("Negate requires number"));
                        break;
                    }
 
                    case OpCode.And:
                    {
                        RequireStack(2, "And");
                        var b = Pop(); var a = Pop();
                        AssertBool(a, "And"); AssertBool(b, "And");
                        Push(Value.FromBool(a.Bool && b.Bool));
                        break;
                    }
 
                    case OpCode.Or:
                    {
                        RequireStack(2, "Or");
                        var b = Pop(); var a = Pop();
                        AssertBool(a, "Or"); AssertBool(b, "Or");
                        Push(Value.FromBool(a.Bool || b.Bool));
                        break;
                    }
 
                    case OpCode.Not:
                    {
                        RequireStack(1, "Not");
                        var a = Pop();
                        AssertBool(a, "Not");
                        Push(Value.FromBool(!a.Bool));
                        break;
                    }
 
                    case OpCode.Equal:
                        ExecEqual(negate: false);
                        break;
 
                    case OpCode.NotEqual:
                        ExecEqual(negate: true);
                        break;
 
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
 
                    case OpCode.Declare:
                    {
                        RequireStack(1, "Declare");
                        var val = Pop();
                        var scope = _scopes.Peek();
                        if (scope.ContainsKey(instr.Name!))
                            throw new RuntimeException($"Variable '{instr.Name}' already declared");
                        scope[instr.Name!] = val;
                        break;
                    }
 
                    case OpCode.Load:
                        Push(Resolve(instr.Name!));
                        break;
 
                    case OpCode.Store:
                    {
                        RequireStack(1, "Store");
                        Assign(instr.Name!, Pop());
                        break;
                    }
 
                    case OpCode.EnterScope:
                        EnterScope();
                        break;
 
                    case OpCode.ExitScope:
                        ExitScope();
                        break;
 
                    case OpCode.Jump:
                        ip = UnwrapInt(instr.Operand);
                        continue;
 
                    case OpCode.JumpIfFalse:
                    {
                        RequireStack(1, "JumpIfFalse");
                        var cond = Pop();
                        AssertBool(cond, "JumpIfFalse");
                        if (!cond.Bool) { ip = UnwrapInt(instr.Operand); continue; }
                        break;
                    }
 
                    case OpCode.JumpIfTrue:
                    {
                        RequireStack(1, "JumpIfTrue");
                        var cond = Pop();
                        AssertBool(cond, "JumpIfTrue");
                        if (cond.Bool) { ip = UnwrapInt(instr.Operand); continue; }
                        break;
                    }
 
                    case OpCode.Function:
                        ip = UnwrapInt(instr.Operand);
                        continue;
 
                    case OpCode.Call:
                    {
                        int argc = UnwrapInt(instr.Operand);
                        var args = PopArgs(argc);
 
                        if (!_functions.TryGetValue(instr.Name!, out var func))
                            throw new RuntimeException($"Undefined function '{instr.Name}'");
 
                        _callStack.Push(ip + 1);
                        _scopeDepth.Push(_scopes.Count);
                        _stackDepth.Push(_stack.Count);
                        _isMethod.Push(false);
 
                        EnterScope();
                        BindParams(func, args);
                        ip = func.Address;
                        continue;
                    }
 
                    case OpCode.Return:
                    {
                        Value ret = Value.Null;
                        int target = _stackDepth.Count > 0 ? _stackDepth.Pop() : 0;
 
                        if (_stack.Count > target)
                        {
                            ret = _stack.Pop();
                            while (_stack.Count > target) _stack.Pop();
                        }
 
                        if (_isMethod.Count > 0 && _isMethod.Pop())
                            if (_thisStack.Count > 0) _thisStack.Pop();
 
                        if (_scopeDepth.Count > 0)
                        {
                            int depth = _scopeDepth.Pop();
                            while (_scopes.Count > depth) ExitScope();
                        }
                        else ExitScope();
 
                        if (_callStack.Count == 0) return;
 
                        ip = _callStack.Pop();
                        Push(ret);
                        continue;
                    }
 
                    case OpCode.CreateObject:
                    {
                        var fields    = UnwrapStringList(instr.Extra);
                        var obj       = _gc.Alloc(instr.Name!);
                        foreach (var f in fields)
                            obj.Fields[f] = Value.FromInt(0);
                        Push(Value.FromObject(obj));
                        break;
                    }
 
                    case OpCode.PushThis:
                        if (_thisStack.Count == 0)
                            throw new RuntimeException("No 'this' in current context");
                        Push(_thisStack.Peek());
                        break;
 
                    case OpCode.LoadField:
                    {
                        RequireStack(1, "LoadField");
                        var obj = Pop();
                        Push(GetField(obj, instr.Name!));
                        break;
                    }
 
                    case OpCode.StoreField:
                    {
                        RequireStack(2, "StoreField");
                        var val = Pop();
                        var obj = Pop();
                        SetField(obj, instr.Name!, val);
                        break;
                    }
 
                    case OpCode.CallMethod:
                    {
                        int argc = UnwrapInt(instr.Operand);
                        var args = PopArgs(argc);
 
                        RequireStack(1, "CallMethod (object)");
                        var obj = Pop();
 
                        if (obj.Type != SabakaType.Object)
                            throw new RuntimeException($"Cannot call method on {obj.Type}");
 
                        string startClass = obj.Object!.ClassName;
                        if (instr.Extra is string baseClass)
                            startClass = baseClass;
 
                        string extKey = $"{startClass.ToLower()}.{instr.Name!.ToLower()}";
                        if (_externals.TryGetValue(extKey, out var extMethod))
                        {
                            Push(extMethod(args));
                            break;
                        }
 
                        var func = ResolveMethod(startClass, instr.Name!);
 
                        _callStack.Push(ip + 1);
                        _scopeDepth.Push(_scopes.Count);
                        _stackDepth.Push(_stack.Count);
                        _thisStack.Push(obj);
                        _isMethod.Push(true);
 
                        EnterScope();
                        BindParams(func, args);
                        ip = func.Address;
                        continue;
                    }
 
                    case OpCode.Inherit:
                        break;
 
                    case OpCode.CreateArray:
                    {
                        int count = UnwrapInt(instr.Operand);
                        var list  = new List<Value>(count);
                        for (int i = 0; i < count; i++) list.Add(Value.Null);
                        var arr = new Value[count];
                        for (int i = 0; i < count; i++) arr[i] = _stack.Pop();
                        for (int i = count - 1; i >= 0; i--) list[i] = arr[count - 1 - i];
                        Push(Value.FromArray(list));
                        break;
                    }
 
                    case OpCode.ArrayLoad:
                    {
                        RequireStack(2, "ArrayLoad");
                        var idx = Pop();
                        var arr = Pop();
 
                        if (idx.Type != SabakaType.Int)
                            throw new RuntimeException("Array index must be int");
 
                        if (arr.Type == SabakaType.String)
                        {
                            if (idx.Int < 0 || idx.Int >= arr.String.Length)
                                throw new RuntimeException($"String index {idx.Int} out of range");
                            Push(Value.FromString(arr.String[idx.Int].ToString()));
                            break;
                        }
 
                        if (arr.Type != SabakaType.Array)
                            throw new RuntimeException($"ArrayLoad: not an array (got {arr.Type})");
 
                        if (idx.Int < 0 || idx.Int >= arr.Array!.Count)
                            throw new RuntimeException($"ArrayLoad: index {idx.Int} out of bounds (size {arr.Array.Count})");
 
                        Push(arr.Array[idx.Int]);
                        break;
                    }
 
                    case OpCode.ArrayStore:
                    {
                        RequireStack(3, "ArrayStore");
                        var val = Pop();
                        var idx = Pop();
                        var arr = Pop();
 
                        if (arr.Type != SabakaType.Array)
                            throw new RuntimeException("ArrayStore: not an array");
                        if (idx.Type != SabakaType.Int)
                            throw new RuntimeException("ArrayStore: index must be int");
 
                        while (arr.Array!.Count <= idx.Int)
                            arr.Array.Add(Value.Null);
 
                        arr.Array[idx.Int] = val;
                        break;
                    }
 
                    case OpCode.ArrayLength:
                    {
                        RequireStack(1, "ArrayLength");
                        var v = Pop();
                        Push(v.Type == SabakaType.String
                            ? Value.FromInt(v.String?.Length ?? 0)
                            : Value.FromInt(v.Array?.Count  ?? 0));
                        break;
                    }
 
                    case OpCode.CreateStruct:
                    {
                        var fields = UnwrapStringList(instr.Extra);
                        var obj    = _gc.Alloc(instr.Name ?? "__struct");
                        foreach (var f in fields)
                            obj.Fields[f] = Value.FromInt(0);
                        Push(Value.FromObject(obj));
                        break;
                    }
 
                    case OpCode.PushEnum:
                        Push(UnwrapValue(instr.Operand));
                        break;
 
                    case OpCode.Print:
                    {
                        RequireStack(1, "Print");
                        _output.WriteLine(Pop().ToString());
                        break;
                    }
 
                    case OpCode.Input:
                    {
                        var line = _input.ReadLine() ?? "";
                        Push(Value.FromString(line));
                        break;
                    }
 
                    case OpCode.Sleep:
                    {
                        RequireStack(1, "Sleep");
                        var v = Pop();
                        Thread.Sleep((int)(v.ToDouble() * 1000));
                        break;
                    }
 
                    case OpCode.ReadFile:
                    {
                        var path = Pop().String;
                        Push(File.Exists(path)
                            ? Value.FromString(File.ReadAllText(path))
                            : Value.FromString(""));
                        break;
                    }
 
                    case OpCode.WriteFile:
                    {
                        var content = Pop().String;
                        var path    = Pop().String;
                        File.WriteAllText(path, content);
                        break;
                    }
 
                    case OpCode.AppendFile:
                    {
                        var content = Pop().String;
                        var path    = Pop().String;
                        File.AppendAllText(path, content);
                        break;
                    }
 
                    case OpCode.FileExists:
                    {
                        var path = Pop().String;
                        Push(Value.FromBool(File.Exists(path)));
                        break;
                    }
 
                    case OpCode.DeleteFile:
                    {
                        var path = Pop().String;
                        if (File.Exists(path)) File.Delete(path);
                        break;
                    }
 
                    case OpCode.ReadLines:
                    {
                        var path = Pop().String;
                        if (!File.Exists(path))
                            throw new RuntimeException($"File not found: {path}");
                        var lines = File.ReadAllLines(path).Select(Value.FromString).ToList();
                        Push(Value.FromArray(lines));
                        break;
                    }
 
                    case OpCode.Time:
                        Push(Value.FromFloat(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0));
                        break;
 
                    case OpCode.TimeMs:
                        Push(Value.FromInt((int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % int.MaxValue)));
                        break;
 
                    case OpCode.HttpGet:
                    {
                        var url = Pop().String;
                        Push(Value.FromString(_http.GetStringAsync(url).GetAwaiter().GetResult()));
                        break;
                    }
 
                    case OpCode.HttpPost:
                    {
                        var body = Pop().String;
                        var url  = Pop().String;
                        var resp = _http.PostAsync(url, new StringContent(body, Encoding.UTF8, "text/plain"))
                                        .GetAwaiter().GetResult();
                        Push(Value.FromString(resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()));
                        break;
                    }
 
                    case OpCode.HttpPostJson:
                    {
                        var json = Pop().String;
                        var url  = Pop().String;
                        var resp = _http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"))
                                        .GetAwaiter().GetResult();
                        Push(Value.FromString(resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()));
                        break;
                    }
 
                    case OpCode.Ord:
                    {
                        var s = Pop().String;
                        if (string.IsNullOrEmpty(s)) throw new RuntimeException("ord: empty string");
                        Push(Value.FromInt((int)s[0]));
                        break;
                    }
 
                    case OpCode.Chr:
                    {
                        var code = Pop().Int;
                        Push(Value.FromString(((char)code).ToString()));
                        break;
                    }
 
                    case OpCode.CallExternal:
                    {
                        int argc = UnwrapInt(instr.Operand);
                        var args = PopArgs(argc);
 
                        string name = instr.Name!;
                        if (!_externals.TryGetValue(name, out var fn))
                            throw new RuntimeException($"External '{name}' not registered");
 
                        Push(fn(args));
                        break;
                    }
 
                    default:
                        throw new RuntimeException($"Unknown opcode: {instr.OpCode}");
                }
 
                ip++;
            }
            catch (RuntimeException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new RuntimeException($"[ip={ip} op={instr.OpCode} name={instr.Name}] {ex.Message}");
            }
        }
    }

    public void Push(Value v) => _stack.Push(v);

    private Value Pop()
    {
        if (_stack.Count == 0) throw new RuntimeException("Stack underflow");
        return _stack.Pop();
    }
    
    private void RequireStack(int n, string op)
    {
        if (_stack.Count < n)
            throw new RuntimeException($"{op}: stack has {_stack.Count} items, need {n}");
    }
    
    private Value[] PopArgs(int count)
    {
        var args = new Value[count];
        for (int i = count - 1; i >= 0; i--)
            args[i] = Pop();
        return args;
    }
 
    private void BindParams(FunctionInfo func, Value[] args)
    {
        var scope = _scopes.Peek();
        for (int i = 0; i < func.Parameters.Count; i++)
            scope[func.Parameters[i]] = i < args.Length ? args[i] : Value.FromString("");
    }
    
    private void EnterScope() => _scopes.Push(new Dictionary<string, Value>());
 
    private void ExitScope()
    {
        if (_scopes.Count > 0) _scopes.Pop();
    }
    
    private Value Resolve(string name)
    {
        foreach (var scope in _scopes)
            if (scope.TryGetValue(name, out var v)) return v;
 
        if (_thisStack.Count > 0)
        {
            var obj = _thisStack.Peek();
            if (obj.Object?.Fields.TryGetValue(name, out var fv) == true) return fv;
        }
 
        throw new RuntimeException($"Undefined variable '{name}'");
    }
    
    private void Assign(string name, Value value)
    {
        foreach (var scope in _scopes)
        {
            if (scope.ContainsKey(name)) { scope[name] = value; return; }
        }
 
        if (_thisStack.Count > 0)
        {
            var obj = _thisStack.Peek();
            if (obj.Object?.Fields.ContainsKey(name) == true)
            {
                obj.Object.Fields[name] = value;
                return;
            }
        }
 
        throw new RuntimeException($"Undefined variable '{name}'");
    }
    
    private Value GetField(Value obj, string name)
    {
        if (obj.Type == SabakaType.Object && obj.Object != null)
        {
            if (obj.Object.Fields.TryGetValue(name, out var v)) return v;
            return Value.FromInt(0);
        }
        throw new RuntimeException($"Cannot load field '{name}' from {obj.Type}");
    }
 
    private void SetField(Value obj, string name, Value val)
    {
        if (obj.Type == SabakaType.Object && obj.Object != null)
        {
            obj.Object.Fields[name] = val;
            return;
        }
        throw new RuntimeException($"Cannot store field '{name}' in {obj.Type}");
    }
    
    private FunctionInfo ResolveMethod(string className, string methodName)
    {
        string fqn = $"{className}.{methodName}";
        if (_functions.TryGetValue(fqn, out var f)) return f;
 
        if (_inheritance.TryGetValue(className, out var baseName))
        {
            string next = methodName == className ? baseName : methodName;
            return ResolveMethod(baseName, next);
        }
 
        throw new RuntimeException($"Undefined method '{methodName}' in class '{className}'");
    }
    
    private void ExecAdd()
    {
        var b = Pop(); var a = Pop();
 
        if (a.Type == SabakaType.String || b.Type == SabakaType.String)
        {
            Push(Value.FromString(a.ToString() + b.ToString()));
            return;
        }
 
        if (!a.IsNumber || !b.IsNumber)
            throw new RuntimeException($"Add: unsupported types {a.Type} and {b.Type}");
 
        if (a.Type == SabakaType.Int && b.Type == SabakaType.Int)
            Push(Value.FromInt(a.Int + b.Int));
        else
            Push(Value.FromFloat(a.ToDouble() + b.ToDouble()));
    }
    
    private void BinaryNumeric(Func<double, double, double> op, string name)
    {
        RequireStack(2, name);
        var b = Pop(); var a = Pop();
        if (!a.IsNumber || !b.IsNumber)
            throw new RuntimeException($"{name}: requires numbers");
 
        if (a.Type == SabakaType.Int && b.Type == SabakaType.Int)
            Push(Value.FromInt((int)op(a.Int, b.Int)));
        else
            Push(Value.FromFloat(op(a.ToDouble(), b.ToDouble())));
    }

    private void BinaryNumericInt(Func<int, int, int> op, string name)
    {
        RequireStack(2, name);
        var b = Pop(); var a = Pop();
        if (a.Type != SabakaType.Int || b.Type != SabakaType.Int)
            throw new RuntimeException($"{name}: requires int operands");
        Push(Value.FromInt(op(a.Int, b.Int)));
    }
    
    private void CompareNumeric(Func<double, double, bool> cmp)
    {
        RequireStack(2, "Compare");
        var b = Pop(); var a = Pop();
        if (!a.IsNumber || !b.IsNumber)
            throw new RuntimeException("Comparison requires numbers");
        Push(Value.FromBool(cmp(a.ToDouble(), b.ToDouble())));
    }
    
    private void ExecEqual(bool negate)
    {
        RequireStack(2, "Equal");
        var b = Pop(); var a = Pop();
 
        bool eq;
        if (a.IsNumber && b.IsNumber)
        {
            eq = a.ToDouble() == b.ToDouble();
        }
        else if (a.Type != b.Type)
        {
            eq = a.Type == SabakaType.Null && b.Type == SabakaType.Null;
        }
        else
        {
            eq = a.Type switch
            {
                SabakaType.Bool   => a.Bool   == b.Bool,
                SabakaType.String => a.String == b.String,
                SabakaType.Array  => ReferenceEquals(a.Array, b.Array),
                SabakaType.Object => ReferenceEquals(a.Object, b.Object),
                SabakaType.Null   => true,
                _ => false
            };
        }
 
        Push(Value.FromBool(negate ? !eq : eq));
    }
    
    private static void AssertBool(Value v, string op)
    {
        if (v.Type != SabakaType.Bool)
            throw new RuntimeException($"{op}: expected bool, got {v.Type}");
    }
    
    private static int UnwrapInt(object? o) => o switch
    {
        int i   => i,
        Value v when v.Type == SabakaType.Int => v.Int,
        _ => 0
    };
 
    private static string UnwrapString(object? o) => o switch
    {
        string s => s,
        Value v when v.Type == SabakaType.String => v.String,
        _ => o?.ToString() ?? ""
    };
 
    private static Value UnwrapValue(object? o) => o switch
    {
        Value v   => v,
        int i     => Value.FromInt(i),
        double d  => Value.FromFloat(d),
        bool b    => Value.FromBool(b),
        string s  => Value.FromString(s),
        _         => Value.Null
    };
 
    private static List<string> UnwrapStringList(object? extra)
    {
        if (extra is List<string> list) return list;
        if (extra is System.Collections.IEnumerable e and not string)
        {
            var r = new List<string>();
            foreach (var item in e)
                r.Add(item is Value v ? v.String : item?.ToString() ?? "");
            return r;
        }
        return [];
    }
    
    private IEnumerable<SabakaObject> CollectRoots()
    {
        foreach (var v in _stack)
            if (v.Type == SabakaType.Object && v.Object != null) yield return v.Object;
 
        foreach (var scope in _scopes)
        foreach (var kv in scope)
            if (kv.Value.Type == SabakaType.Object && kv.Value.Object != null)
                yield return kv.Value.Object;
 
        foreach (var v in _thisStack)
            if (v.Type == SabakaType.Object && v.Object != null) yield return v.Object;
    }
}

public sealed class FunctionInfo
{
    public int           Address    { get; }
    public List<string>  Parameters { get; }
 
    public FunctionInfo(int address, List<string> parameters)
    {
        Address    = address;
        Parameters = parameters;
    }
}
