using SabakaLang.AST;
using SabakaLang.AST.Interfaces;
using SabakaLang.Binder;
using SabakaLang.Lexer.Enums;
using SabakaLang.Runtime;

namespace SabakaLang.Compiler;

/// <summary>Turns a successfully bound program into bytecode.</summary>
public sealed class Compiler
{
    private BindResult _bind = null!;
    private SemanticModel _m = null!;
    private CompiledProgram _p = null!;
    private readonly Dictionary<FunctionSymbol, int> _funcIndex = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<TypeSymbol, int> _classIndex = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, int> _nativeIndex = new();

    private List<Instruction> _code = [];
    private int _line;
    private List<LoopCtx> _loops = [];

    private sealed class LoopCtx(bool isLoop)
    {
        public bool IsLoop { get; } = isLoop;
        public List<int> Breaks { get; } = [];
        public List<int> Continues { get; } = [];
    }

    public CompiledProgram Compile(BindResult bind)
    {
        if (bind.HasErrors) throw new InvalidOperationException("Cannot compile a program that has binding errors.");
        _bind = bind;
        _m = bind.Model;
        _p = new CompiledProgram();

        for (var i = 0; i < bind.GlobalCount; i++) _p.Globals.Add(Value.Null);

        // 1. allocate indices so code can refer to anything
        foreach (var mod in bind.Modules)
        {
            Register(mod.Entry);
            foreach (var f in mod.Functions) Register(f);
            foreach (var t in mod.Types.Where(t => t.Kind is SymbolKind.Class or SymbolKind.Struct))
            {
                _classIndex[t] = _p.Classes.Count;
                _p.Classes.Add(null!);
            }
        }

        // 2. classes
        foreach (var mod in bind.Modules)
            foreach (var t in mod.Types.Where(t => _classIndex.ContainsKey(t)))
                _p.Classes[_classIndex[t]] = BuildClass(t);

        // 3. code
        foreach (var mod in bind.Modules)
        {
            EmitEntry(mod);
            _p.Entries.Add(_funcIndex[mod.Entry]);
            foreach (var f in mod.Functions) EmitFunction(f);
        }

        // typed defaults for globals that are declared without initialiser
        foreach (var s in bind.AllSymbols.OfType<VariableSymbol>().Where(v => v.IsGlobal))
            _p.Globals[s.Slot] = DefaultValue(s.Type);

        return _p;
    }

    private void Register(FunctionSymbol f)
    {
        _funcIndex[f] = _p.Functions.Count;
        _p.Functions.Add(new CompiledFunction
        {
            Name = f.Id,
            ArgCount = (f.Owner is not null ? 1 : 0) + f.Parameters.Count,
            LocalCount = f.LocalCount
        });
    }

    private CompiledClass BuildClass(TypeSymbol t)
    {
        var c = new CompiledClass
        {
            Id = t.Id,
            Base = t.BaseSymbol?.Id,
            IsStruct = t.Kind == SymbolKind.Struct,
            Interfaces = t.AllInterfaces().Select(i => i.Id).Distinct().ToList()
        };
        foreach (var f in t.Fields)
        {
            c.Fields.Add(new FieldInfo
            {
                Name = f.Name,
                Default = DefaultValue(f.Type),
                StructClass = f.Type is { Kind: TypeKind.Struct, IsNullable: false } && f.Type.TypeSymbol is { } ts && _classIndex.TryGetValue(ts, out var ci) ? ci : -1
            });
        }
        foreach (var m in t.Methods.Where(m => !m.IsAbstract)) c.Methods[m.Name] = _funcIndex[m];
        if (t.Constructor is { } ctor) c.Constructor = _funcIndex[ctor];
        if (t.Initializer is { } init) c.Initializer = _funcIndex[init];
        return c;
    }

    private static Value DefaultValue(BoundType t)
    {
        if (t.IsNullable) return Value.Null;
        return t.Kind switch
        {
            TypeKind.Int => Value.FromInt(0),
            TypeKind.Float => Value.FromFloat(0),
            TypeKind.Bool => Value.FromBool(false),
            TypeKind.String => Value.FromString(""),
            TypeKind.Char => Value.FromChar('\0'),
            TypeKind.Enum when t.TypeSymbol is { EnumMembers.Count: > 0 } e =>
                Value.FromEnum(new EnumMember(e.Id, e.EnumMembers[0], 0)),
            _ => Value.Null
        };
    }

    // ------------------------------------------------------------------ function bodies

    private void BeginFunction()
    {
        _code = [];
        _loops = [];
        _line = 0;
    }

    private void FinishFunction(FunctionSymbol f, int localCount)
    {
        if (_code.Count == 0 || _code[^1].Op != OpCode.Return)
        {
            Emit(OpCode.Push, arg: Value.Null);
            Emit(OpCode.Return);
        }
        var cf = _p.Functions[_funcIndex[f]];
        cf.Code = _code.ToArray();
        cf.LocalCount = Math.Max(localCount, cf.ArgCount);
    }

    private void EmitEntry(BoundModule mod)
    {
        BeginFunction();
        foreach (var st in mod.Statements)
        {
            if (st is FuncDecl or ClassDecl or InterfaceDecl or StructDecl or EnumDecl or ImportStmt) continue;
            EmitStmt(st);
        }
        FinishFunction(mod.Entry, mod.Entry.LocalCount);
    }

    private void EmitFunction(FunctionSymbol f)
    {
        if (f.IsAbstract) return;
        BeginFunction();

        if (f.Decl is null)
        {
            // synthetic <init>: run field initialisers (this = slot 0)
            _line = f.Span.Start.Line;
            foreach (var field in f.Owner!.Fields)
            {
                if (field.IsConst)
                {
                    Emit(OpCode.LoadLocal, 0);
                    Emit(OpCode.Push, arg: field.ConstValue);
                    Emit(OpCode.StoreField, arg: field.Name);
                }
                else if (field.Decl?.Init is { } init)
                {
                    _line = field.Span.Start.Line;
                    Emit(OpCode.LoadLocal, 0);
                    EmitExpr(init);
                    Emit(OpCode.StoreField, arg: field.Name);
                }
            }
            FinishFunction(f, f.LocalCount);
            return;
        }

        foreach (var st in f.Decl.Body) EmitStmt(st);
        FinishFunction(f, f.LocalCount);
    }

    // ------------------------------------------------------------------ emit helpers

    private int Emit(OpCode op, int a = 0, int b = 0, object? arg = null)
    {
        _code.Add(new Instruction(op, a, b, arg, _line));
        return _code.Count - 1;
    }

    private int EmitJump(OpCode op) => Emit(op, -1);

    private void Patch(int jump) => _code[jump] = _code[jump] with { A = _code.Count };

    private void PatchTo(int jump, int target) => _code[jump] = _code[jump] with { A = target };

    private int NativeIndex(string key, string? className = null)
    {
        if (!_nativeIndex.TryGetValue(key, out var i))
        {
            i = _p.Natives.Count;
            _p.Natives.Add(new NativeRef(key, className));
            _nativeIndex[key] = i;
        }
        return i;
    }

    private void EmitDefault(BoundType t)
    {
        if (t is { Kind: TypeKind.Struct, IsNullable: false } && t.TypeSymbol is { } ts && _classIndex.TryGetValue(ts, out var ci))
            Emit(OpCode.NewObject, ci);
        else
            Emit(OpCode.Push, arg: DefaultValue(t));
    }

    private void Convert(IExpr e)
    {
        if (!_m.Conversions.TryGetValue(e, out var c)) return;
        switch (c)
        {
            case Conversion.IntToFloat: Emit(OpCode.ToFloat); break;
            case Conversion.CharToString: Emit(OpCode.CharToString); break;
            case Conversion.CloneStruct: Emit(OpCode.CloneStruct); break;
        }
    }

    // ------------------------------------------------------------------ statements

    private void EmitStmt(IStmt stmt)
    {
        if (stmt.Span.Start.Line > 0) _line = stmt.Span.Start.Line;

        switch (stmt)
        {
            case VarDecl v:
            {
                var sym = (VariableSymbol)_m.Declarations[v];
                if (v.Init is not null) EmitExpr(v.Init); else EmitDefault(sym.Type);
                Emit(sym.IsGlobal ? OpCode.StoreGlobal : OpCode.StoreLocal, sym.Slot);
                break;
            }
            case ConstDecl or FuncDecl or ClassDecl or InterfaceDecl or StructDecl or EnumDecl or ImportStmt:
                break; // constants are inlined; declarations are compiled separately

            case ExprStmt e: EmitExprStmt(e.Expr); break;

            case ReturnStmt r:
                if (r.Value is null) Emit(OpCode.Push, arg: Value.Null); else EmitExpr(r.Value);
                Emit(OpCode.Return);
                break;

            case IfStmt i:
            {
                EmitExpr(i.Condition);
                var jElse = EmitJump(OpCode.JumpIfFalse);
                foreach (var s in i.Then) EmitStmt(s);
                if (i.Else is null) { Patch(jElse); break; }
                var jEnd = EmitJump(OpCode.Jump);
                Patch(jElse);
                foreach (var s in i.Else) EmitStmt(s);
                Patch(jEnd);
                break;
            }

            case WhileStmt w:
            {
                var top = _code.Count;
                EmitExpr(w.Condition);
                var jEnd = EmitJump(OpCode.JumpIfFalse);
                var ctx = new LoopCtx(true);
                _loops.Add(ctx);
                foreach (var s in w.Body) EmitStmt(s);
                _loops.RemoveAt(_loops.Count - 1);
                foreach (var c in ctx.Continues) PatchTo(c, top);
                Emit(OpCode.Jump, top);
                Patch(jEnd);
                foreach (var b in ctx.Breaks) Patch(b);
                break;
            }

            case ForStmt f: EmitFor(f); break;
            case ForeachStmt fe: EmitForeach(fe); break;
            case SwitchStmt sw: EmitSwitch(sw); break;

            case BreakStmt:
                _loops[^1].Breaks.Add(EmitJump(OpCode.Jump));
                break;
            case ContinueStmt:
                _loops.Last(l => l.IsLoop).Continues.Add(EmitJump(OpCode.Jump));
                break;

            default:
                throw new InvalidOperationException($"Cannot compile statement {stmt.GetType().Name}");
        }
    }

    private void EmitFor(ForStmt f)
    {
        if (f.Init is not null) EmitStmt(f.Init);
        var top = _code.Count;
        var jEnd = -1;
        if (f.Condition is not null)
        {
            EmitExpr(f.Condition);
            jEnd = EmitJump(OpCode.JumpIfFalse);
        }
        var ctx = new LoopCtx(true);
        _loops.Add(ctx);
        foreach (var s in f.Body) EmitStmt(s);
        _loops.RemoveAt(_loops.Count - 1);
        foreach (var c in ctx.Continues) Patch(c);
        if (f.Step is not null) EmitExprStmt(f.Step);
        Emit(OpCode.Jump, top);
        if (jEnd >= 0) Patch(jEnd);
        foreach (var b in ctx.Breaks) Patch(b);
    }

    private void EmitForeach(ForeachStmt fe)
    {
        var info = _m.Foreach[fe];
        EmitExpr(fe.Collection);
        Emit(OpCode.StoreLocal, info.CollectionSlot);
        Emit(OpCode.Push, arg: Value.FromInt(0));
        Emit(OpCode.StoreLocal, info.IndexSlot);

        var top = _code.Count;
        Emit(OpCode.LoadLocal, info.IndexSlot);
        Emit(OpCode.LoadLocal, info.CollectionSlot);
        Emit(OpCode.Length);
        Emit(OpCode.Lt);
        var jEnd = EmitJump(OpCode.JumpIfFalse);

        Emit(OpCode.LoadLocal, info.CollectionSlot);
        Emit(OpCode.LoadLocal, info.IndexSlot);
        Emit(OpCode.LoadIndex);
        switch (info.ItemConversion)
        {
            case Conversion.IntToFloat: Emit(OpCode.ToFloat); break;
            case Conversion.CharToString: Emit(OpCode.CharToString); break;
        }
        Emit(OpCode.StoreLocal, info.Item.Slot);

        var ctx = new LoopCtx(true);
        _loops.Add(ctx);
        foreach (var s in fe.Body) EmitStmt(s);
        _loops.RemoveAt(_loops.Count - 1);

        foreach (var c in ctx.Continues) Patch(c);
        Emit(OpCode.LoadLocal, info.IndexSlot);
        Emit(OpCode.Push, arg: Value.FromInt(1));
        Emit(OpCode.Add);
        Emit(OpCode.StoreLocal, info.IndexSlot);
        Emit(OpCode.Jump, top);
        Patch(jEnd);
        foreach (var b in ctx.Breaks) Patch(b);
    }

    private void EmitSwitch(SwitchStmt sw)
    {
        var slot = _m.Switches[sw].ValueSlot;
        EmitExpr(sw.Value);
        Emit(OpCode.StoreLocal, slot);

        var caseJumps = new List<int>();
        foreach (var c in sw.Cases)
        {
            if (c.Value is null) { caseJumps.Add(-1); continue; }
            Emit(OpCode.LoadLocal, slot);
            EmitExpr(c.Value);
            Emit(OpCode.Eq);
            caseJumps.Add(EmitJump(OpCode.JumpIfTrue));
        }
        var jDefaultOrEnd = EmitJump(OpCode.Jump);

        var ctx = new LoopCtx(false);
        _loops.Add(ctx);
        var ends = new List<int>();
        var defaultTarget = -1;
        for (var i = 0; i < sw.Cases.Count; i++)
        {
            if (caseJumps[i] >= 0) Patch(caseJumps[i]); else defaultTarget = _code.Count;
            foreach (var s in sw.Cases[i].Body) EmitStmt(s);
            ends.Add(EmitJump(OpCode.Jump)); // no fall-through
        }
        _loops.RemoveAt(_loops.Count - 1);

        if (defaultTarget >= 0) PatchTo(jDefaultOrEnd, defaultTarget); else Patch(jDefaultOrEnd);
        foreach (var e in ends) Patch(e);
        foreach (var b in ctx.Breaks) Patch(b);
    }

    private void EmitExprStmt(IExpr e)
    {
        if (e.Span.Start.Line > 0) _line = e.Span.Start.Line;
        if (e is AssignExpr a && !_m.Constants.ContainsKey(a))
        {
            EmitAssign(a, needValue: false);
            return;
        }
        EmitExpr(e);
        Emit(OpCode.Pop);
    }

    // ------------------------------------------------------------------ expressions

    private void EmitExpr(IExpr e)
    {
        if (e.Span.Start.Line > 0) _line = e.Span.Start.Line;

        if (_m.Constants.TryGetValue(e, out var constant))
        {
            Emit(OpCode.Push, arg: constant);
            Convert(e);
            return;
        }

        switch (e)
        {
            case NullLit: Emit(OpCode.Push, arg: Value.Null); break;
            case NameExpr n: EmitName(n); break;
            case BinaryExpr b: EmitBinary(b); break;
            case UnaryExpr u:
                EmitExpr(u.Operand);
                Emit(u.Op == TokenType.Minus ? OpCode.Neg : OpCode.Not);
                break;
            case AssignExpr a: EmitAssign(a, needValue: true); break;
            case CallExpr c: EmitCall(c); break;
            case MemberExpr m: EmitMember(m); break;
            case IndexExpr i:
                EmitExpr(i.Object);
                EmitExpr(i.Index);
                Emit(OpCode.LoadIndex);
                break;
            case ArrayExpr a:
                foreach (var el in a.Elements) EmitExpr(el);
                Emit(OpCode.BuildArray, a.Elements.Count);
                break;
            case NewArrayExpr na:
            {
                EmitExpr(na.Size);
                var elem = _m.ArrayElementTypes[na];
                var structIdx = elem is { Kind: TypeKind.Struct, IsNullable: false } && elem.TypeSymbol is { } ts && _classIndex.TryGetValue(ts, out var ci) ? ci : -1;
                Emit(OpCode.NewArray, structIdx, 0, DefaultValue(elem));
                break;
            }
            case NewExpr n: EmitNew(n); break;
            case TernaryExpr t:
            {
                EmitExpr(t.Condition);
                var jElse = EmitJump(OpCode.JumpIfFalse);
                EmitExpr(t.Then);
                var jEnd = EmitJump(OpCode.Jump);
                Patch(jElse);
                EmitExpr(t.Else);
                Patch(jEnd);
                break;
            }
            case CoalesceExpr c:
            {
                EmitExpr(c.Left);
                var j = EmitJump(OpCode.JumpIfNotNullKeep);
                EmitExpr(c.Right);
                Patch(j);
                break;
            }
            case InterpolatedStringExpr s:
            {
                if (s.Parts.Count == 0) { Emit(OpCode.Push, arg: Value.FromString("")); break; }
                foreach (var part in s.Parts)
                {
                    EmitExpr(part);
                    if (!(part is StringLit)) Emit(OpCode.Stringify);
                }
                Emit(OpCode.Concat, s.Parts.Count);
                break;
            }
            case IsExpr i:
                EmitExpr(i.Left);
                Emit(OpCode.IsType, arg: MakeTypeTest(_m.IsTypes[i]));
                break;
            default:
                throw new InvalidOperationException($"Cannot compile expression {e.GetType().Name}");
        }

        Convert(e);
    }

    private static TypeTest MakeTypeTest(BoundType t) => t.Kind switch
    {
        TypeKind.Int => new TypeTest("int", null, t.IsNullable),
        TypeKind.Float => new TypeTest("float", null, t.IsNullable),
        TypeKind.Bool => new TypeTest("bool", null, t.IsNullable),
        TypeKind.String => new TypeTest("string", null, t.IsNullable),
        TypeKind.Char => new TypeTest("char", null, t.IsNullable),
        TypeKind.Array => new TypeTest("array", null, t.IsNullable),
        TypeKind.Enum => new TypeTest("enum", ((TypeSymbol)t.Symbol!).Id, t.IsNullable),
        TypeKind.Class or TypeKind.Interface or TypeKind.Struct => new TypeTest("class", ((TypeSymbol)t.Symbol!).Id, t.IsNullable),
        _ => new TypeTest("any", null, true)
    };

    private void EmitName(NameExpr n)
    {
        switch (_m.Names[n])
        {
            case VariableSymbol v:
                Emit(v.IsGlobal ? OpCode.LoadGlobal : OpCode.LoadLocal, v.Slot);
                break;
            case FieldSymbol f:
                Emit(OpCode.LoadLocal, 0);
                Emit(OpCode.LoadField, arg: f.Name);
                break;
            default:
                throw new InvalidOperationException($"Name '{n.Name}' is not a value");
        }
    }

    private void EmitMember(MemberExpr m)
    {
        var b = _m.Members[m];
        switch (b.Kind)
        {
            case MemberKind.ModuleVariable:
                Emit(OpCode.LoadGlobal, ((VariableSymbol)b.Symbol!).Slot);
                break;
            case MemberKind.Length:
                EmitExpr(m.Object);
                Emit(OpCode.Length);
                break;
            case MemberKind.NativeProperty or MemberKind.ExtensionProperty:
                EmitExpr(m.Object);
                Emit(OpCode.CallNative, NativeIndex(b.NativeKey!), 1);
                break;
            default: // Field, Dynamic
                EmitExpr(m.Object);
                Emit(OpCode.LoadField, arg: m.Member);
                break;
        }
    }

    private static OpCode BinOp(TokenType t) => t switch
    {
        TokenType.Plus => OpCode.Add, TokenType.Minus => OpCode.Sub, TokenType.Star => OpCode.Mul,
        TokenType.Slash => OpCode.Div, TokenType.Percent => OpCode.Mod,
        TokenType.EqualEqual => OpCode.Eq, TokenType.NotEqual => OpCode.Ne,
        TokenType.Less => OpCode.Lt, TokenType.Greater => OpCode.Gt,
        TokenType.LessEqual => OpCode.Le, TokenType.GreaterEqual => OpCode.Ge,
        _ => throw new InvalidOperationException($"Unknown binary operator {t}")
    };

    private void EmitBinary(BinaryExpr b)
    {
        if (b.Op is TokenType.AndAnd or TokenType.OrOr)
        {
            EmitExpr(b.Left);
            var j = EmitJump(b.Op == TokenType.AndAnd ? OpCode.JumpIfFalseKeep : OpCode.JumpIfTrueKeep);
            EmitExpr(b.Right);
            Patch(j);
            return;
        }
        EmitExpr(b.Left);
        EmitExpr(b.Right);
        Emit(BinOp(b.Op));
    }

    // ------------------------------------------------------------------ assignment

    /// <summary>Parser desugars <c>x += e</c> into <c>x = x + e</c> sharing the target node: the target is evaluated once.</summary>
    private static bool IsCompound(AssignExpr a, out BinaryExpr binary)
    {
        binary = (a.Value as BinaryExpr)!;
        return a.Value is BinaryExpr b && ReferenceEquals(b.Left, a.Target) && b.Op is not (TokenType.AndAnd or TokenType.OrOr);
    }

    private void EmitAssign(AssignExpr a, bool needValue)
    {
        var compound = IsCompound(a, out var bin);
        var keep = needValue ? 1 : 0;

        switch (a.Target)
        {
            case NameExpr n:
                switch (_m.Names[n])
                {
                    case VariableSymbol v:
                        EmitExpr(a.Value);
                        if (needValue) Emit(OpCode.Dup);
                        Emit(v.IsGlobal ? OpCode.StoreGlobal : OpCode.StoreLocal, v.Slot);
                        break;
                    case FieldSymbol f:
                        Emit(OpCode.LoadLocal, 0);
                        if (compound)
                        {
                            Emit(OpCode.Dup);
                            Emit(OpCode.LoadField, arg: f.Name);
                            EmitCompoundTail(bin);
                        }
                        else EmitExpr(a.Value);
                        Emit(OpCode.StoreField, 0, keep, f.Name);
                        break;
                    default:
                        throw new InvalidOperationException($"Cannot assign to '{n.Name}'");
                }
                break;

            case MemberExpr m:
            {
                var b = _m.Members[m];
                if (b.Kind == MemberKind.ModuleVariable)
                {
                    EmitExpr(a.Value);
                    if (needValue) Emit(OpCode.Dup);
                    Emit(OpCode.StoreGlobal, ((VariableSymbol)b.Symbol!).Slot);
                    break;
                }

                EmitExpr(m.Object);
                if (b.Kind == MemberKind.NativeProperty)
                {
                    var prop = ((PropertySymbol)b.Symbol!).Native;
                    if (compound)
                    {
                        Emit(OpCode.Dup);
                        Emit(OpCode.CallNative, NativeIndex(prop.GetAccessor.Key), 1);
                        EmitCompoundTail(bin);
                    }
                    else EmitExpr(a.Value);
                    Emit(OpCode.CallNative, NativeIndex(prop.SetAccessor!.Key), 2);
                    if (!needValue) Emit(OpCode.Pop);
                    break;
                }

                if (compound)
                {
                    Emit(OpCode.Dup);
                    Emit(OpCode.LoadField, arg: m.Member);
                    EmitCompoundTail(bin);
                }
                else EmitExpr(a.Value);
                Emit(OpCode.StoreField, 0, keep, m.Member);
                break;
            }

            case IndexExpr ix:
                EmitExpr(ix.Object);
                EmitExpr(ix.Index);
                if (compound)
                {
                    Emit(OpCode.Dup2);
                    Emit(OpCode.LoadIndex);
                    EmitCompoundTail(bin);
                }
                else EmitExpr(a.Value);
                Emit(OpCode.StoreIndex, 0, keep);
                break;

            default:
                throw new InvalidOperationException("Invalid assignment target");
        }
    }

    /// <summary>With the current value of the target on the stack: evaluate the right operand, apply the operator and conversions.</summary>
    private void EmitCompoundTail(BinaryExpr bin)
    {
        EmitExpr(bin.Right);
        Emit(BinOp(bin.Op));
        Convert(bin);
    }

    // ------------------------------------------------------------------ calls & object creation

    private void EmitArgs(IEnumerable<IExpr> args)
    {
        foreach (var a in args) EmitExpr(a);
    }

    private void EmitCall(CallExpr c)
    {
        var b = _m.Calls[c];
        var argc = c.Args.Count;
        switch (b.Kind)
        {
            case CallKind.Function:
                EmitArgs(c.Args);
                Emit(OpCode.Call, _funcIndex[b.Target!], argc);
                break;

            case CallKind.Method:
                if (b.ImplicitThis) Emit(OpCode.LoadLocal, 0);
                else EmitExpr(((MemberExpr)c.Callee).Object);
                EmitArgs(c.Args);
                Emit(OpCode.CallVirtual, 0, argc, b.MethodName);
                break;

            case CallKind.SuperMethod:
                Emit(OpCode.LoadLocal, 0);
                EmitArgs(c.Args);
                Emit(OpCode.CallDirect, _funcIndex[b.Target!], argc + 1);
                break;

            case CallKind.SuperConstructor:
                if (b.Target is null) { Emit(OpCode.Push, arg: Value.Null); break; }
                Emit(OpCode.LoadLocal, 0);
                EmitArgs(c.Args);
                Emit(OpCode.CallDirect, _funcIndex[b.Target], argc + 1);
                break;

            case CallKind.Native:
                EmitArgs(c.Args);
                Emit(OpCode.CallNative, NativeIndex(b.NativeKey!), argc);
                break;

            case CallKind.NativeMethod:
                EmitExpr(((MemberExpr)c.Callee).Object);
                EmitArgs(c.Args);
                Emit(OpCode.CallNative, NativeIndex(b.NativeKey!), argc + 1);
                break;

            case CallKind.Dynamic:
                EmitExpr(((MemberExpr)c.Callee).Object);
                EmitArgs(c.Args);
                Emit(OpCode.CallDynamic, 0, argc, b.MethodName);
                break;
        }
    }

    private void EmitNew(NewExpr n)
    {
        var b = _m.News[n];
        switch (b.Kind)
        {
            case NewKind.Native:
                EmitArgs(n.Args);
                Emit(OpCode.NewNative, NativeIndex(b.Constructor!.NativeKey!, b.Type.Id), n.Args.Count);
                break;

            default:
                Emit(OpCode.NewObject, _classIndex[b.Type]);
                if (b.Constructor is { } ctor)
                {
                    Emit(OpCode.Dup);
                    EmitArgs(n.Args);
                    Emit(OpCode.CallDirect, _funcIndex[ctor], n.Args.Count + 1);
                    Emit(OpCode.Pop);
                }
                break;
        }
    }
}
