using System.Globalization;

namespace SabakaLang.Compiler;

public enum OpCode
{
    Push,
    Pop,
    Dup,
    Swap,
 
    Add, Sub, Mul, Div, Mod,
 
    Equal, NotEqual, Greater, Less, GreaterEqual, LessEqual,
 
    And, Or, Not, Negate,
 
    Declare,
    Load,
    Store,
 
    EnterScope,
    ExitScope,
 
    Jump,
    JumpIfFalse,
    JumpIfTrue,
    
    Function,
    Call,
    Return,
    
    Is,
    
    CreateObject,
    CallMethod,
    LoadField,
    StoreField,
    PushThis,
    Inherit,
 
    CreateArray,
    ArrayLoad,
    ArrayStore,
    ArrayLength,
 
    CreateStruct,
 
    PushEnum,
 
    Print,
    Input,
    Sleep,
    ReadFile, WriteFile, AppendFile, FileExists, DeleteFile, ReadLines,
    Time, TimeMs,
    HttpGet, HttpPost, HttpPostJson,
    Ord, Chr,
 
    CallExternal
}

public sealed class Instruction(
    OpCode opCode,
    object? operand = null,
    string? name = null,
    object? extra = null)
{
    public OpCode  OpCode  { get; } = opCode;
    public object? Operand { get; set; } = operand;
    public string? Name    { get; } = name;
    public object? Extra   { get; } = extra;

    public override string ToString()
    {
        var parts = new List<string> { OpCode.ToString() };
        if (Name    is not null) parts.Add($"'{Name}'");
        if (Operand is not null) parts.Add(Operand.ToString()!);
        return string.Join(" ", parts);
    }
}

internal sealed class ClassMeta(string name, string? @base)
{
    public string  Name       { get; } = name;
    public string? Base       { get; } = @base;
    public List<string>         Fields  { get; } = [];
    public List<VarDecl>        FieldDecls { get; } = [];
    public List<FuncDecl>       Methods { get; } = [];

    // ReSharper disable once CollectionNeverQueried.Global
    public readonly List<string> Interfaces = [];
}

public readonly record struct CompileError(string Message, Position Position)
{
    public override string ToString() => $"[{Position.Line}:{Position.Column}] {Message}";
}

public sealed class CompileResult(
    IReadOnlyList<Instruction> code,
    IReadOnlyList<CompileError> errors,
    SymbolTable? symbols = null)
{
    public IReadOnlyList<Instruction>  Code    { get; } = code;
    public IReadOnlyList<CompileError> Errors  { get; } = errors;
    public SymbolTable                 Symbols { get; } = symbols ?? new SymbolTable();
    public bool HasErrors => Errors.Count > 0;
}

public enum SabakaType { Null, Int, Float, Bool, String, Array, Object }

public readonly struct Value
{
    public readonly SabakaType Type;
 
    public readonly int    Int;
    public readonly double Float;
    public readonly bool   Bool;
    public readonly string String;
 
    public readonly List<Value>?           Array;
    public readonly SabakaObject?          Object;
 
    private Value(SabakaType t, int i = 0, double f = 0, bool b = false,
                  string s = "", List<Value>? arr = null, SabakaObject? obj = null)
    {
        Type   = t; Int = i; Float = f; Bool = b;
        String = s; Array = arr; Object = obj;
    }
 
    public static readonly Value Null    = new(SabakaType.Null);
    public static Value FromInt(int v)           => new(SabakaType.Int,   i: v);
    public static Value FromFloat(double v)      => new(SabakaType.Float, f: v);
    public static Value FromBool(bool v)         => new(SabakaType.Bool,  b: v);
    public static Value FromString(string? v)     => new(SabakaType.String, s: v ?? "");
    public static Value FromArray(List<Value> v) => new(SabakaType.Array,  arr: v);
    public static Value FromObject(SabakaObject v) => new(SabakaType.Object, obj: v);
 
    public bool IsNull   => Type == SabakaType.Null;
    public bool IsNumber => Type is SabakaType.Int or SabakaType.Float;
 
    public double ToDouble() => Type switch
    {
        SabakaType.Int   => Int,
        SabakaType.Float => Float,
        _ => throw new RuntimeException($"Expected number, got {Type}")
    };
 
    public override string ToString() => Type switch
    {
        SabakaType.Null   => "null",
        SabakaType.Int    => Int.ToString(CultureInfo.InvariantCulture),
        SabakaType.Float  => Float.ToString(CultureInfo.InvariantCulture),
        SabakaType.Bool   => Bool ? "true" : "false",
        SabakaType.String => String,
        SabakaType.Array  => "[" + string.Join(", ", Array!.Select(v => v.ToString())) + "]",
        SabakaType.Object => Object!.ToString(),
        _ => "?"
    };
}

public sealed class SabakaObject(string className)
{
    public string ClassName { get; } = className;
    public Dictionary<string, Value> Fields { get; } = new();

    public SabakaObject Clone()
    {
        var c = new SabakaObject(ClassName);
        foreach (var kv in Fields) c.Fields[kv.Key] = kv.Value;
        return c;
    }
 
    public override string ToString() =>
        $"{ClassName} {{ {string.Join(", ", Fields.Select(kv => $"{kv.Key}: {kv.Value}"))} }}";
}
 
public sealed class RuntimeException(string msg) : Exception(msg);

public sealed class Compiler
{
    private readonly List<Instruction>  _code   = [];
    private readonly List<CompileError> _errors = [];
 
    private readonly Dictionary<string, ClassMeta>              _classes   = new();
    private readonly Dictionary<string, List<VarDecl>>         _structs   = new();
    private readonly Dictionary<string, Dictionary<string,int>> _enums     = new();
    private readonly Dictionary<string, InterfaceDecl>          _interfaces= new();
 
    private readonly Dictionary<string,(int ParamCount, bool Registered)> _externals = new();
 
    private readonly Stack<Dictionary<string, string>> _typeScopes = new();
 
    private string? _currentClass;

    private SymbolTable _symbolTable = new();
    
    public void RegisterExternal(string name, int paramCount)
        => _externals[name] = (paramCount, true);

    public CompileResult Compile(IReadOnlyList<IStmt> statements, BindResult bindResult)
    {
        _symbolTable = bindResult.Symbols;

        foreach (var be in bindResult.Errors)
            _errors.Add(new CompileError(be.Message, be.Position));

        PushTypeScope();

        foreach (var s in statements) HoistTopLevel(s);
        foreach (var s in statements) EmitStmt(s);

        PopTypeScope();
        return new CompileResult(_code, _errors, _symbolTable);
    }
    
    private void PushTypeScope() => _typeScopes.Push(new Dictionary<string, string>());
    private void PopTypeScope()  => _typeScopes.Pop();
 
    private void DeclareVarType(string name, string type)
    {
        if (_typeScopes.Count > 0) _typeScopes.Peek()[name] = type;
    }

    private bool IsKnownClass(string name) =>
        _classes.ContainsKey(name) ||
        _symbolTable.Lookup(name).Any(s => s.Kind == SymbolKind.Class);

    private bool IsKnownEnum(string name) =>
        _enums.ContainsKey(name) ||
        _symbolTable.Lookup(name).Any(s => s.Kind == SymbolKind.Enum);

    private bool TryGetEnumValue(string enumName, string member, out int value)
    {
        if (_enums.TryGetValue(enumName, out var dict) && dict.TryGetValue(member, out value))
            return true;

        var members = _symbolTable.MembersOf(enumName)
                                  .Where(s => s.Kind == SymbolKind.EnumMember)
                                  .ToList();
        var idx = members.FindIndex(s => s.Name == member);
        if (idx >= 0) { value = idx; return true; }

        value = 0;
        return false;
    }

    private bool IsMethodOf(string className, string funcName) =>
        (_classes.TryGetValue(className, out var m) && m.Methods.Any(mm => mm.Name == funcName)) ||
        _symbolTable.MembersOf(className).Any(s => s.Name == funcName && s.Kind == SymbolKind.Method);

    private int Emit(OpCode op, object? operand = null, string? name = null, object? extra = null)
    {
        _code.Add(new Instruction(op, operand, name, extra));
        return _code.Count - 1;
    }
 
    private void Patch(int idx, int target) => _code[idx].Operand = target;
 
    private int Ip => _code.Count;
 
    private void Error(string msg, Position pos) => _errors.Add(new CompileError(msg, pos));
    
    private void HoistTopLevel(IStmt stmt)
    {
        switch (stmt)
        {
            case FuncDecl:
                break;
 
            case ClassDecl c:
                var meta = new ClassMeta(c.Name, c.Base);
                foreach (var f in c.Fields)  { meta.Fields.Add(f.Name); meta.FieldDecls.Add(f); }
                foreach (var m in c.Methods) meta.Methods.Add(m);
                meta.Interfaces.AddRange(c.Interfaces);
                _classes[c.Name] = meta;
                break;
 
            case InterfaceDecl i:
                _interfaces[i.Name] = i;
                break;
 
            case StructDecl s:
                _structs[s.Name] = s.Fields;
                break;
 
            case EnumDecl e:
                var vals = new Dictionary<string, int>();
                for (int i = 0; i < e.Members.Count; i++) vals[e.Members[i]] = i;
                _enums[e.Name] = vals;
                break;
        }
    }
    
    private void EmitStmt(IStmt stmt)
    {
        switch (stmt)
        {
            case ImportStmt:    break;
            case VarDecl v:     EmitVarDecl(v);   break;
            case FuncDecl f:    EmitFuncDecl(f);  break;
            case ClassDecl c:   EmitClassDecl(c); break;
            case InterfaceDecl: break;
            case StructDecl:  EmitStructDecl(); break;
            case EnumDecl:      break;
            case IfStmt ifs:    EmitIf(ifs);       break;
            case WhileStmt w:   EmitWhile(w);      break;
            case ForStmt f:     EmitFor(f);        break;
            case ForeachStmt fe:EmitForeach(fe);   break;
            case SwitchStmt sw: EmitSwitch(sw);    break;
            case ReturnStmt r:  EmitReturn(r);     break;
            case ExprStmt es:
                EmitExpr(es.Expr);
                //Emit(OpCode.Pop);
                break;
        }
    }
 
    private void EmitVarDecl(VarDecl v)
    {
        if (v.Init is not null)
        {
            EmitExpr(v.Init);
        }
        else
        {
            EmitDefaultValue(v.Type);
        }
 
        Emit(OpCode.Declare, name: v.Name);
        DeclareVarType(v.Name, TypeRefToString(v.Type));
    }
 
    private void EmitDefaultValue(TypeRef t)
    {
        switch (t.Name)
        {
            case "int":    Emit(OpCode.Push, Value.FromInt(0));    break;
            case "float":  Emit(OpCode.Push, Value.FromFloat(0.0));break;
            case "bool":   Emit(OpCode.Push, Value.FromBool(false));break;
            case "string": Emit(OpCode.Push, Value.FromString("")); break;
            default:
                if (_structs.ContainsKey(t.Name))
                {
                    var fieldNames = GetAllStructFields(t.Name);
                    Emit(OpCode.CreateStruct, name: t.Name, extra: fieldNames);
                }
                else if (_classes.ContainsKey(t.Name))
                {
                    EmitCreateObject(t.Name);
                }
                else
                {
                    Emit(OpCode.Push, Value.Null);
                }
                break;
        }
    }

    private void EmitFuncDecl(FuncDecl f, string? ownerClass = null)
    {
        string fqn  = ownerClass is null ? f.Name : $"{ownerClass}.{f.Name}";
        var paramNames = f.Params.Select(p => p.Name).ToList();
        
        int funcIdx = Emit(OpCode.Function, operand: 0, name: fqn, extra: paramNames);
        int unused = Ip;
 
        PushTypeScope();
 
        foreach (var tp in f.TypeParams)
            DeclareVarType(tp.Name, tp.Name);
 
        foreach (var p in f.Params)
            DeclareVarType(p.Name, TypeRefToString(p.Type));
 
        if (ownerClass is not null && _classes.TryGetValue(ownerClass, out var meta))
            foreach (var field in meta.FieldDecls)
                DeclareVarType(field.Name, TypeRefToString(field.Type));
 
        foreach (var s in f.Body) EmitStmt(s);
 
        PopTypeScope();
 
        Emit(OpCode.Push, Value.Null);
        Emit(OpCode.Return);
 
        Patch(funcIdx, Ip);
    }
    
    private void EmitClassDecl(ClassDecl c)
    {
        if (!_classes.TryGetValue(c.Name, out var meta)) return;
 
        foreach (var inter in c.Interfaces)
        {
            if (!_interfaces.TryGetValue(inter, out var id)) continue;
            foreach (var m in id.Methods)
            {
                if (meta.Methods.All(cm => cm.Name != m.Name))
                    Error($"Class '{c.Name}' does not implement '{inter}.{m.Name}'", c.Span.Start);
            }
        }
 
        if (c.Base is not null)
            Emit(OpCode.Inherit, operand: c.Base, name: c.Name);
 
        var savedClass = _currentClass;
        _currentClass = c.Name;
 
        GetAllClassFields(c.Name);
 
        foreach (var m in c.Methods)
            EmitFuncDecl(m, ownerClass: c.Name);
 
        _currentClass = savedClass;
    }
 
    private void EmitStructDecl()
    {
    }
    
    private void EmitIf(IfStmt s)
    {
        EmitExpr(s.Condition);
        int jmpFalseIdx = Emit(OpCode.JumpIfFalse, 0);
 
        Emit(OpCode.EnterScope);
        PushTypeScope();
        foreach (var st in s.Then) EmitStmt(st);
        PopTypeScope();
        Emit(OpCode.ExitScope);
 
        if (s.Else is not null)
        {
            int jmpEndIdx = Emit(OpCode.Jump, 0);
            Patch(jmpFalseIdx, Ip);
 
            Emit(OpCode.EnterScope);
            PushTypeScope();
            foreach (var st in s.Else) EmitStmt(st);
            PopTypeScope();
            Emit(OpCode.ExitScope);
 
            Patch(jmpEndIdx, Ip);
        }
        else
        {
            Patch(jmpFalseIdx, Ip);
        }
    }
 
    private void EmitWhile(WhileStmt s)
    {
        int loopStart = Ip;
        EmitExpr(s.Condition);
        int jmpFalseIdx = Emit(OpCode.JumpIfFalse, 0);
 
        Emit(OpCode.EnterScope);
        PushTypeScope();
        foreach (var st in s.Body) EmitStmt(st);
        PopTypeScope();
        Emit(OpCode.ExitScope);
 
        Emit(OpCode.Jump, loopStart);
        Patch(jmpFalseIdx, Ip);
    }
 
    private void EmitFor(ForStmt s)
    {
        Emit(OpCode.EnterScope);
        PushTypeScope();
 
        if (s.Init is not null) EmitStmt(s.Init);
 
        int loopStart = Ip;
        if (s.Condition is not null)
        {
            EmitExpr(s.Condition);
        }
        else
        {
            Emit(OpCode.Push, Value.FromBool(true));
        }
 
        int jmpFalseIdx = Emit(OpCode.JumpIfFalse, 0);
 
        Emit(OpCode.EnterScope);
        PushTypeScope();
        foreach (var st in s.Body) EmitStmt(st);
        PopTypeScope();
        Emit(OpCode.ExitScope);
 
        if (s.Step is not null)
        {
            EmitExpr(s.Step);
            Emit(OpCode.Pop);
        }
 
        Emit(OpCode.Jump, loopStart);
        Patch(jmpFalseIdx, Ip);
 
        PopTypeScope();
        Emit(OpCode.ExitScope);
    }
 
    private void EmitForeach(ForeachStmt s)
    {
        Emit(OpCode.EnterScope);
        PushTypeScope();
 
        string idxVar = $"__idx_{Ip}";
        Emit(OpCode.Push, Value.FromInt(0));
        Emit(OpCode.Declare, name: idxVar);
        DeclareVarType(idxVar, "int");
 
        int loopStart = Ip;
 
        Emit(OpCode.Load, name: idxVar);
        EmitExpr(s.Collection);
        Emit(OpCode.ArrayLength);
        Emit(OpCode.Less);
 
        int jmpFalseIdx = Emit(OpCode.JumpIfFalse, 0);
 
        Emit(OpCode.EnterScope);
        PushTypeScope();
 
        EmitExpr(s.Collection);
        Emit(OpCode.Load, name: idxVar);
        Emit(OpCode.ArrayLoad);
        Emit(OpCode.Declare, name: s.ItemName);
        DeclareVarType(s.ItemName, TypeRefToString(s.ItemType));
 
        foreach (var st in s.Body) EmitStmt(st);
 
        PopTypeScope();
        Emit(OpCode.ExitScope);
 
        Emit(OpCode.Load, name: idxVar);
        Emit(OpCode.Push, Value.FromInt(1));
        Emit(OpCode.Add);
        Emit(OpCode.Store, name: idxVar);
 
        Emit(OpCode.Jump, loopStart);
        Patch(jmpFalseIdx, Ip);
 
        PopTypeScope();
        Emit(OpCode.ExitScope);
    }
 
    private void EmitSwitch(SwitchStmt s)
    {
        Emit(OpCode.EnterScope);
        PushTypeScope();
 
        string switchVar = $"__sw_{Ip}";
        EmitExpr(s.Value);
        Emit(OpCode.Declare, name: switchVar);
        DeclareVarType(switchVar, "?");
 
        var endJumps = new List<int>();
        SwitchCase? defaultCase = null;
 
        foreach (var c in s.Cases)
        {
            if (c.Value is null) { defaultCase = c; continue; }
 
            Emit(OpCode.Load, name: switchVar);
            EmitExpr(c.Value);
            Emit(OpCode.Equal);
            int jmpFalseIdx = Emit(OpCode.JumpIfFalse, 0);
 
            Emit(OpCode.EnterScope);
            PushTypeScope();
            foreach (var st in c.Body) EmitStmt(st);
            PopTypeScope();
            Emit(OpCode.ExitScope);
 
            endJumps.Add(Emit(OpCode.Jump, 0));
            Patch(jmpFalseIdx, Ip);
        }
 
        if (defaultCase is not null)
        {
            Emit(OpCode.EnterScope);
            PushTypeScope();
            foreach (var st in defaultCase.Body) EmitStmt(st);
            PopTypeScope();
            Emit(OpCode.ExitScope);
        }
 
        foreach (var j in endJumps) Patch(j, Ip);
 
        PopTypeScope();
        Emit(OpCode.ExitScope);
    }
 
    private void EmitReturn(ReturnStmt r)
    {
        if (r.Value is not null)
            EmitExpr(r.Value);
        else
            Emit(OpCode.Push, Value.Null);
 
        Emit(OpCode.Return);
    }
    
    private void EmitExpr(IExpr expr)
    {
        switch (expr)
        {
            case IntLit    x: Emit(OpCode.Push, Value.FromInt(x.Value));    break;
            case FloatLit  x: Emit(OpCode.Push, Value.FromFloat(x.Value));  break;
            case StringLit x: Emit(OpCode.Push, Value.FromString(x.Value)); break;
            case BoolLit   x: Emit(OpCode.Push, Value.FromBool(x.Value));   break;
            case NullLit   : Emit(OpCode.Push, Value.Null);                 break;
 
            case NameExpr n:  EmitName(n);    break;
            case BinaryExpr b:EmitBinary(b);  break;
            case UnaryExpr u: EmitUnary(u);   break;
            case AssignExpr a:EmitAssign(a);  break;
            case CallExpr c:  EmitCall(c);    break;
            case MemberExpr m:EmitMember(m);  break;
            case IndexExpr i: EmitIndex(i);   break;
            case ArrayExpr a: EmitArrayLit(a);break;
            case NewExpr n:   EmitNew(n);     break;
            case SuperExpr s: EmitSuper(s);   break;
            case TernaryExpr t: EmitTernary(t); break;
            case InterpolatedStringExpr interp: EmitInterpolatedString(interp); break;
            case CoalesceExpr c: EmitCoalesce(c); break;
        }
    }

    private void EmitCoalesce(CoalesceExpr c)
    {
        EmitExpr(c.Left);
        Emit(OpCode.Dup);

        int jumpIfNotNull = Emit(OpCode.JumpIfTrue, 0);
        
        Emit(OpCode.Pop);
        EmitExpr(c.Right);
        
        Patch(jumpIfNotNull, Ip);
    }
 
    private void EmitName(NameExpr n)
    {
        if (n.Name == "this") { Emit(OpCode.PushThis); return; }
        Emit(OpCode.Load, name: n.Name);
    }
 
    private void EmitBinary(BinaryExpr b)
    {
        if (b.Op == TokenType.AndAnd)
        {
            EmitExpr(b.Left);
            int jmp = Emit(OpCode.JumpIfFalse, 0);
            EmitExpr(b.Right);
            int end = Emit(OpCode.Jump, 0);
            Patch(jmp, Ip);
            Emit(OpCode.Push, Value.FromBool(false));
            Patch(end, Ip);
            return;
        }
        if (b.Op == TokenType.OrOr)
        {
            EmitExpr(b.Left);
            int jmp = Emit(OpCode.JumpIfTrue, 0);
            EmitExpr(b.Right);
            int end = Emit(OpCode.Jump, 0);
            Patch(jmp, Ip);
            Emit(OpCode.Push, Value.FromBool(true));
            Patch(end, Ip);
            return;
        }
        
        if (b.Op == TokenType.Is)
        {
            EmitExpr(b.Left);
            string? typeName = null;
            switch (b.Right)
            {
                case NameExpr n:
                    typeName = n.Name;
                    Emit(OpCode.Push, Value.FromString(n.Name));
                    break;

                case StringLit s:
                    typeName = s.Value;
                    Emit(OpCode.Push, Value.FromString(s.Value));
                    break;

                default:
                    Error("Invalid type in 'is' expression", default);
                    Emit(OpCode.Push, Value.FromString("unknown"));
                    break;
            }
            Emit(OpCode.Is, name: typeName);
            return;
        }
        
        if (b is { Left: IntLit li, Right: IntLit ri })
        {
            int? folded = b.Op switch
            {
                TokenType.Plus    => li.Value + ri.Value,
                TokenType.Minus   => li.Value - ri.Value,
                TokenType.Star    => li.Value * ri.Value,
                TokenType.Slash   => ri.Value != 0 ? li.Value / ri.Value : null,
                TokenType.Percent => ri.Value != 0 ? li.Value % ri.Value : null,
                _ => null
            };
            if (folded.HasValue) { Emit(OpCode.Push, Value.FromInt(folded.Value)); return; }
        }
 
        EmitExpr(b.Left);
        EmitExpr(b.Right);
 
        var op = b.Op switch
        {
            TokenType.Plus         => OpCode.Add,
            TokenType.Minus        => OpCode.Sub,
            TokenType.Star         => OpCode.Mul,
            TokenType.Slash        => OpCode.Div,
            TokenType.Percent      => OpCode.Mod,
            TokenType.EqualEqual   => OpCode.Equal,
            TokenType.NotEqual     => OpCode.NotEqual,
            TokenType.Greater      => OpCode.Greater,
            TokenType.Less         => OpCode.Less,
            TokenType.GreaterEqual => OpCode.GreaterEqual,
            TokenType.LessEqual    => OpCode.LessEqual,
            _ => throw new Exception($"Unknown binary op {b.Op}")
        };
        Emit(op);
    }
 
    private void EmitUnary(UnaryExpr u)
    {
        EmitExpr(u.Operand);
        if (u.Op == TokenType.Minus) Emit(OpCode.Negate);
        else if (u.Op == TokenType.Bang) Emit(OpCode.Not);
    }
 
    private void EmitAssign(AssignExpr a)
    {
        switch (a.Target)
        {
            case NameExpr n:
                EmitExpr(a.Value);
                Emit(OpCode.Dup);
                Emit(OpCode.Store, name: n.Name);
                break;
 
            case MemberExpr m:
                EmitExpr(a.Value);
                Emit(OpCode.Dup);
                EmitExpr(m.Object);
                Emit(OpCode.Swap);
                Emit(OpCode.StoreField, name: m.Member);
                break;
 
            case IndexExpr i:
                EmitExpr(i.Object);
                EmitExpr(i.Index);
                EmitExpr(a.Value);
                Emit(OpCode.ArrayStore);
                break;
 
            default:
                Error("Invalid assignment target", a.Span.Start);
                Emit(OpCode.Push, Value.Null);
                break;
        }
    }
 
    private void EmitCall(CallExpr c)
    {
        if (c.Callee is NameExpr ne)
        {
            if (TryEmitBuiltin(ne.Name, c)) return;

            if (_externals.TryGetValue(ne.Name, out _))
            {
                foreach (var a in c.Args) EmitExpr(a);
                Emit(OpCode.CallExternal, c.Args.Count, name: ne.Name);
                return;
            }

            if (IsKnownClass(ne.Name))
            {
                EmitCreateObject(ne.Name);
                EmitConstructorCall(ne.Name, c.Args, c.Span.Start);
                return;
            }

            if (_currentClass is not null && IsMethodOf(_currentClass, ne.Name))
            {
                Emit(OpCode.PushThis);
                foreach (var a in c.Args) EmitExpr(a);
                Emit(OpCode.CallMethod, c.Args.Count, name: ne.Name);
                return;
            }

            foreach (var a in c.Args) EmitExpr(a);
            Emit(OpCode.Call, c.Args.Count, name: ne.Name);
            return;
        }

        if (c.Callee is MemberExpr me)
        {
            if (me.Object is SuperExpr)
            {
                if (_currentClass is null)
                {
                    Error("'super' outside class", c.Span.Start);
                    return;
                }
                var baseClass = _classes.GetValueOrDefault(_currentClass)?.Base;
                if (baseClass is null)
                {
                    Error($"Class '{_currentClass}' has no base class", c.Span.Start);
                    return;
                }
                Emit(OpCode.PushThis);
                foreach (var a in c.Args) EmitExpr(a);
                Emit(OpCode.CallMethod, c.Args.Count, name: me.Member, extra: baseClass);
                return;
            }

            if (me.Object is NameExpr modName)
            {
                string extKey = $"{modName.Name}.{me.Member}";
                if (_externals.TryGetValue(extKey, out _))
                {
                    foreach (var a in c.Args) EmitExpr(a);
                    Emit(OpCode.CallExternal, c.Args.Count, name: extKey);
                    return;
                }
            }

            EmitExpr(me.Object);
            foreach (var a in c.Args) EmitExpr(a);
            Emit(OpCode.CallMethod, c.Args.Count, name: me.Member);
            return;
        }

        Error("Unsupported call expression form", c.Span.Start);
    }
 
    private void EmitMember(MemberExpr m)
    {
        if (m.Object is NameExpr oe && IsKnownEnum(oe.Name))
        {
            if (!TryGetEnumValue(oe.Name, m.Member, out var enumVal))
                Error($"Unknown enum member '{m.Member}' in '{oe.Name}'", m.Span.Start);
            Emit(OpCode.Push, Value.FromInt(enumVal));
            return;
        }

        if (m.Member == "length")
        {
            EmitExpr(m.Object);
            Emit(OpCode.ArrayLength);
            return;
        }

        EmitExpr(m.Object);
        Emit(OpCode.LoadField, name: m.Member);
    }
 
    private void EmitIndex(IndexExpr i)
    {
        EmitExpr(i.Object);
        EmitExpr(i.Index);
        Emit(OpCode.ArrayLoad);
    }
 
    private void EmitArrayLit(ArrayExpr a)
    {
        foreach (var el in a.Elements) EmitExpr(el);
        Emit(OpCode.CreateArray, a.Elements.Count);
    }
 
    private void EmitNew(NewExpr n)
    {
        EmitCreateObject(n.TypeName);
        EmitConstructorCall(n.TypeName, n.Args, n.Span.Start);
    }
 
    private void EmitSuper(SuperExpr s)
    {
        if (_currentClass is null) Error("'super' outside class", s.Span.Start);
        Emit(OpCode.PushThis);
    }
    

    private void EmitInterpolatedString(InterpolatedStringExpr interp)
    {
        if (interp.Parts.Count == 0)
        {
            Emit(OpCode.Push, Value.FromString(""));
            return;
        }

        EmitExpr(interp.Parts[0]);

        if (interp.Parts[0] is not StringLit)
        {
            Emit(OpCode.Push, Value.FromString(""));
            Emit(OpCode.Swap);
            Emit(OpCode.Add);
        }

        for (int i = 1; i < interp.Parts.Count; i++)
        {
            EmitExpr(interp.Parts[i]);
            Emit(OpCode.Add);
        }
    }

    private void EmitTernary(TernaryExpr t)
    {
        EmitExpr(t.Condition);

        int jmpFalse = Emit(OpCode.JumpIfFalse, 0);

        EmitExpr(t.Then);
        int jmpEnd = Emit(OpCode.Jump, 0);

        Patch(jmpFalse, Ip);

        EmitExpr(t.Else);

        Patch(jmpEnd, Ip);
    }
    
    private void EmitCreateObject(string className)
    {
        var allFields = GetAllClassFields(className);
        Emit(OpCode.CreateObject, name: className, extra: allFields);
        
        EmitFieldInitializers(className);
    }
 
    private void EmitFieldInitializers(string className)
    {
        if (!_classes.TryGetValue(className, out var meta)) return;
 
        if (meta.Base is not null) EmitFieldInitializers(meta.Base);
 
        foreach (var f in meta.FieldDecls)
        {
            if (f.Init is null) continue;
            Emit(OpCode.Dup);
            EmitExpr(f.Init);
            Emit(OpCode.StoreField, name: f.Name);
        }
    }
 
    private void EmitConstructorCall(string className, List<IExpr> args, Position pos)
    {
        bool hasCtor = HasConstructor(className);
        if (!hasCtor && args.Count > 0)
        {
            Error($"Class '{className}' has no constructor but received arguments", pos);
            return;
        }
        if (!hasCtor) return;
 
        Emit(OpCode.Dup);
        foreach (var a in args) EmitExpr(a);
        Emit(OpCode.CallMethod, args.Count, name: className);
        Emit(OpCode.Pop);
    }
    
    private bool TryEmitBuiltin(string name, CallExpr c)
    {
        switch (name)
        {
            case "print":
                foreach (var a in c.Args) EmitExpr(a);
                Emit(OpCode.Print);
                return true;
 
            case "input":
                Emit(OpCode.Input);
                return true;
 
            case "sleep":
                RequireArgCount(c, 1);
                EmitExpr(c.Args[0]);
                Emit(OpCode.Sleep);
                return true;
 
            case "readFile":
                RequireArgCount(c, 1);
                EmitExpr(c.Args[0]);
                Emit(OpCode.ReadFile);
                return true;
 
            case "writeFile":
                RequireArgCount(c, 2);
                EmitExpr(c.Args[0]); EmitExpr(c.Args[1]);
                Emit(OpCode.WriteFile);
                return true;
 
            case "appendFile":
                RequireArgCount(c, 2);
                EmitExpr(c.Args[0]); EmitExpr(c.Args[1]);
                Emit(OpCode.AppendFile);
                return true;
 
            case "fileExists":
                RequireArgCount(c, 1);
                EmitExpr(c.Args[0]);
                Emit(OpCode.FileExists);
                return true;
 
            case "deleteFile":
                RequireArgCount(c, 1);
                EmitExpr(c.Args[0]);
                Emit(OpCode.DeleteFile);
                return true;
 
            case "readLines":
                RequireArgCount(c, 1);
                EmitExpr(c.Args[0]);
                Emit(OpCode.ReadLines);
                return true;
 
            case "time":
                Emit(OpCode.Time);
                return true;
 
            case "timeMs":
                Emit(OpCode.TimeMs);
                return true;
 
            case "httpGet":
                RequireArgCount(c, 1);
                EmitExpr(c.Args[0]);
                Emit(OpCode.HttpGet);
                return true;
 
            case "httpPost":
                RequireArgCount(c, 2);
                EmitExpr(c.Args[0]); EmitExpr(c.Args[1]);
                Emit(OpCode.HttpPost);
                return true;
 
            case "httpPostJson":
                RequireArgCount(c, 2);
                EmitExpr(c.Args[0]); EmitExpr(c.Args[1]);
                Emit(OpCode.HttpPostJson);
                return true;
 
            case "ord":
                RequireArgCount(c, 1);
                EmitExpr(c.Args[0]);
                Emit(OpCode.Ord);
                return true;
 
            case "chr":
                RequireArgCount(c, 1);
                EmitExpr(c.Args[0]);
                Emit(OpCode.Chr);
                return true;
 
            default:
                return false;
        }
    }
    
    private void RequireArgCount(CallExpr c, int expected)
    {
        if (c.Args.Count != expected)
            Error($"'{(c.Callee as NameExpr)?.Name}' expects {expected} argument(s), got {c.Args.Count}",
                  c.Span.Start);
    }
    
    private List<string> GetAllClassFields(string className)
    {
        var result = new List<string>();
        if (!_classes.TryGetValue(className, out var meta)) return result;
        if (meta.Base is not null) result.AddRange(GetAllClassFields(meta.Base));
        result.AddRange(meta.Fields);
        return result;
    }
 
    private List<string> GetAllStructFields(string structName)
    {
        if (!_structs.TryGetValue(structName, out var fields)) return [];
        return fields.Select(f => f.Name).ToList();
    }
 
    private bool HasConstructor(string className)
    {
        if (!_classes.TryGetValue(className, out var meta)) return false;
        if (meta.Methods.Any(m => m.Name == className)) return true;
        if (meta.Base is not null) return HasConstructor(meta.Base);
        return false;
    }
    
    private static string TypeRefToString(TypeRef t)
    {
        var s = t.Name;
        if (t.TypeArgs.Count > 0) s += "<" + string.Join(",", t.TypeArgs) + ">";
        if (t.IsArray) s += "[]";
        return s;
    }
}