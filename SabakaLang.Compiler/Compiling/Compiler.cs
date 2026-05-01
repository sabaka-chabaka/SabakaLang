using SabakaLang.Compiler.AST;
using SabakaLang.Compiler.Binding;
using SabakaLang.Compiler.Lexing;
using SabakaLang.Compiler.Runtime;

namespace SabakaLang.Compiler.Compiling;

public sealed class Compiler
{
    private readonly List<Instruction>  _code   = [];
    private readonly List<CompileError> _errors = [];
    
    private readonly Dictionary<string, ClassMeta>      _classMeta = new();
    private readonly Dictionary<string, List<VarDecl>>  _structs   = new();
    private readonly Dictionary<string, Value> _constants = new();
 
    private readonly Dictionary<string,(int ParamCount, bool Registered)> _externals = new();
 
    private readonly Stack<Dictionary<string, string>> _typeScopes = new();
 
    private string? _currentClass;

    private SymbolTable _symbolTable = new();
    
    public void RegisterExternal(string name, int paramCount)
        => _externals[name] = (paramCount, true);

    public CompileResult Compile(IReadOnlyList<IStmt> statements, BindResult bindResult)
    {
        _symbolTable = bindResult.Symbols;
        
        foreach (var sym in _symbolTable.All)
        {
            if (sym.Kind == SymbolKind.Constant)
            {
                // now it's null
            }
        }

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
        _symbolTable.Lookup(name).Any(s => s.Kind == SymbolKind.Class);

    private bool IsKnownEnum(string name) =>
        _symbolTable.Lookup(name).Any(s => s.Kind == SymbolKind.Enum);

    private bool TryGetEnumValue(string enumName, string member, out string value)
    {
        var members = _symbolTable.MembersOf(enumName)
                                  .Where(s => s.Kind == SymbolKind.EnumMember)
                                  .ToList();
        var m = members.FirstOrDefault(s => s.Name == member);
        if (m != null) { value = m.Name; return true; }

        value = "";
        return false;
    }

    private bool IsMethodOf(string className, string funcName) =>
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
            case ClassDecl c:
                var meta = new ClassMeta(c.Name, c.Base);
                foreach (var f in c.Fields)  { meta.Fields.Add(f.Name); meta.FieldDecls.Add(f); }
                foreach (var m in c.Methods) meta.Methods.Add(m);
                meta.Interfaces.AddRange(c.Interfaces);
                _classMeta[c.Name] = meta;
                break;
 
            case StructDecl s:
                _structs[s.Name] = s.Fields;
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
            case ConstDecl c: EmitConstDecl(c); break;
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
            case "char": Emit(OpCode.Push, Value.FromChar('\0')); break;
            default:
                if (_structs.ContainsKey(t.Name) ||
                    _symbolTable.Lookup(t.Name).Any(s => s.Kind == SymbolKind.Struct))
                {
                    var fieldNames = GetAllStructFields(t.Name);
                    Emit(OpCode.CreateStruct, name: t.Name, extra: fieldNames);
                }
                else if (IsKnownClass(t.Name))
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
 
        if (ownerClass is not null && _classMeta.TryGetValue(ownerClass, out var meta))
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
        if (!_classMeta.TryGetValue(c.Name, out var meta)) return;

        foreach (var inter in c.Interfaces)
        {
            var interfaceMethods = _symbolTable.MembersOf(inter)
                .Where(s => s.Kind == SymbolKind.Method)
                .ToList();
            foreach (var m in interfaceMethods)
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
    
    private void EmitConstDecl(ConstDecl c)
    {
        var value = EvaluateConstantExpression(c.Value);
    
        if (value == null)
        {
            Error("Failed to evaluate constant expression", c.Span.Start);
            value = Value.Null;
        }

        _constants[c.Name] = value.Value;

    }
    
    private Value? EvaluateConstantExpression(IExpr expr)
    {
        return expr switch
        {
            IntLit i => Value.FromInt(i.Value),
            FloatLit f => Value.FromFloat(f.Value),
            BoolLit b => Value.FromBool(b.Value),
            StringLit s => Value.FromString(s.Value),
            CharLit c => Value.FromChar(c.Value),
            NullLit => Value.Null,
            UnaryExpr u => EvaluateUnary(u),
            BinaryExpr b => EvaluateBinary(b),
            NameExpr n when _constants.TryGetValue(n.Name, out var v) => v,
            _ => null
        };
    }

    private Value? EvaluateUnary(UnaryExpr u)
    {
        var val = EvaluateConstantExpression(u.Operand);
        if (val == null) return null;

        return u.Op switch
        {
            TokenType.Minus when val.Value.Type == SabakaType.Int => Value.FromInt(-val.Value.Int),
            TokenType.Minus when val.Value.Type == SabakaType.Float => Value.FromFloat(-val.Value.Float),
            TokenType.Bang when val.Value.Type == SabakaType.Bool => Value.FromBool(!val.Value.Bool),
            _ => null
        };
    }

    private Value? EvaluateBinary(BinaryExpr b)
    {
        var left = EvaluateConstantExpression(b.Left);
        var right = EvaluateConstantExpression(b.Right);
        if (left == null || right == null) return null;

        var l = left.Value;
        var r = right.Value;

        return b.Op switch
        {
            TokenType.Plus when l.Type == SabakaType.Int && r.Type == SabakaType.Int => Value.FromInt(l.Int + r.Int),
            TokenType.Minus when l.Type == SabakaType.Int && r.Type == SabakaType.Int => Value.FromInt(l.Int - r.Int),
            TokenType.Star when l.Type == SabakaType.Int && r.Type == SabakaType.Int => Value.FromInt(l.Int * r.Int),
            TokenType.Slash when l.Type == SabakaType.Int && r.Type == SabakaType.Int => r.Int != 0 ? Value.FromInt(l.Int / r.Int) : null,
            _ => null
        };
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
            case CharLit   c: Emit(OpCode.Push, Value.FromChar(c.Value));   break;
 
            case NameExpr n:  EmitName(n);    break;
            case IsExpr ise: EmitIs(ise); break;
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

        Emit(OpCode.Push, Value.Null);
        Emit(OpCode.NotEqual);

        int jmp = Emit(OpCode.JumpIfTrue, 0);

        Emit(OpCode.Pop);
        EmitExpr(c.Right);

        Patch(jmp, Ip);
    }
 
    private void EmitName(NameExpr n)
    {
        if (_constants.TryGetValue(n.Name, out var constValue))
        {
            Emit(OpCode.Push, constValue);
            return;
        }
        if (n.Name == "this") 
        { 
            Emit(OpCode.PushThis); 
            return; 
        }
        Emit(OpCode.Load, name: n.Name);
    }
 
    private void EmitIs(IsExpr i)
    {
        EmitExpr(i.Left);
        var typeName = TypeRefToString(i.Right);
        Emit(OpCode.Push, Value.FromString(typeName));
        Emit(OpCode.Is, name: typeName);
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
                Emit(OpCode.Push, Value.Null); // RE-ADDED
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
                var baseClass = _classMeta.GetValueOrDefault(_currentClass)?.Base;
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
            Emit(OpCode.Push, Value.FromString(enumVal));
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
        var typeName = n.TypeName;
        if (n.TypeArgs.Count > 0)
        {
            typeName += "<" + string.Join(", ", n.TypeArgs) + ">";
        }
        EmitCreateObject(typeName, n.TypeName);
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
        EmitCreateObject(className, className);
    }
 
    private void EmitCreateObject(string instanceClassName, string metaClassName)
    {
        var allFields = GetAllClassFields(metaClassName);
        Emit(OpCode.CreateObject, name: instanceClassName, extra: allFields);
        
        EmitFieldInitializers(metaClassName);
    }
 
    private void EmitFieldInitializers(string className)
    {
        if (!_classMeta.TryGetValue(className, out var meta)) return;
 
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
        if (!_classMeta.TryGetValue(className, out var meta)) return result;
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
        if (!_classMeta.TryGetValue(className, out var meta)) return false;
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
