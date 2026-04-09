using System.Globalization;

namespace SabakaLang.Compiler;

public enum OpCode
{
    Push,
    Pop,
    Dup,
    
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

public sealed class Instruction
{
    public OpCode OpCode { get; }
    public object? Operand { get; set; }
    public string? Name { get; }
    public object? Extra { get; }
    
    public Instruction(OpCode opCode, object? operand = null, string? name = null, object? extra = null)
    {
        OpCode = opCode;
        Operand = operand;
        Name = name;
        Extra = extra;
    }
    
    public override string ToString()
    {
        var parts = new List<string> { OpCode.ToString() };
        if (Name    is not null) parts.Add($"'{Name}'");
        if (Operand is not null) parts.Add(Operand.ToString()!);
        return string.Join(" ", parts);
    }
}

internal sealed class ClassMeta
{
    public string  Name       { get; }
    public string? Base       { get; }
    public List<string>         Fields  { get; } = [];
    public List<VarDecl>        FieldDecls { get; } = [];
    public List<FuncDecl>       Methods { get; } = [];
    public List<string>         Interfaces { get; } = [];
    public ClassMeta(string name, string? @base) { Name = name; Base = @base; }
}

public readonly record struct CompileError(string Message, Position Position)
{
    public override string ToString() => $"[{Position.Line}:{Position.Column}] {Message}";
}

public sealed class CompileResult
{
    public IReadOnlyList<Instruction>  Code   { get; }
    public IReadOnlyList<CompileError> Errors { get; }
    public bool HasErrors => Errors.Count > 0;
 
    public CompileResult(IReadOnlyList<Instruction> code, IReadOnlyList<CompileError> errors)
    {
        Code   = code;
        Errors = errors;
    }
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
    public static Value FromString(string v)     => new(SabakaType.String, s: v ?? "");
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

public sealed class SabakaObject
{
    public string ClassName { get; }
    public Dictionary<string, Value> Fields { get; } = new();
 
    public SabakaObject(string className) => ClassName = className;
 
    public SabakaObject Clone()
    {
        var c = new SabakaObject(ClassName);
        foreach (var kv in Fields) c.Fields[kv.Key] = kv.Value;
        return c;
    }
 
    public override string ToString() =>
        $"{ClassName} {{ {string.Join(", ", Fields.Select(kv => $"{kv.Key}: {kv.Value}"))} }}";
}
 
public sealed class RuntimeException : Exception
{
    public RuntimeException(string msg) : base(msg) { }
}

public sealed class Compiler
{
    private readonly List<Instruction> _code = [];
    private readonly List<CompileError> _errors = [];
    
    private readonly Dictionary<string, ClassMeta>              _classes   = new();
    private readonly Dictionary<string, List<VarDecl>>         _structs   = new();
    private readonly Dictionary<string, Dictionary<string,int>> _enums     = new();
    private readonly Dictionary<string, InterfaceDecl>          _interfaces= new();
    
    private readonly Dictionary<string,(int ParamCount, bool Registered)> _externals = new();
    
    private readonly Stack<Dictionary<string, string>> _typeScopes = new();
    
    private string? _currentClass;
    
    public void RegisterExternal(string name, int paramCount)
        => _externals[name] = (paramCount, true);
 
    public CompileResult Compile(IReadOnlyList<IStmt> statements)
    {
        PushTypeScope();
 
        foreach (var s in statements) HoistTopLevel(s);
 
        foreach (var s in statements) EmitStmt(s);
 
        PopTypeScope();
        return new CompileResult(_code, _errors);
    }
    
    private void PushTypeScope() => _typeScopes.Push(new Dictionary<string, string>());
    private void PopTypeScope()  => _typeScopes.Pop();
 
    private void DeclareVarType(string name, string type)
    {
        if (_typeScopes.Count > 0) _typeScopes.Peek()[name] = type;
    }
 
    private string? GetVarType(string name)
    {
        foreach (var scope in _typeScopes)
            if (scope.TryGetValue(name, out var t)) return t;
        return null;
    }
    
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
            case FuncDecl f:
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
            case StructDecl s:  EmitStructDecl(s); break;
            case EnumDecl:      break;
            case IfStmt ifs:    EmitIf(ifs);       break;
            case WhileStmt w:   EmitWhile(w);      break;
            case ForStmt f:     EmitFor(f);        break;
            case ForeachStmt fe:EmitForeach(fe);   break;
            case SwitchStmt sw: EmitSwitch(sw);    break;
            case ReturnStmt r:  EmitReturn(r);     break;
            case ExprStmt es:
                EmitExpr(es.Expr);
                if (es.Expr is not CallExpr && es.Expr is not AssignExpr)
                    Emit(OpCode.Pop);
                break;
        }
    }
    
    private void EmitVarDecl(VarDecl v, string? ownerClass = null)
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
        int bodyStart = Ip;
 
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
 
        foreach (var iface in c.Interfaces)
        {
            if (!_interfaces.TryGetValue(iface, out var id)) continue;
            foreach (var m in id.Methods)
            {
                if (!meta.Methods.Any(cm => cm.Name == m.Name))
                    Error($"Class '{c.Name}' does not implement '{iface}.{m.Name}'", c.Span.Start);
            }
        }
 
        if (c.Base is not null)
            Emit(OpCode.Inherit, operand: c.Base, name: c.Name);
 
        var savedClass = _currentClass;
        _currentClass = c.Name;
 
        var allFields = GetAllClassFields(c.Name);
 
        foreach (var m in c.Methods)
            EmitFuncDecl(m, ownerClass: c.Name);
 
        _currentClass = savedClass;
    }
 
    private void EmitStructDecl(StructDecl s)
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
        }
    }
    
    private void EmitName(NameExpr n)
    {
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
 
        if (b.Left is IntLit li && b.Right is IntLit ri)
        {
            int? folded = b.Op switch
            {
                TokenType.Plus    => li.Value + ri.Value,
                TokenType.Minus   => li.Value - ri.Value,
                TokenType.Star    => li.Value * ri.Value,
                TokenType.Slash   => ri.Value != 0 ? li.Value / ri.Value : (int?)null,
                TokenType.Percent => ri.Value != 0 ? li.Value % ri.Value : (int?)null,
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
                Emit(OpCode.Store, name: n.Name);
                Emit(OpCode.Load,  name: n.Name);
                break;
 
            case MemberExpr m:
                EmitExpr(m.Object);
                EmitExpr(a.Value);
                Emit(OpCode.StoreField, name: m.Member);
                EmitExpr(a.Value);
                break;
 
            case IndexExpr i:
                EmitExpr(i.Object);
                EmitExpr(i.Index);
                EmitExpr(a.Value);
                Emit(OpCode.ArrayStore);
                EmitExpr(a.Value);
                break;
 
            default:
                Error("Invalid assignment target", a.Span.Start);
                break;
        }
    }
    
    private void EmitCall(CallExpr c)
    {
        if (c.Callee is NameExpr ne)
        {
            if (TryEmitBuiltin(ne.Name, c)) return;
 
            if (_externals.TryGetValue(ne.Name, out var ext))
            {
                foreach (var a in c.Args) EmitExpr(a);
                Emit(OpCode.CallExternal, c.Args.Count, name: ne.Name);
                return;
            }
 
            if (_classes.TryGetValue(ne.Name, out _))
            {
                EmitCreateObject(ne.Name);
                EmitConstructorCall(ne.Name, c.Args, c.Span.Start);
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
        if (m.Object is NameExpr oe && _enums.TryGetValue(oe.Name, out var enumVals))
        {
            if (!enumVals.TryGetValue(m.Member, out var enumVal))
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
    
    private void EmitCreateObject(string className)
    {
        var allFields = GetAllClassFields(className);
        Emit(OpCode.CreateObject, name: className, extra: allFields);
 
        // Run field initializers from base → derived
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