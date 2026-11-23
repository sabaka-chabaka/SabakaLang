using SabakaLang.Compiler;
using SabakaLang.Runtime;
using SabakaLang.Extensions;

namespace SabakaLang.VM;

public sealed class VmOptions
{
    public TextWriter Out { get; init; } = Console.Out;
    public TextReader In { get; init; } = Console.In;

    /// <summary>Passed through to natives as <see cref="NativeContext.Host"/>.</summary>
    public object? Host { get; init; }

    public int MaxCallDepth { get; init; } = 100_000;
    public int MaxStackSize { get; init; } = 4_000_000;
}

/// <summary>Executes a <see cref="CompiledProgram"/>. Re-entrant: native code may call back into script functions.</summary>
public sealed class VirtualMachine
{
    private readonly CompiledProgram _program;
    private readonly ExtensionRegistry _registry;
    private readonly VmOptions _options;
    private readonly INativeCallable[] _natives;
    private readonly ClassRt[] _classes;
    private readonly Dictionary<string, ClassRt> _classById = new();
    private readonly Dictionary<string, int> _functionByName = new();
    private readonly VmContext _ctx;

    private Value[] _stack = new Value[1024];
    private int _sp;
    private readonly Value[] _globals;

    private struct Frame
    {
        public CompiledFunction Fn;
        public int Bp;
        public int Ip;
    }

    private Frame[] _frames = new Frame[64];
    private int _depth;

    public GarbageCollector Gc { get; } = new();
    public int ExitCode { get; private set; }

    private sealed class ClassRt
    {
        public required CompiledClass Info { get; init; }
        public required int Index { get; init; }
        public ClassRt? Base { get; set; }
        public Dictionary<string, int> Methods { get; } = new();   // flattened incl. inherited
        public HashSet<string> Supers { get; } = new();             // own id, base ids, interface ids
        public int Constructor { get; set; } = -1;                  // own or inherited
    }

    public VirtualMachine(CompiledProgram program, ExtensionRegistry registry, VmOptions? options = null)
    {
        _program = program;
        _registry = registry;
        _options = options ?? new VmOptions();
        _ctx = new VmContext(this);

        // link natives
        _natives = new INativeCallable[program.Natives.Count];
        for (var i = 0; i < _natives.Length; i++)
            _natives[i] = registry.ResolveKey(program.Natives[i].Key)
                          ?? throw new RuntimeException($"Native member '{program.Natives[i].Key}' is not registered. Is the extension loaded?");

        // link classes
        _classes = new ClassRt[program.Classes.Count];
        for (var i = 0; i < _classes.Length; i++)
        {
            _classes[i] = new ClassRt { Info = program.Classes[i], Index = i };
            _classById[program.Classes[i].Id] = _classes[i];
        }
        foreach (var c in _classes)
            if (c.Info.Base is { } b) c.Base = _classById[b];
        foreach (var c in _classes) Flatten(c);

        for (var i = 0; i < program.Functions.Count; i++)
        {
            var n = program.Functions[i].Name;
            if (!n.Contains('.') && !n.StartsWith('<') && !n.Contains("::<")) _functionByName.TryAdd(n, i);
        }

        _globals = program.Globals.ToArray();
        Gc.Roots = EnumerateRoots;
    }

    private void Flatten(ClassRt c)
    {
        if (c.Supers.Count > 0) return;
        c.Supers.Add(c.Info.Id);
        foreach (var i in c.Info.Interfaces) c.Supers.Add(i);
        c.Constructor = c.Info.Constructor;
        if (c.Base is { } b)
        {
            Flatten(b);
            foreach (var kv in b.Methods) c.Methods[kv.Key] = kv.Value;
            foreach (var s in b.Supers) c.Supers.Add(s);
            if (c.Constructor < 0) c.Constructor = b.Constructor;
        }
        foreach (var kv in c.Info.Methods) c.Methods[kv.Key] = kv.Value;
    }

    private IEnumerable<Value> EnumerateRoots()
    {
        for (var i = 0; i < _sp; i++) yield return _stack[i];
        foreach (var g in _globals) yield return g;
    }

    // ------------------------------------------------------------------ public API

    public TextWriter Out => _options.Out;
    public TextReader In => _options.In;
    public ExtensionRegistry Registry => _registry;
    public object? Host => _options.Host;
    public Value GetGlobal(int index) => _globals[index];

    /// <summary>Runs every module's top-level code. Returns normally or after <c>exit()</c>; script errors throw <see cref="RuntimeException"/>.</summary>
    public void Run()
    {
        try
        {
            foreach (var entry in _program.Entries) Invoke(entry, []);
        }
        catch (ScriptExitException e)
        {
            ExitCode = e.ExitCode;
        }
        finally
        {
            Out.Flush();
        }
    }

    /// <summary>Calls a global script function by name from the host application.</summary>
    public Value CallFunction(string name, params Value[] args)
    {
        if (!_functionByName.TryGetValue(name, out var idx))
        {
            // fall back to native functions
            var callable = _registry.Modules.SelectMany(m => m.Functions).FirstOrDefault(f => f.Name == name && f.Signature.Parameters.Count == args.Length)
                           ?? throw new RuntimeException($"Function '{name}' not found");
            return callable.Invoke(_ctx, args);
        }
        return Invoke(idx, args);
    }

    // ------------------------------------------------------------------ stack helpers

    private void Push(Value v)
    {
        if (_sp == _stack.Length) GrowStack(_sp + 1);
        _stack[_sp++] = v;
    }

    private Value Pop() => _stack[--_sp];

    private void GrowStack(int needed)
    {
        if (needed > _options.MaxStackSize) throw new RuntimeException("Stack overflow");
        var size = _stack.Length;
        while (size < needed) size *= 2;
        Array.Resize(ref _stack, Math.Min(size, Math.Max(_options.MaxStackSize, needed)));
    }

    private void PushFrame(int funcIndex, int argc)
    {
        var fn = _program.Functions[funcIndex];
        if (_depth >= _options.MaxCallDepth) throw new RuntimeException("Stack overflow (call depth exceeded)");
        if (_depth == _frames.Length) Array.Resize(ref _frames, _frames.Length * 2);

        var bp = _sp - argc;
        var top = bp + Math.Max(fn.LocalCount, argc);
        if (top + 16 > _stack.Length) GrowStack(top + 16);
        for (var i = bp + argc; i < top; i++) _stack[i] = Value.Null;
        _sp = top;
        _frames[_depth++] = new Frame { Fn = fn, Bp = bp, Ip = 0 };
    }

    /// <summary>Runs a function to completion (used by the host, constructors, field initialisers and native callbacks).</summary>
    private Value Invoke(int funcIndex, ReadOnlySpan<Value> args)
    {
        foreach (var a in args) Push(a);
        var stop = _depth;
        PushFrame(funcIndex, args.Length);
        return RunLoop(stop);
    }

    // ------------------------------------------------------------------ errors

    private RuntimeException Annotate(RuntimeException ex)
    {
        if (ex.ScriptStack.Count > 0) return ex;
        var stack = new List<string>();
        for (var i = _depth - 1; i >= 0; i--)
        {
            var f = _frames[i];
            var ip = Math.Max(0, Math.Min(f.Ip - 1, f.Fn.Code.Length - 1));
            var line = f.Fn.Code.Length > 0 ? f.Fn.Code[ip].Line : 0;
            stack.Add($"at {f.Fn.Name}{(line > 0 ? $" (line {line})" : "")}");
            if (i == _depth - 1) ex.Line = line;
        }
        ex.ScriptStack = stack;
        return ex;
    }

    private static RuntimeException Err(string m) => new(m);

    // ------------------------------------------------------------------ the interpreter loop

    private Value RunLoop(int stopDepth)
    {
        try
        {
            return Loop(stopDepth);
        }
        catch (RuntimeException ex)
        {
            Annotate(ex);
            // leave the VM usable for the host: unwind everything above the entry depth
            while (_depth > stopDepth) { _sp = _frames[_depth - 1].Bp; _depth--; }
            throw;
        }
        catch (ScriptExitException)
        {
            while (_depth > stopDepth) { _sp = _frames[_depth - 1].Bp; _depth--; }
            throw;
        }
    }

    private Value Loop(int stopDepth)
    {
        var frame = _frames[_depth - 1];
        var code = frame.Fn.Code;
        var bp = frame.Bp;
        var ip = frame.Ip;

        while (true)
        {
            var ins = code[ip++];
            // keep the saved ip current so error reports point at the right line
            _frames[_depth - 1].Ip = ip;

            switch (ins.Op)
            {
                case OpCode.Nop: break;
                case OpCode.Push: Push((Value)ins.Arg!); break;
                case OpCode.Pop: _sp--; break;
                case OpCode.Dup: Push(_stack[_sp - 1]); break;
                case OpCode.Dup2:
                {
                    var a = _stack[_sp - 2];
                    var b = _stack[_sp - 1];
                    Push(a);
                    Push(b);
                    break;
                }

                case OpCode.LoadLocal: Push(_stack[bp + ins.A]); break;
                case OpCode.StoreLocal: _stack[bp + ins.A] = Pop(); break;
                case OpCode.LoadGlobal: Push(_globals[ins.A]); break;
                case OpCode.StoreGlobal: _globals[ins.A] = Pop(); break;

                case OpCode.LoadField:
                {
                    var obj = Pop();
                    Push(GetMember(obj, (string)ins.Arg!));
                    break;
                }
                case OpCode.StoreField:
                {
                    var value = Pop();
                    var obj = Pop();
                    SetMember(obj, (string)ins.Arg!, value);
                    if (ins.B == 1) Push(value);
                    break;
                }

                case OpCode.LoadIndex:
                {
                    var idx = Pop();
                    var arr = Pop();
                    Push(LoadIndex(arr, idx));
                    break;
                }
                case OpCode.StoreIndex:
                {
                    var value = Pop();
                    var idx = Pop();
                    var arr = Pop();
                    StoreIndex(arr, idx, value);
                    if (ins.B == 1) Push(value);
                    break;
                }
                case OpCode.Length:
                {
                    var v = Pop();
                    Push(Value.FromInt(v.Type switch
                    {
                        SabakaType.Array => v.Array!.Count,
                        SabakaType.String => v.String.Length,
                        SabakaType.Null => throw Err("Cannot take the length of null"),
                        _ => throw Err($"Cannot take the length of {v.TypeName}")
                    }));
                    break;
                }
                case OpCode.BuildArray:
                {
                    var list = new List<Value>(ins.A);
                    for (var i = 0; i < ins.A; i++) list.Add(_stack[_sp - ins.A + i]);
                    _sp -= ins.A;
                    Push(Value.FromArray(list));
                    break;
                }
                case OpCode.NewArray:
                {
                    var n = Pop();
                    if (n.Type != SabakaType.Int || n.Int < 0) throw Err("Array size must be a non-negative int");
                    var list = new List<Value>(n.Int);
                    var def = (Value)ins.Arg!;
                    for (var i = 0; i < n.Int; i++)
                        list.Add(ins.A >= 0 ? Value.FromObject(CreateObject(_classes[ins.A])) : def);
                    Push(Value.FromArray(list));
                    break;
                }
                case OpCode.NewObject:
                    Push(Value.FromObject(CreateObject(_classes[ins.A])));
                    break;
                case OpCode.CloneStruct:
                {
                    var v = Pop();
                    Push(v.Object is { IsStruct: true } o ? Value.FromObject(Gc.Alloc(o.Clone())) : v);
                    break;
                }

                case OpCode.Add: { var b = Pop(); var a = Pop(); Push(Add(a, b)); break; }
                case OpCode.Sub: { var b = Pop(); var a = Pop(); Push(Arith(a, b, '-')); break; }
                case OpCode.Mul: { var b = Pop(); var a = Pop(); Push(Arith(a, b, '*')); break; }
                case OpCode.Div: { var b = Pop(); var a = Pop(); Push(Arith(a, b, '/')); break; }
                case OpCode.Mod: { var b = Pop(); var a = Pop(); Push(Arith(a, b, '%')); break; }
                case OpCode.Neg:
                {
                    var v = Pop();
                    Push(v.Type switch
                    {
                        SabakaType.Int => Value.FromInt(unchecked(-v.Int)),
                        SabakaType.Float => Value.FromFloat(-v.Float),
                        _ => throw Err($"Negate requires a number, got {v.TypeName}")
                    });
                    break;
                }
                case OpCode.Not:
                {
                    var v = Pop();
                    if (v.Type != SabakaType.Bool) throw Err($"'!' requires a bool, got {v.TypeName}");
                    Push(Value.FromBool(!v.Bool));
                    break;
                }

                case OpCode.Eq: { var b = Pop(); var a = Pop(); Push(Value.FromBool(a.Equals(b))); break; }
                case OpCode.Ne: { var b = Pop(); var a = Pop(); Push(Value.FromBool(!a.Equals(b))); break; }
                case OpCode.Lt: { var b = Pop(); var a = Pop(); Push(Value.FromBool(Compare(a, b) < 0)); break; }
                case OpCode.Gt: { var b = Pop(); var a = Pop(); Push(Value.FromBool(Compare(a, b) > 0)); break; }
                case OpCode.Le: { var b = Pop(); var a = Pop(); Push(Value.FromBool(Compare(a, b) <= 0)); break; }
                case OpCode.Ge: { var b = Pop(); var a = Pop(); Push(Value.FromBool(Compare(a, b) >= 0)); break; }

                case OpCode.Jump: ip = ins.A; break;
                case OpCode.JumpIfFalse: if (!Cond(Pop())) ip = ins.A; break;
                case OpCode.JumpIfTrue: if (Cond(Pop())) ip = ins.A; break;
                case OpCode.JumpIfFalseKeep:
                    if (!Cond(_stack[_sp - 1])) ip = ins.A; else _sp--;
                    break;
                case OpCode.JumpIfTrueKeep:
                    if (Cond(_stack[_sp - 1])) ip = ins.A; else _sp--;
                    break;
                case OpCode.JumpIfNotNullKeep:
                    if (!_stack[_sp - 1].IsNull) ip = ins.A; else _sp--;
                    break;

                case OpCode.Call:
                case OpCode.CallDirect:
                    PushFrame(ins.A, ins.B);
                    (code, bp, ip) = Enter();
                    break;

                case OpCode.CallVirtual:
                {
                    var name = (string)ins.Arg!;
                    var recv = _stack[_sp - ins.B - 1];
                    if (recv.Object is { } obj && _classById.TryGetValue(obj.ClassName, out var cls) && cls.Methods.TryGetValue(name, out var fi))
                    {
                        PushFrame(fi, ins.B + 1);
                        (code, bp, ip) = Enter();
                        break;
                    }
                    throw Err(recv.IsNull
                        ? $"Cannot call method '{name}' on null"
                        : $"Method '{name}' not found on {recv.TypeName}");
                }

                case OpCode.CallDynamic:
                {
                    var name = (string)ins.Arg!;
                    var recv = _stack[_sp - ins.B - 1];
                    if (recv.Object is { } obj && _classById.TryGetValue(obj.ClassName, out var cls) && cls.Methods.TryGetValue(name, out var fi))
                    {
                        if (_program.Functions[fi].ArgCount != ins.B + 1)
                            throw Err($"Method '{name}' expects {_program.Functions[fi].ArgCount - 1} argument(s), got {ins.B}");
                        PushFrame(fi, ins.B + 1);
                        (code, bp, ip) = Enter();
                        break;
                    }
                    var args = PopArgs(ins.B);
                    var target = Pop();
                    Push(CallBuiltinMethod(target, name, args));
                    break;
                }

                case OpCode.CallNative:
                {
                    var args = PopArgs(ins.B);
                    Push(CallNative(_natives[ins.A], args));
                    break;
                }

                case OpCode.NewNative:
                {
                    var args = PopArgs(ins.B);
                    var cls = _registry.FindClass(_program.Natives[ins.A].ClassName!)
                              ?? throw Err($"Native class '{_program.Natives[ins.A].ClassName}' is not registered");
                    var obj = _ctx.NewNativeObject(cls);
                    var full = new Value[args.Length + 1];
                    full[0] = Value.FromObject(obj);
                    args.CopyTo(full, 1);
                    CallNative(_natives[ins.A], full);
                    Push(full[0]);
                    break;
                }

                case OpCode.Return:
                {
                    var ret = Pop();
                    _sp = _frames[_depth - 1].Bp;
                    _depth--;
                    Push(ret);
                    if (_depth == stopDepth) return Pop();
                    (code, bp, ip) = Enter();
                    break;
                }

                case OpCode.ToFloat:
                {
                    var v = _stack[_sp - 1];
                    if (v.Type == SabakaType.Int) _stack[_sp - 1] = Value.FromFloat(v.Int);
                    break;
                }
                case OpCode.CharToString:
                {
                    var v = _stack[_sp - 1];
                    if (v.Type == SabakaType.Char) _stack[_sp - 1] = Value.FromString(v.Char.ToString());
                    break;
                }
                case OpCode.Stringify:
                {
                    var v = Pop();
                    Push(v.Type == SabakaType.String ? v : Value.FromString(Stringify(v)));
                    break;
                }
                case OpCode.Concat:
                {
                    var sb = new System.Text.StringBuilder();
                    for (var i = 0; i < ins.A; i++) sb.Append(_stack[_sp - ins.A + i].String);
                    _sp -= ins.A;
                    Push(Value.FromString(sb.ToString()));
                    break;
                }
                case OpCode.IsType:
                    Push(Value.FromBool(IsType(Pop(), (TypeTest)ins.Arg!)));
                    break;

                case OpCode.Halt: return Value.Null;

                default:
                    throw Err($"Unknown opcode {ins.Op}");
            }
        }
    }

    private (Instruction[] Code, int Bp, int Ip) Enter()
    {
        var f = _frames[_depth - 1];
        return (f.Fn.Code, f.Bp, f.Ip);
    }

    // ------------------------------------------------------------------ operations

    private static bool Cond(Value v) =>
        v.Type == SabakaType.Bool ? v.Bool : throw Err($"Condition must be a bool, got {v.TypeName}");

    private Value Add(Value a, Value b)
    {
        if (a.Type == SabakaType.Int && b.Type == SabakaType.Int) return Value.FromInt(unchecked(a.Int + b.Int));
        if (a.IsNumber && b.IsNumber) return Value.FromFloat(a.ToDouble() + b.ToDouble());
        if (a.Type is SabakaType.String or SabakaType.Char || b.Type is SabakaType.String or SabakaType.Char)
            return Value.FromString(Stringify(a) + Stringify(b));
        throw Err($"Cannot add {a.TypeName} and {b.TypeName}");
    }

    private static Value Arith(Value a, Value b, char op)
    {
        if (!a.IsNumber || !b.IsNumber)
            throw Err($"Operator '{op}' requires numbers, got {a.TypeName} and {b.TypeName}");

        if (a.Type == SabakaType.Int && b.Type == SabakaType.Int)
        {
            var x = a.Int;
            var y = b.Int;
            switch (op)
            {
                case '-': return Value.FromInt(unchecked(x - y));
                case '*': return Value.FromInt(unchecked(x * y));
                case '/':
                    if (y == 0) throw Err("Division by zero");
                    return Value.FromInt(y == -1 ? unchecked(-x) : x / y);
                default:
                    if (y == 0) throw Err("Modulo by zero");
                    return Value.FromInt(y == -1 ? 0 : x % y);
            }
        }

        var p = a.ToDouble();
        var q = b.ToDouble();
        return Value.FromFloat(op switch { '-' => p - q, '*' => p * q, '/' => p / q, _ => p % q });
    }

    private static int Compare(Value a, Value b)
    {
        if (a.IsNumber && b.IsNumber) return a.ToDouble().CompareTo(b.ToDouble());
        if (a.Type == b.Type && a.Type is SabakaType.String or SabakaType.Char)
            return string.CompareOrdinal(a.ToString(), b.ToString());
        throw Err($"Cannot compare {a.TypeName} and {b.TypeName}");
    }

    private static Value LoadIndex(Value arr, Value idx)
    {
        if (idx.Type != SabakaType.Int) throw Err($"Array index must be an int, got {idx.TypeName}");
        var i = idx.Int;
        switch (arr.Type)
        {
            case SabakaType.Array:
                var list = arr.Array!;
                if ((uint)i >= (uint)list.Count) throw Err($"Array index {i} is out of bounds (size {list.Count})");
                return list[i];
            case SabakaType.String:
                var s = arr.String;
                if ((uint)i >= (uint)s.Length) throw Err($"String index {i} is out of range (length {s.Length})");
                return Value.FromChar(s[i]);
            case SabakaType.Null: throw Err("Cannot index null");
            default: throw Err($"Cannot index {arr.TypeName}");
        }
    }

    private static void StoreIndex(Value arr, Value idx, Value value)
    {
        if (idx.Type != SabakaType.Int) throw Err($"Array index must be an int, got {idx.TypeName}");
        if (arr.Type == SabakaType.Null) throw Err("Cannot index null");
        if (arr.Type == SabakaType.String) throw Err("Strings are immutable");
        if (arr.Type != SabakaType.Array) throw Err($"Cannot index {arr.TypeName}");

        var list = arr.Array!;
        var i = idx.Int;
        if (i < 0) throw Err($"Array index {i} is out of bounds (size {list.Count})");
        while (list.Count <= i) list.Add(Value.Null); // assigning past the end grows the array
        list[i] = value;
    }

    private bool IsType(Value v, TypeTest t)
    {
        if (v.IsNull) return t.Nullable || t.Kind == "any";
        return t.Kind switch
        {
            "int" => v.Type == SabakaType.Int,
            "float" => v.Type == SabakaType.Float,
            "bool" => v.Type == SabakaType.Bool,
            "string" => v.Type == SabakaType.String,
            "char" => v.Type == SabakaType.Char,
            "array" => v.Type == SabakaType.Array,
            "enum" => v.Type == SabakaType.Enum && v.Enum!.EnumName == t.Name,
            "class" => v.Type == SabakaType.Object && _classById.TryGetValue(v.Object!.ClassName, out var c)
                ? c.Supers.Contains(t.Name!)
                : v.Type == SabakaType.Object && v.Object!.ClassName == t.Name,
            _ => true
        };
    }

    // ------------------------------------------------------------------ objects

    private SabakaObject CreateObject(ClassRt cls)
    {
        var obj = Gc.Alloc(new SabakaObject(cls.Info.Id) { IsStruct = cls.Info.IsStruct });
        InitFields(obj, cls);
        return obj;
    }

    private void InitFields(SabakaObject obj, ClassRt cls)
    {
        if (cls.Base is { } b) InitFields(obj, b);
        foreach (var f in cls.Info.Fields)
            obj.Fields[f.Name] = f.StructClass >= 0 ? Value.FromObject(CreateObject(_classes[f.StructClass])) : f.Default;
        if (cls.Info.Initializer >= 0) Invoke(cls.Info.Initializer, [Value.FromObject(obj)]);
    }

    private Value GetMember(Value obj, string name)
    {
        switch (obj.Type)
        {
            case SabakaType.Object:
            {
                var o = obj.Object!;
                if (o.Fields.TryGetValue(name, out var v)) return v;
                if (_registry.FindClass(o.ClassName) is { } nc)
                {
                    var prop = nc.Properties.FirstOrDefault(p => p.Name == name);
                    if (prop is not null) return prop.Getter(_ctx, obj);
                }
                throw Err($"Object of class '{o.ShortName}' has no member '{name}'");
            }
            case SabakaType.Array when name == "length": return Value.FromInt(obj.Array!.Count);
            case SabakaType.String when name == "length": return Value.FromInt(obj.String.Length);
            case SabakaType.Null: throw Err($"Cannot access member '{name}' of null");
        }

        var ext = _registry.FindExtensions(name).FirstOrDefault(e => e.IsProperty && e.AcceptsRuntime(obj));
        if (ext is not null) return ext.Invoke(_ctx, [obj]);
        throw Err($"{obj.TypeName} has no member '{name}'");
    }

    private void SetMember(Value obj, string name, Value value)
    {
        if (obj.Type == SabakaType.Null) throw Err($"Cannot set member '{name}' of null");
        if (obj.Type != SabakaType.Object) throw Err($"Cannot set member '{name}' of {obj.TypeName}");

        var o = obj.Object!;
        if (o.Fields.ContainsKey(name)) { o.Fields[name] = value; return; }
        if (_registry.FindClass(o.ClassName) is { } nc)
        {
            var prop = nc.Properties.FirstOrDefault(p => p.Name == name);
            if (prop?.Setter is { } setter) { setter(_ctx, obj, value); return; }
            if (prop is not null) throw Err($"Property '{name}' is read-only");
        }
        throw Err($"Object of class '{o.ShortName}' has no member '{name}'");
    }

    // ------------------------------------------------------------------ natives and dynamic calls

    private Value[] PopArgs(int n)
    {
        var args = new Value[n];
        for (var i = n - 1; i >= 0; i--) args[i] = Pop();
        return args;
    }

    private Value CallNative(INativeCallable callable, Value[] args)
    {
        try
        {
            return callable.Invoke(_ctx, args);
        }
        catch (RuntimeException) { throw; }
        catch (ScriptExitException) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new RuntimeException($"{callable.Key}: {ex.Message}", ex);
        }
    }

    /// <summary>Method call on a value whose type was unknown at compile time.</summary>
    private Value CallBuiltinMethod(Value target, string name, Value[] args)
    {
        if (target.IsNull) throw Err($"Cannot call method '{name}' on null");

        if (target.Object is { } obj && _registry.FindClass(obj.ClassName) is { } nc)
        {
            var m = nc.Methods.FirstOrDefault(x => !x.IsStatic && x.Name == name && Accepts(x.Signature, args.Length));
            if (m is not null) return CallNative(m, [target, ..args]);
        }

        var ext = _registry.FindExtensions(name).FirstOrDefault(e => !e.IsProperty && e.AcceptsRuntime(target) && Accepts(e.Signature, args.Length));
        if (ext is not null) return CallNative(ext, [target, ..args]);

        throw Err($"Cannot call method '{name}' on {target.TypeName}");
    }

    private static bool Accepts(NativeSignature s, int argc) => s.IsVariadic ? argc >= s.MinArgs : argc == s.Parameters.Count;

    // ------------------------------------------------------------------ services for natives

    internal string Stringify(Value v)
    {
        if (v.Object is { } o && _classById.TryGetValue(o.ClassName, out var cls) &&
            cls.Methods.TryGetValue("toString", out var fi) && _program.Functions[fi].ArgCount == 1)
        {
            var r = Invoke(fi, [v]);
            return r.Type == SabakaType.String ? r.String : r.ToString();
        }
        if (v.Object is { } no && _registry.FindClass(no.ClassName) is { } nc
            && nc.Methods.FirstOrDefault(m => !m.IsStatic && m.Name == "toString" && m.Signature.Parameters.Count == 0) is { } ts)
        {
            var r = CallNative(ts, [v]);
            return r.Type == SabakaType.String ? r.String : r.ToString();
        }
        if (v.Type == SabakaType.Array)
            return "[" + string.Join(", ", v.Array!.Select(Stringify)) + "]";
        return v.ToString();
    }

    internal SabakaObject AllocateObject(string className) => Gc.Alloc(new SabakaObject(className));

    internal Value CallMethodByName(Value target, string name, Value[] args)
    {
        if (target.Object is { } obj && _classById.TryGetValue(obj.ClassName, out var cls) && cls.Methods.TryGetValue(name, out var fi))
            return Invoke(fi, [target, ..args]);
        return CallBuiltinMethod(target, name, args);
    }

    internal Value NewObjectByName(string className, Value[] args)
    {
        if (_classById.TryGetValue(className, out var cls))
        {
            var obj = CreateObject(cls);
            if (cls.Constructor >= 0) Invoke(cls.Constructor, [Value.FromObject(obj), ..args]);
            return Value.FromObject(obj);
        }
        if (_registry.FindClass(className) is { } nc)
        {
            var ctor = nc.Constructors.FirstOrDefault(c => Accepts(c.Signature, args.Length))
                       ?? throw Err($"Class '{className}' has no constructor taking {args.Length} argument(s)");
            var o = _ctx.NewNativeObject(nc);
            CallNative(ctor, [Value.FromObject(o), ..args]);
            return Value.FromObject(o);
        }
        throw Err($"Class '{className}' not found");
    }

    private sealed class VmContext(VirtualMachine vm) : NativeContext
    {
        public override TextWriter Out => vm.Out;
        public override TextReader In => vm.In;
        public override ExtensionRegistry Registry => vm.Registry;
        public override object? Host => vm.Host;
        public override Value CallFunction(string name, params Value[] args) => vm.CallFunction(name, args);
        public override Value CallMethod(Value target, string name, params Value[] args) => vm.CallMethodByName(target, name, args);
        public override Value NewObject(string className, params Value[] args) => vm.NewObjectByName(className, args);
        public override string Stringify(Value value) => vm.Stringify(value);
        protected override SabakaObject Allocate(string className) => vm.AllocateObject(className);
    }
}
