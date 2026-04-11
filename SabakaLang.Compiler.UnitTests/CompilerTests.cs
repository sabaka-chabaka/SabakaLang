using System.Runtime.InteropServices;

namespace SabakaLang.Compiler.UnitTests;

public class CompilerTests
{

    private static CompileResult Compile(string source)
    {
        var lex   = new Lexer(source).Tokenize();
        var parse = new Parser(lex).Parse();
        if (parse.HasErrors)
            throw new Exception("Parse errors:\n" +
                                string.Join("\n", parse.Errors.Select(e => e.Message)));
        var bind = new Binder().Bind(parse.Statements);
        if (bind.HasErrors)
            throw new Exception("Bind errors:\n" +
                                string.Join("\n", bind.Errors.Select(e => e.ToString())));
        return new Compiler().Compile(parse.Statements, bind);
    }

    private static CompileResult CompileUnchecked(string source)
    {
        var lex   = new Lexer(source).Tokenize();
        var parse = new Parser(lex).Parse();
        if (parse.HasErrors)
            throw new Exception("Parse errors:\n" +
                                string.Join("\n", parse.Errors.Select(e => e.Message)));
        var emptyBind = new Binder().Bind(Array.Empty<IStmt>());
        return new Compiler().Compile(parse.Statements, emptyBind);
    }

    private static IReadOnlyList<Instruction> Code(string source)
    {
        var r = Compile(source);
        Assert.False(r.HasErrors,
            "Compile errors:\n" + string.Join("\n", r.Errors.Select(e => e.ToString())));
        return r.Code;
    }

    private static bool Has(IReadOnlyList<Instruction> code, OpCode op)
        => code.Any(i => i.OpCode == op);

    private static IEnumerable<Instruction> All(IReadOnlyList<Instruction> code, OpCode op)
        => code.Where(i => i.OpCode == op);

    private static Instruction First(IReadOnlyList<Instruction> code, OpCode op)
        => code.First(i => i.OpCode == op);

    private static void AssertNoErrors(CompileResult r)
        => Assert.False(r.HasErrors,
            string.Join("\n", r.Errors.Select(e => e.ToString())));
    
    [Fact]
    public void IntLiteral_EmitsPush()
    {
        var code = Code("42;");
        var push = code.First(i => i.OpCode == OpCode.Push);
        Assert.Equal(Value.FromInt(42), push.Operand);
    }

    [Fact]
    public void FloatLiteral_EmitsPush()
    {
        var code = Code("3.14;");
        Assert.Contains(All(code, OpCode.Push),
            p => p.Operand is Value v && v.Type == SabakaType.Float);
    }

    [Fact]
    public void StringLiteral_EmitsPush()
    {
        var code = Code("\"hello\";");
        Assert.Contains(All(code, OpCode.Push),
            p => p.Operand is Value v && v.Type == SabakaType.String && v.String == "hello");
    }

    [Fact]
    public void BoolTrue_EmitsPushTrue()
    {
        var code = Code("true;");
        Assert.Contains(All(code, OpCode.Push),
            p => p.Operand is Value v && v.Type == SabakaType.Bool && v.Bool);
    }

    [Fact]
    public void BoolFalse_EmitsPushFalse()
    {
        var code = Code("false;");
        Assert.Contains(All(code, OpCode.Push),
            p => p.Operand is Value v && v.Type == SabakaType.Bool && !v.Bool);
    }

    [Fact]
    public void ConstantFolding_IntPlusInt_NoAddOpcode()
    {
        var code = Code("1 + 2;");
        Assert.False(Has(code, OpCode.Add), "Should have been folded at compile time");
        Assert.Contains(All(code, OpCode.Push), p => p.Operand is Value v && v.Int == 3);
    }

    [Fact]
    public void ConstantFolding_IntMinusInt()
    {
        var code = Code("10 - 3;");
        Assert.False(Has(code, OpCode.Sub));
        Assert.Contains(All(code, OpCode.Push), p => p.Operand is Value v && v.Int == 7);
    }

    [Fact]
    public void ConstantFolding_IntMulInt()
    {
        var code = Code("4 * 5;");
        Assert.False(Has(code, OpCode.Mul));
        Assert.Contains(All(code, OpCode.Push), p => p.Operand is Value v && v.Int == 20);
    }

    [Fact]
    public void NoFolding_IfNotBothInts()
    {
        var code = Code("int x = 1; x + 1;");
        Assert.True(Has(code, OpCode.Add));
    }

    [Theory]
    [InlineData("int a = 0; int b = 0; a + b;",  OpCode.Add)]
    [InlineData("int a = 0; int b = 0; a - b;",  OpCode.Sub)]
    [InlineData("int a = 0; int b = 0; a * b;",  OpCode.Mul)]
    [InlineData("int a = 0; int b = 0; a / b;",  OpCode.Div)]
    [InlineData("int a = 0; int b = 0; a % b;",  OpCode.Mod)]
    [InlineData("int a = 0; int b = 0; a == b;", OpCode.Equal)]
    [InlineData("int a = 0; int b = 0; a != b;", OpCode.NotEqual)]
    [InlineData("int a = 0; int b = 0; a > b;",  OpCode.Greater)]
    [InlineData("int a = 0; int b = 0; a < b;",  OpCode.Less)]
    [InlineData("int a = 0; int b = 0; a >= b;", OpCode.GreaterEqual)]
    [InlineData("int a = 0; int b = 0; a <= b;", OpCode.LessEqual)]
    public void BinaryOp_EmitsCorrectOpcode(string src, OpCode expected)
    {
        var code = Code(src);
        Assert.True(Has(code, expected), $"Expected {expected} in code");
    }
    
    [Fact]
    public void UnaryNegate_EmitsNegate()
    {
        var code = Code("int x = 1; -x;");
        Assert.True(Has(code, OpCode.Negate));
    }

    [Fact]
    public void UnaryNot_EmitsNot()
    {
        var code = Code("bool x = true; !x;");
        Assert.True(Has(code, OpCode.Not));
    }

    [Fact]
    public void AndAnd_EmitsJumpIfFalse_ForShortCircuit()
    {
        var code = Code("bool a = true; bool b = false; a && b;");
        Assert.True(Has(code, OpCode.JumpIfFalse));
        Assert.False(Has(code, OpCode.And));
    }

    [Fact]
    public void OrOr_EmitsJumpIfTrue_ForShortCircuit()
    {
        var code = Code("bool a = true; bool b = false; a || b;");
        Assert.True(Has(code, OpCode.JumpIfTrue));
        Assert.False(Has(code, OpCode.Or));
    }

    [Fact]
    public void AndAnd_EmitsPushFalseOnShortCircuit()
    {
        var code = Code("bool a = true; bool b = false; a && b;");
        Assert.Contains(All(code, OpCode.Push),
            p => p.Operand is Value v && v.Type == SabakaType.Bool && !v.Bool);
    }

    [Fact]
    public void OrOr_EmitsPushTrueOnShortCircuit()
    {
        var code = Code("bool a = true; bool b = false; a || b;");
        Assert.Contains(All(code, OpCode.Push),
            p => p.Operand is Value v && v.Type == SabakaType.Bool && v.Bool);
    }

    [Fact]
    public void VarDecl_EmitsDeclare()
    {
        var code = Code("int x = 5;");
        var decl = First(code, OpCode.Declare);
        Assert.Equal("x", decl.Name);
    }

    [Fact]
    public void VarDecl_NoInit_EmitsDefaultZero()
    {
        var code = Code("int x;");
        Assert.Contains(All(code, OpCode.Push),
            p => p.Operand is Value v && v.Type == SabakaType.Int && v.Int == 0);
        Assert.True(Has(code, OpCode.Declare));
    }

    [Fact]
    public void VarDecl_FloatNoInit_EmitsDefaultZero()
    {
        var code = Code("float f;");
        Assert.Contains(All(code, OpCode.Push),
            p => p.Operand is Value v && v.Type == SabakaType.Float);
    }

    [Fact]
    public void VarDecl_BoolNoInit_EmitsDefaultFalse()
    {
        var code = Code("bool b;");
        Assert.Contains(All(code, OpCode.Push),
            p => p.Operand is Value v && v.Type == SabakaType.Bool && !v.Bool);
    }

    [Fact]
    public void VarDecl_StringNoInit_EmitsDefaultEmpty()
    {
        var code = Code("string s;");
        Assert.Contains(All(code, OpCode.Push),
            p => p.Operand is Value v && v.Type == SabakaType.String && v.String == "");
    }
    
    [Fact]
    public void VarLoad_EmitsLoad()
    {
        var code = Code("int x = 0; x;");
        var load = code.First(i => i.OpCode == OpCode.Load && i.Name == "x");
        Assert.Equal("x", load.Name);
    }

    [Fact]
    public void VarAssign_EmitsStore()
    {
        var code = Code("int x = 0; x = 5;");
        Assert.True(Has(code, OpCode.Store));
        Assert.Equal("x", code.First(i => i.OpCode == OpCode.Store && i.Name == "x").Name);
    }

    [Fact]
    public void VarAssign_EmitsDupBeforeStore()
    {
        var code = Code("int x = 0; x = 5;");
        var ops = code.Select(i => i.OpCode).ToList();
        int dupIdx   = ops.LastIndexOf(OpCode.Dup);
        int storeIdx = ops.LastIndexOf(OpCode.Store);
        Assert.True(dupIdx >= 0, "Expected Dup");
        Assert.True(dupIdx < storeIdx, "Dup must come before Store");
    }
    
    [Fact]
    public void FuncDecl_EmitsFunctionInstruction()
    {
        var code = Code("void foo() {}");
        Assert.True(Has(code, OpCode.Function));
        Assert.Equal("foo", First(code, OpCode.Function).Name);
    }

    [Fact]
    public void FuncDecl_SkipToAfterBody()
    {
        var code = Code("void foo() {}");
        var fn   = First(code, OpCode.Function);
        int fnIdx = code.ToList().IndexOf(fn);
        Assert.True((int)fn.Operand! > fnIdx);
    }

    [Fact]
    public void FuncDecl_BodyEndsWithReturn()
    {
        var code   = Code("void foo() {}");
        var fn     = First(code, OpCode.Function);
        int skipTo = (int)fn.Operand!;
        Assert.Equal(OpCode.Return, code[skipTo - 1].OpCode);
    }

    [Fact]
    public void FuncDecl_WithParams_NamesInExtra()
    {
        var code  = Code("int add(int a, int b) { return a + b; }");
        var fn    = First(code, OpCode.Function);
        var parms = (List<string>)fn.Extra!;
        Assert.Contains("a", parms);
        Assert.Contains("b", parms);
    }

    [Fact]
    public void FuncCall_EmitsCall()
    {
        var code = Code("void foo() {} foo();");
        Assert.True(Has(code, OpCode.Call));
        Assert.Equal("foo", First(code, OpCode.Call).Name);
    }

    [Fact]
    public void FuncCall_WithArgs_CountInOperand()
    {
        var code = Code("void foo(int a, int b, int c) {} foo(1, 2, 3);");
        var call = First(code, OpCode.Call);
        Assert.Equal(3, (int)call.Operand!);
    }

    [Fact]
    public void ReturnStmt_WithValue_EmitsReturn()
    {
        var code = Code("int f() { return 42; }");
        Assert.True(Has(code, OpCode.Return));
    }

    [Fact]
    public void ReturnStmt_NoValue_PushesNull()
    {
        var code = Code("void f() { return; }");
        var ops  = code.Select(i => i.OpCode).ToList();
        bool found = false;
        for (int i = 1; i < ops.Count; i++)
        {
            if (ops[i] == OpCode.Return && ops[i - 1] == OpCode.Push)
            {
                if (code[i - 1].Operand is Value v && v.IsNull) { found = true; break; }
            }
        }
        Assert.True(found);
    }

    [Fact]
    public void MultipleTopLevelFunctions_AllRegistered()
    {
        var code = Code("void a() {} void b() {} void c() {}");
        var fns  = All(code, OpCode.Function).Select(f => f.Name).ToList();
        Assert.Contains("a", fns);
        Assert.Contains("b", fns);
        Assert.Contains("c", fns);
    }
    
    [Fact]
    public void IfStmt_EmitsJumpIfFalse()
    {
        var code = Code("bool x = true; if (x) {}");
        Assert.True(Has(code, OpCode.JumpIfFalse));
    }

    [Fact]
    public void IfElse_EmitsJumpAndJumpIfFalse()
    {
        var code = Code("bool x = true; if (x) {} else {}");
        Assert.True(Has(code, OpCode.JumpIfFalse));
        Assert.True(Has(code, OpCode.Jump));
    }

    [Fact]
    public void IfStmt_EmitsEnterExitScope()
    {
        var code = Code("bool x = true; if (x) { int y = 1; }");
        Assert.True(Has(code, OpCode.EnterScope));
        Assert.True(Has(code, OpCode.ExitScope));
    }

    [Fact]
    public void IfStmt_JumpIfFalse_TargetAfterThen()
    {
        var code     = Code("if (true) { int x = 1; }");
        var jmpFalse = First(code, OpCode.JumpIfFalse);
        int target   = (int)jmpFalse.Operand!;
        Assert.True(target > 0 && target <= code.Count);
    }

    [Fact]
    public void IfElse_Jump_TargetAfterElse()
    {
        var code   = Code("if (true) { int x = 1; } else { int y = 2; }");
        var jump   = First(code, OpCode.Jump);
        int target = (int)jump.Operand!;
        Assert.True(target <= code.Count);
    }

    [Fact]
    public void WhileStmt_EmitsJumpIfFalseAndJump()
    {
        var code = Code("while (true) {}");
        Assert.True(Has(code, OpCode.JumpIfFalse));
        Assert.True(Has(code, OpCode.Jump));
    }

    [Fact]
    public void WhileStmt_BackJump_PointsToLoopStart()
    {
        var code  = Code("bool x = true; while (x) {}");
        var jumps = All(code, OpCode.Jump).ToList();
        foreach (var j in jumps)
        {
            int jIdx   = code.ToList().IndexOf(j);
            int target = (int)j.Operand!;
            if (target < jIdx) return;
        }
        Assert.Fail("No back jump found for while loop");
    }

    [Fact]
    public void ForStmt_EmitsExpectedOpcodes()
    {
        var code = Code("for (int i = 0; i < 10; i = i + 1) {}");
        Assert.True(Has(code, OpCode.Declare));
        Assert.True(Has(code, OpCode.Less));
        Assert.True(Has(code, OpCode.JumpIfFalse));
        Assert.True(Has(code, OpCode.Jump));
        Assert.True(Has(code, OpCode.Store));
    }

    [Fact]
    public void ForStmt_EmptyParts_StillCompiles()
    {
        var r = CompileUnchecked("for (;;) {}");
        Assert.True(Has(r.Code, OpCode.Jump));
    }

    [Fact]
    public void ForeachStmt_EmitsArrayLength()
    {
        var code = Code("int[] items = [1, 2, 3]; foreach (int x in items) {}");
        Assert.True(Has(code, OpCode.ArrayLength));
    }

    [Fact]
    public void ForeachStmt_EmitsArrayLoad()
    {
        var code = Code("int[] items = [1, 2, 3]; foreach (int x in items) {}");
        Assert.True(Has(code, OpCode.ArrayLoad));
    }

    [Fact]
    public void ForeachStmt_DeclaresLoopVar()
    {
        var code  = Code("int[] items = [1, 2, 3]; foreach (int x in items) {}");
        var decls = All(code, OpCode.Declare).Select(d => d.Name).ToList();
        Assert.Contains("x", decls);
    }
    
    [Fact]
    public void SwitchStmt_EmitsEqual_PerCase()
    {
        var code = Code("int x = 0; int y = 0; switch (x) { case 1: y = 1; case 2: y = 2; }");
        Assert.Equal(2, All(code, OpCode.Equal).Count());
    }

    [Fact]
    public void SwitchStmt_EmitsJumpAtEndOfEachCase()
    {
        var code = Code("int x = 0; int y = 0; switch (x) { case 1: y = 1; case 2: y = 2; }");
        Assert.True(All(code, OpCode.Jump).Count() >= 2);
    }

    [Fact]
    public void ArrayLiteral_EmitsCreateArray()
    {
        var code = Code("[1, 2, 3];");
        Assert.True(Has(code, OpCode.CreateArray));
        Assert.Equal(3, (int)First(code, OpCode.CreateArray).Operand!);
    }

    [Fact]
    public void ArrayLiteral_Empty_EmitsCreateArrayZero()
    {
        var code = Code("[];");
        Assert.Equal(0, (int)First(code, OpCode.CreateArray).Operand!);
    }

    [Fact]
    public void ArrayIndex_EmitsArrayLoad()
    {
        var code = Code("int[] arr = [1, 2, 3]; arr[0];");
        Assert.True(Has(code, OpCode.ArrayLoad));
    }

    [Fact]
    public void ArrayAssign_EmitsArrayStore()
    {
        var code = Code("int[] arr = [1, 2, 3]; arr[0] = 5;");
        Assert.True(Has(code, OpCode.ArrayStore));
    }

    [Fact]
    public void ArrayAssign_DupBeforeStore()
    {
        var code = Code("int[] arr = [1, 2, 3]; arr[0] = 5;");
        var ops  = code.Select(i => i.OpCode).ToList();
        int dupIdx   = ops.LastIndexOf(OpCode.Dup);
        int storeIdx = ops.IndexOf(OpCode.ArrayStore);
        Assert.True(dupIdx >= 0);
        Assert.True(dupIdx < storeIdx);
    }

    [Fact]
    public void ArrayLength_EmitsArrayLength()
    {
        var code = Code("int[] arr = [1, 2]; arr.length;");
        Assert.True(Has(code, OpCode.ArrayLength));
    }

    [Theory]
    [InlineData("print(\"x\");",            OpCode.Print)]
    [InlineData("input();",                OpCode.Input)]
    [InlineData("sleep(1);",               OpCode.Sleep)]
    [InlineData("readFile(\"f\");",         OpCode.ReadFile)]
    [InlineData("writeFile(\"f\",\"c\");",  OpCode.WriteFile)]
    [InlineData("appendFile(\"f\",\"c\");", OpCode.AppendFile)]
    [InlineData("fileExists(\"f\");",       OpCode.FileExists)]
    [InlineData("deleteFile(\"f\");",       OpCode.DeleteFile)]
    [InlineData("readLines(\"f\");",        OpCode.ReadLines)]
    [InlineData("time();",                 OpCode.Time)]
    [InlineData("timeMs();",               OpCode.TimeMs)]
    [InlineData("ord(\"a\");",             OpCode.Ord)]
    [InlineData("chr(65);",               OpCode.Chr)]
    public void BuiltIn_EmitsCorrectOpcode(string src, OpCode expected)
    {
        var code = Code(src);
        Assert.True(Has(code, expected), $"Expected {expected}");
        Assert.False(code.Any(i => i.OpCode == OpCode.Call &&
                                   i.Name == expected.ToString().ToLower()),
            "Should not emit generic Call for built-in");
    }

    [Fact]
    public void Print_DoesNotEmitCall()
    {
        var code = Code("print(\"hi\");");
        Assert.False(code.Any(i => i.OpCode == OpCode.Call && i.Name == "print"));
    }
    
    [Fact]
    public void ClassDecl_Methods_EmittedAsFunctionFqn()
    {
        var code = Code("class Dog { void speak() {} }");
        var fns  = All(code, OpCode.Function).Select(f => f.Name).ToList();
        Assert.Contains("Dog.speak", fns);
    }

    [Fact]
    public void ClassDecl_WithBase_EmitsInherit()
    {
        var code = Code("class Animal {} class Dog : Animal {}");
        Assert.True(Has(code, OpCode.Inherit));
        var inh = First(code, OpCode.Inherit);
        Assert.Equal("Dog",    inh.Name);
        Assert.Equal("Animal", inh.Operand);
    }

    [Fact]
    public void NewExpr_EmitsCreateObject()
    {
        var code = Code("class Foo {} Foo f = new Foo();");
        Assert.True(Has(code, OpCode.CreateObject));
        Assert.Equal("Foo", First(code, OpCode.CreateObject).Name);
    }

    [Fact]
    public void NewExpr_WithConstructor_EmitsCallMethod()
    {
        var code = Code("class Dog { void Dog(string n) {} } Dog d = new Dog(\"rex\");");
        Assert.True(Has(code, OpCode.CallMethod));
        Assert.Equal("Dog", First(code, OpCode.CallMethod).Name);
    }

    [Fact]
    public void FieldAccess_EmitsLoadField()
    {
        var code = Code("class Dog { string name; } Dog dog = new Dog(); dog.name;");
        Assert.True(Has(code, OpCode.LoadField));
        Assert.Equal("name", First(code, OpCode.LoadField).Name);
    }

    [Fact]
    public void FieldAssign_EmitsStoreField()
    {
        var code = Code("class Dog { string name; } Dog dog = new Dog(); dog.name = \"rex\";");
        Assert.True(Has(code, OpCode.StoreField));
        Assert.Equal("name", First(code, OpCode.StoreField).Name);
    }

    [Fact]
    public void FieldAssign_DupBeforeStoreField()
    {
        var code = Code("class Dog { string name; } Dog dog = new Dog(); dog.name = \"rex\";");
        var ops  = code.Select(i => i.OpCode).ToList();
        int dupIdx   = ops.LastIndexOf(OpCode.Dup);
        int storeIdx = ops.IndexOf(OpCode.StoreField);
        Assert.True(dupIdx >= 0, "Expected Dup");
        Assert.True(dupIdx < storeIdx, "Dup must come before StoreField");
    }

    [Fact]
    public void MethodCall_EmitsCallMethod()
    {
        var code = Code("class Dog { void speak() {} } Dog dog = new Dog(); dog.speak();");
        Assert.True(Has(code, OpCode.CallMethod));
        var cm = All(code, OpCode.CallMethod).Last();
        Assert.Equal("speak", cm.Name);
    }

    [Fact]
    public void MethodCall_ArgCount_InOperand()
    {
        var code = Code("class Dog { void move(int x, int y) {} } Dog dog = new Dog(); dog.move(1, 2);");
        var cm   = All(code, OpCode.CallMethod).Last();
        Assert.Equal(2, (int)cm.Operand!);
    }

    [Fact]
    public void ClassFieldInitializer_EmitsDupAndStoreField()
    {
        var code = Code("class Counter { int count = 0; } Counter c = new Counter();");
        Assert.True(Has(code, OpCode.Dup));
        Assert.True(Has(code, OpCode.StoreField));
    }

    [Fact]
    public void SuperCall_EmitsCallMethodWithBaseExtra()
    {
        var code = Code("""
            class Animal { void speak() {} }
            class Dog : Animal { override void speak() { super.speak(); } }
        """);
        var superCall = code.FirstOrDefault(i =>
            i.OpCode == OpCode.CallMethod && i.Extra is string);
        Assert.NotNull(superCall);
        Assert.Equal("Animal", superCall.Extra);
    }

    [Fact]
    public void PushThis_EmittedInMethod()
    {
        var code = Code("class Foo { void bar() {} void baz() { bar(); } }");
        Assert.True(Has(code, OpCode.PushThis));
    }

    [Fact]
    public void StructDecl_NoCode_JustRegistered()
    {
        AssertNoErrors(Compile("struct Vec2 { float x; float y; }"));
    }

    [Fact]
    public void StructVar_NoInit_EmitsCreateStruct()
    {
        var code = Code("struct Vec2 { float x; float y; } Vec2 v;");
        Assert.True(Has(code, OpCode.CreateStruct));
        Assert.Equal("Vec2", First(code, OpCode.CreateStruct).Name);
    }

    [Fact]
    public void EnumMemberAccess_EmitsPushInt()
    {
        var code = Code("enum Color { Red, Green, Blue } Color c = Color.Red;");
        Assert.Contains(All(code, OpCode.Push),
            p => p.Operand is Value v && v.Type == SabakaType.Int && v.Int == 0);
    }

    [Fact]
    public void EnumMemberAccess_SecondMember_PushesOne()
    {
        var code = Code("enum Color { Red, Green, Blue } int g = Color.Green;");
        Assert.Contains(All(code, OpCode.Push),
            p => p.Operand is Value v && v.Type == SabakaType.Int && v.Int == 1);
    }
    
    [Fact]
    public void ExternalFunc_EmitsCallExternal()
    {
        var lex   = new Lexer("myExt(1, 2);").Tokenize();
        var parse = new Parser(lex).Parse();
        var bind  = new Binder().Bind(parse.Statements);
        var comp  = new Compiler();
        comp.RegisterExternal("myExt", 2);
        var result = comp.Compile(parse.Statements, bind);

        Assert.True(Has(result.Code, OpCode.CallExternal));
        Assert.Equal("myExt", First(result.Code, OpCode.CallExternal).Name);
    }
    
    [Fact]
    public void IfBody_WrappedInEnterExitScope()
    {
        var code = Code("if (true) { int x = 1; }");
        var ops  = code.Select(i => i.OpCode).ToList();
        int enter = ops.IndexOf(OpCode.EnterScope);
        int exit  = ops.IndexOf(OpCode.ExitScope);
        Assert.True(enter >= 0 && exit > enter);
    }

    [Fact]
    public void ForLoop_WrappedInEnterExitScope()
    {
        var code = Code("for (int i = 0; i < 3; i = i + 1) {}");
        Assert.True(Has(code, OpCode.EnterScope));
        Assert.True(Has(code, OpCode.ExitScope));
    }

    [Fact]
    public void Super_OutsideClass_ProducesError()
    {
        var r = CompileUnchecked("void foo() { super.bar(); }");
        Assert.True(r.HasErrors);
    }

    [Fact]
    public void UnknownEnumMember_ProducesError()
    {
        var r = Compile("enum Color { Red } int x = Color.Purple;");
        Assert.True(r.HasErrors);
        Assert.Contains(r.Errors, e => e.Message.Contains("Purple"));
    }

    [Fact]
    public void UndeclaredVariable_ProducesBindError()
    {
        var lex   = new Lexer("x + 1;").Tokenize();
        var parse = new Parser(lex).Parse();
        var bind  = new Binder().Bind(parse.Statements);
        Assert.True(bind.HasErrors);
        Assert.Contains(bind.Errors, e => e.Message.Contains("'x'"));
    }

    [Fact]
    public void DuplicateDeclaration_ProducesBindError()
    {
        var lex   = new Lexer("int x = 1; int x = 2;").Tokenize();
        var parse = new Parser(lex).Parse();
        var bind  = new Binder().Bind(parse.Statements);
        Assert.True(bind.HasErrors);
        Assert.Contains(bind.Errors, e => e.Message.Contains("x") && e.Message.Contains("already declared"));
    }

    [Fact]
    public void Complex_FibFunction_CompilesClean()
    {
        var r = Compile("int fib(int n) { if (n <= 1) return n; return fib(n-1) + fib(n-2); }");
        AssertNoErrors(r);
        Assert.True(Has(r.Code, OpCode.Function));
    }

    [Fact]
    public void Complex_ClassHierarchy_CompilesClean()
    {
        const string src = """
            interface IAnimal { void speak(); }
            class Animal : IAnimal {
                string name;
                void speak() { print(name); }
            }
            class Dog : Animal {
                override void speak() { super.speak(); }
            }
        """;
        AssertNoErrors(Compile(src));
    }

    [Fact]
    public void Complex_GenericStyleClass_CompilesClean()
    {
        const string src = """
            class Counter {
                int value = 0;
                void Counter(int start) { value = start; }
                void increment() { value = value + 1; }
                int get() { return value; }
            }
        """;
        AssertNoErrors(Compile(src));
    }

    [Fact]
    public void Complex_NestedLoops_CompilesClean()
    {
        const string src = """
            void matrix() {
                for (int i = 0; i < 3; i = i + 1) {
                    for (int j = 0; j < 3; j = j + 1) {
                        int cell = i + j;
                    }
                }
            }
        """;
        AssertNoErrors(Compile(src));
    }

    [Fact]
    public void Complex_SwitchWithDefault_CompilesClean()
    {
        const string src = """
            void classify(int n) {
                switch (n) {
                    case 0: print("zero");
                    case 1: print("one");
                    default: print("other");
                }
            }
        """;
        AssertNoErrors(Compile(src));
    }

    [Fact]
    public void Complex_ForeachOverArray_CompilesClean()
    {
        const string src = """
            void printAll(string[] items) {
                foreach (string s in items) {
                    print(s);
                }
            }
        """;
        AssertNoErrors(Compile(src));
    }

    [Fact]
    public void Complex_MultipleClasses_CompilesClean()
    {
        const string src = """
            class A { int x; }
            class B { int y; }
            class C : A { int z; }
            void run() {
                A a = new A();
                B b = new B();
                C c = new C();
            }
        """;
        AssertNoErrors(Compile(src));
    }

    [Fact]
    public void Complex_CompileResult_HasSymbols()
    {
        var r = Compile("""
            int counter = 0;
            void increment() { counter = counter + 1; }
            class Timer { int ticks; }
        """);
        AssertNoErrors(r);
        Assert.NotNull(r.Symbols);
        Assert.NotEmpty(r.Symbols.Lookup("counter"));
        Assert.NotEmpty(r.Symbols.Lookup("increment"));
        Assert.NotEmpty(r.Symbols.Lookup("Timer"));
    }
    
    [Fact]
    public void ReturnStmt_NullLiteral()
    {
        var code = Code("int f() { return null; }");

        Assert.Contains(code, i =>
            i.OpCode == OpCode.Push &&
            i.Operand is Value v &&
            v.IsNull);

        Assert.Contains(code, i => i.OpCode == OpCode.Return);
    }
}