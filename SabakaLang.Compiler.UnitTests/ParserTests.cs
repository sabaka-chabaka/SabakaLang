namespace SabakaLang.Compiler.UnitTests;

public class ParserTests
{
    private static ParseResult Parse(string source)
    {
        var lexer = new Lexer(source).Tokenize();
        return new Parser(lexer).Parse();
    }
 
    private static T AssertSingle<T>(ParseResult result) where T : IStmt
    {
        if (result.HasErrors)
            throw new Exception(string.Join("\n", result.Errors.Select(e => e.Message)));
        var stmt = Assert.Single(result.Statements);
        return Assert.IsType<T>(stmt);
    }

    [Fact]
    public void IntLiteral_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("42"));
        var lit = Assert.IsType<IntLit>(stmt.Expr);
        Assert.Equal(42, lit.Value);
    }
    
    [Fact]
    public void FloatLiteral_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("3.14;"));
        var lit = Assert.IsType<FloatLit>(stmt.Expr);
        Assert.Equal(3.14, lit.Value);
    }
 
    [Fact]
    public void StringLiteral_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("\"hello\";"));
        var lit = Assert.IsType<StringLit>(stmt.Expr);
        Assert.Equal("hello", lit.Value);
    }
 
    [Fact]
    public void BoolLiteral_True_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("true;"));
        var lit = Assert.IsType<BoolLit>(stmt.Expr);
        Assert.True(lit.Value);
    }
 
    [Fact]
    public void BoolLiteral_False_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("false;"));
        var lit = Assert.IsType<BoolLit>(stmt.Expr);
        Assert.False(lit.Value);
    }

    [Fact]
    public void NullLiteral_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("null;"));
        Assert.IsType<NullLit>(stmt.Expr);
    }
    
    [Fact]
    public void BinaryExpr_Addition_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("1 + 2;"));
        var bin = Assert.IsType<BinaryExpr>(stmt.Expr);
        Assert.Equal(TokenType.Plus, bin.Op);
        Assert.IsType<IntLit>(bin.Left);
        Assert.IsType<IntLit>(bin.Right);
    }
 
    [Fact]
    public void BinaryExpr_Subtraction_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("10 - 3;"));
        var bin = Assert.IsType<BinaryExpr>(stmt.Expr);
        Assert.Equal(TokenType.Minus, bin.Op);
    }
 
    [Fact]
    public void BinaryExpr_Multiplication_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("4 * 5;"));
        var bin = Assert.IsType<BinaryExpr>(stmt.Expr);
        Assert.Equal(TokenType.Star, bin.Op);
    }
 
    [Fact]
    public void BinaryExpr_Division_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("8 / 2;"));
        var bin = Assert.IsType<BinaryExpr>(stmt.Expr);
        Assert.Equal(TokenType.Slash, bin.Op);
    }
 
    [Fact]
    public void BinaryExpr_Modulo_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("7 % 3;"));
        var bin = Assert.IsType<BinaryExpr>(stmt.Expr);
        Assert.Equal(TokenType.Percent, bin.Op);
    }
 
    [Fact]
    public void BinaryExpr_EqualEqual_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("a == b;"));
        var bin = Assert.IsType<BinaryExpr>(stmt.Expr);
        Assert.Equal(TokenType.EqualEqual, bin.Op);
    }
 
    [Fact]
    public void BinaryExpr_NotEqual_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("a != b;"));
        var bin = Assert.IsType<BinaryExpr>(stmt.Expr);
        Assert.Equal(TokenType.NotEqual, bin.Op);
    }
 
    [Fact]
    public void BinaryExpr_Less_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("a < b;"));
        var bin = Assert.IsType<BinaryExpr>(stmt.Expr);
        Assert.Equal(TokenType.Less, bin.Op);
    }
 
    [Fact]
    public void BinaryExpr_Greater_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("a > b;"));
        var bin = Assert.IsType<BinaryExpr>(stmt.Expr);
        Assert.Equal(TokenType.Greater, bin.Op);
    }
 
    [Fact]
    public void BinaryExpr_LessEqual_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("a <= b;"));
        var bin = Assert.IsType<BinaryExpr>(stmt.Expr);
        Assert.Equal(TokenType.LessEqual, bin.Op);
    }
 
    [Fact]
    public void BinaryExpr_GreaterEqual_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("a >= b;"));
        var bin = Assert.IsType<BinaryExpr>(stmt.Expr);
        Assert.Equal(TokenType.GreaterEqual, bin.Op);
    }
 
    [Fact]
    public void BinaryExpr_LogicalAnd_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("a && b;"));
        var bin = Assert.IsType<BinaryExpr>(stmt.Expr);
        Assert.Equal(TokenType.AndAnd, bin.Op);
    }
 
    [Fact]
    public void BinaryExpr_LogicalOr_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("a || b;"));
        var bin = Assert.IsType<BinaryExpr>(stmt.Expr);
        Assert.Equal(TokenType.OrOr, bin.Op);
    }
 
    [Fact]
    public void BinaryExpr_Precedence_MulBeforeAdd()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("1 + 2 * 3;"));
        var add = Assert.IsType<BinaryExpr>(stmt.Expr);
        Assert.Equal(TokenType.Plus, add.Op);
        var mul = Assert.IsType<BinaryExpr>(add.Right);
        Assert.Equal(TokenType.Star, mul.Op);
    }
 
    [Fact]
    public void BinaryExpr_Precedence_ParensOverridePrecedence()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("(1 + 2) * 3;"));
        var mul = Assert.IsType<BinaryExpr>(stmt.Expr);
        Assert.Equal(TokenType.Star, mul.Op);
        var add = Assert.IsType<BinaryExpr>(mul.Left);
        Assert.Equal(TokenType.Plus, add.Op);
    }
 
    [Fact]
    public void BinaryExpr_LeftAssociativity()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("1 - 2 - 3;"));
        var outer = Assert.IsType<BinaryExpr>(stmt.Expr);
        Assert.IsType<BinaryExpr>(outer.Left);
        Assert.IsType<IntLit>(outer.Right);
    }
    
    [Fact]
    public void UnaryExpr_Negate_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("-x;"));
        var un = Assert.IsType<UnaryExpr>(stmt.Expr);
        Assert.Equal(TokenType.Minus, un.Op);
        Assert.IsType<NameExpr>(un.Operand);
    }
 
    [Fact]
    public void UnaryExpr_Bang_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("!flag;"));
        var un = Assert.IsType<UnaryExpr>(stmt.Expr);
        Assert.Equal(TokenType.Bang, un.Op);
    }
    
    [Fact]
    public void AssignExpr_SimpleVar_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("x = 5;"));
        var assign = Assert.IsType<AssignExpr>(stmt.Expr);
        var target = Assert.IsType<NameExpr>(assign.Target);
        Assert.Equal("x", target.Name);
        Assert.IsType<IntLit>(assign.Value);
    }
 
    [Fact]
    public void AssignExpr_Member_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("obj.field = 10;"));
        var assign = Assert.IsType<AssignExpr>(stmt.Expr);
        Assert.IsType<MemberExpr>(assign.Target);
    }
 
    [Fact]
    public void AssignExpr_Index_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("arr[0] = 99;"));
        var assign = Assert.IsType<AssignExpr>(stmt.Expr);
        Assert.IsType<IndexExpr>(assign.Target);
    }
    
    [Fact]
    public void AssignExpr_PlusEqual_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("a += 5;"));
        var assign = Assert.IsType<AssignExpr>(stmt.Expr);

        var target = Assert.IsType<NameExpr>(assign.Target);
        Assert.Equal("a", target.Name);

        var bin = Assert.IsType<BinaryExpr>(assign.Value);
        Assert.Equal(TokenType.Plus, bin.Op);

        Assert.IsType<NameExpr>(bin.Left);
        var right = Assert.IsType<IntLit>(bin.Right);
        Assert.Equal(5, right.Value);
    }

    [Fact]
    public void AssignExpr_MinusEqual_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("a -= 3;"));
        var assign = Assert.IsType<AssignExpr>(stmt.Expr);

        var bin = Assert.IsType<BinaryExpr>(assign.Value);
        Assert.Equal(TokenType.Minus, bin.Op);
    }

    [Fact]
    public void AssignExpr_StarEqual_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("a *= 2;"));
        var assign = Assert.IsType<AssignExpr>(stmt.Expr);

        var bin = Assert.IsType<BinaryExpr>(assign.Value);
        Assert.Equal(TokenType.Star, bin.Op);
    }
    
    [Fact]
    public void PostfixIncrement_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("a++;"));
        var assign = Assert.IsType<AssignExpr>(stmt.Expr);

        var target = Assert.IsType<NameExpr>(assign.Target);
        Assert.Equal("a", target.Name);

        var bin = Assert.IsType<BinaryExpr>(assign.Value);
        Assert.Equal(TokenType.Plus, bin.Op);

        var right = Assert.IsType<IntLit>(bin.Right);
        Assert.Equal(1, right.Value);
    }

    [Fact]
    public void PostfixDecrement_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("a--;"));
        var assign = Assert.IsType<AssignExpr>(stmt.Expr);

        var bin = Assert.IsType<BinaryExpr>(assign.Value);
        Assert.Equal(TokenType.Minus, bin.Op);
    }
    
    [Fact]
    public void VarDecl_Int_NoInit()
    {
        var decl = AssertSingle<VarDecl>(Parse("int x;"));
        Assert.Equal("int", decl.Type.Name);
        Assert.Equal("x", decl.Name);
        Assert.Null(decl.Init);
    }
 
    [Fact]
    public void VarDecl_Float_WithInit()
    {
        var decl = AssertSingle<VarDecl>(Parse("float pi = 3.14;"));
        Assert.Equal("float", decl.Type.Name);
        Assert.Equal("pi", decl.Name);
        var lit = Assert.IsType<FloatLit>(decl.Init);
        Assert.Equal(3.14, lit.Value);
    }
 
    [Fact]
    public void VarDecl_Bool_WithInit()
    {
        var decl = AssertSingle<VarDecl>(Parse("bool flag = true;"));
        Assert.Equal("bool", decl.Type.Name);
        Assert.IsType<BoolLit>(decl.Init);
    }
 
    [Fact]
    public void VarDecl_String_WithInit()
    {
        var decl = AssertSingle<VarDecl>(Parse("string name = \"Alice\";"));
        Assert.Equal("string", decl.Type.Name);
        var lit = Assert.IsType<StringLit>(decl.Init);
        Assert.Equal("Alice", lit.Value);
    }
 
    [Fact]
    public void VarDecl_ArrayType_Parsed()
    {
        var decl = AssertSingle<VarDecl>(Parse("int[] nums;"));
        Assert.Equal("int", decl.Type.Name);
        Assert.True(decl.Type.IsArray);
    }
 
    [Fact]
    public void VarDecl_CustomType_Parsed()
    {
        var decl = AssertSingle<VarDecl>(Parse("MyClass obj;"));
        Assert.Equal("MyClass", decl.Type.Name);
        Assert.Equal("obj", decl.Name);
    }
 
    [Fact]
    public void VarDecl_GenericType_Parsed()
    {
        var decl = AssertSingle<VarDecl>(Parse("List<int> items;"));
        Assert.Equal("List", decl.Type.Name);
        Assert.Contains("int", decl.Type.TypeArgs);
    }
 
    [Fact]
    public void VarDecl_AccessMod_Private()
    {
        var decl = AssertSingle<VarDecl>(Parse("private int x;"));
        Assert.Equal(AccessMod.Private, decl.Access);
    }
 
    [Fact]
    public void VarDecl_AccessMod_Public()
    {
        var decl = AssertSingle<VarDecl>(Parse("public int x;"));
        Assert.Equal(AccessMod.Public, decl.Access);
    }
 
    [Fact]
    public void VarDecl_AccessMod_Protected()
    {
        var decl = AssertSingle<VarDecl>(Parse("protected int x;"));
        Assert.Equal(AccessMod.Protected, decl.Access);
    }
    
    [Fact]
    public void FuncDecl_Return_Null()
    {
        var func = AssertSingle<FuncDecl>(Parse("void f() { return null; }"));

        var ret = Assert.IsType<ReturnStmt>(func.Body[0]);
        Assert.IsType<NullLit>(ret.Value);
    }
    
    [Fact]
    public void FuncDecl_VoidNoParams_Parsed()
    {
        var fn = AssertSingle<FuncDecl>(Parse("void foo() {}"));
        Assert.Equal("void", fn.ReturnType.Name);
        Assert.Equal("foo", fn.Name);
        Assert.Empty(fn.Params);
        Assert.Empty(fn.Body);
    }
 
    [Fact]
    public void FuncDecl_WithReturnType_Parsed()
    {
        var fn = AssertSingle<FuncDecl>(Parse("int add(int a, int b) { return a + b; }"));
        Assert.Equal("int", fn.ReturnType.Name);
        Assert.Equal("add", fn.Name);
        Assert.Equal(2, fn.Params.Count);
        Assert.Single(fn.Body);
    }
 
    [Fact]
    public void FuncDecl_Params_NamesAndTypes()
    {
        var fn = AssertSingle<FuncDecl>(Parse("void greet(string name, int times) {}"));
        Assert.Equal("name", fn.Params[0].Name);
        Assert.Equal("string", fn.Params[0].Type.Name);
        Assert.Equal("times", fn.Params[1].Name);
        Assert.Equal("int", fn.Params[1].Type.Name);
    }
 
    [Fact]
    public void FuncDecl_WithBody_Parsed()
    {
        var fn = AssertSingle<FuncDecl>(Parse("int square(int n) { return n * n; }"));
        var ret = Assert.IsType<ReturnStmt>(fn.Body[0]);
        Assert.IsType<BinaryExpr>(ret.Value);
    }
 
    [Fact]
    public void FuncDecl_PrivateAccess_Parsed()
    {
        var fn = AssertSingle<FuncDecl>(Parse("private void helper() {}"));
        Assert.Equal(AccessMod.Private, fn.Access);
    }
 
    [Fact]
    public void FuncDecl_Override_Parsed()
    {
        var fn = AssertSingle<FuncDecl>(Parse("override void render() {}"));
        Assert.True(fn.IsOverride);
    }
 
    [Fact]
    public void FuncDecl_PublicOverride_Parsed()
    {
        var fn = AssertSingle<FuncDecl>(Parse("public override string toString() { return \"x\"; }"));
        Assert.Equal(AccessMod.Public, fn.Access);
        Assert.True(fn.IsOverride);
    }
 
    [Fact]
    public void FuncDecl_GenericTypeParam_Parsed()
    {
        var fn = AssertSingle<FuncDecl>(Parse("void swap<T>(T a, T b) {}"));
        Assert.Equal("swap", fn.Name);
        Assert.Single(fn.TypeParams);
        Assert.Equal("T", fn.TypeParams[0].Name);
    }
 
    [Fact]
    public void FuncDecl_ArrayReturnType_Parsed()
    {
        var fn = AssertSingle<FuncDecl>(Parse("int[] getNumbers() {}"));
        Assert.Equal("int", fn.ReturnType.Name);
        Assert.True(fn.ReturnType.IsArray);
    }
 
    [Fact]
    public void FuncDecl_SingleParam_Parsed()
    {
        var fn = AssertSingle<FuncDecl>(Parse("void print(string msg) {}"));
        Assert.Single(fn.Params);
        Assert.Equal("msg", fn.Params[0].Name);
    }
    
    [Fact]
    public void CallExpr_NoArgs_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("foo();"));
        var call = Assert.IsType<CallExpr>(stmt.Expr);
        var callee = Assert.IsType<NameExpr>(call.Callee);
        Assert.Equal("foo", callee.Name);
        Assert.Empty(call.Args);
    }
 
    [Fact]
    public void CallExpr_WithArgs_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("add(1, 2);"));
        var call = Assert.IsType<CallExpr>(stmt.Expr);
        Assert.Equal(2, call.Args.Count);
    }
 
    [Fact]
    public void CallExpr_ChainedMemberCall_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("obj.method();"));
        var call = Assert.IsType<CallExpr>(stmt.Expr);
        Assert.IsType<MemberExpr>(call.Callee);
    }
 
    [Fact]
    public void CallExpr_NestedCall_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("foo(bar(1));"));
        var outer = Assert.IsType<CallExpr>(stmt.Expr);
        var inner = Assert.IsType<CallExpr>(outer.Args[0]);
        Assert.IsType<NameExpr>(inner.Callee);
    }
 
    [Fact]
    public void CallExpr_MultipleChained_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("a.b().c();"));
        var outerCall = Assert.IsType<CallExpr>(stmt.Expr);
        Assert.IsType<MemberExpr>(outerCall.Callee);
    }
    
    [Fact]
    public void MemberExpr_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("obj.field;"));
        var mem = Assert.IsType<MemberExpr>(stmt.Expr);
        Assert.Equal("field", mem.Member);
        Assert.IsType<NameExpr>(mem.Object);
    }
 
    [Fact]
    public void MemberExpr_Chained_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("a.b.c;"));
        var outer = Assert.IsType<MemberExpr>(stmt.Expr);
        Assert.Equal("c", outer.Member);
        var inner = Assert.IsType<MemberExpr>(outer.Object);
        Assert.Equal("b", inner.Member);
    }
 
    [Fact]
    public void IndexExpr_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("arr[0];"));
        var idx = Assert.IsType<IndexExpr>(stmt.Expr);
        Assert.IsType<NameExpr>(idx.Object);
        Assert.IsType<IntLit>(idx.Index);
    }
 
    [Fact]
    public void IndexExpr_ExprIndex_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("arr[i + 1];"));
        var idx = Assert.IsType<IndexExpr>(stmt.Expr);
        Assert.IsType<BinaryExpr>(idx.Index);
    }
    
    [Fact]
    public void ArrayExpr_Empty_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("[];"));
        var arr = Assert.IsType<ArrayExpr>(stmt.Expr);
        Assert.Empty(arr.Elements);
    }
 
    [Fact]
    public void ArrayExpr_WithElements_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("[1, 2, 3];"));
        var arr = Assert.IsType<ArrayExpr>(stmt.Expr);
        Assert.Equal(3, arr.Elements.Count);
    }
 
    [Fact]
    public void ArrayExpr_OfStrings_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("[\"a\", \"b\"];"));
        var arr = Assert.IsType<ArrayExpr>(stmt.Expr);
        Assert.Equal(2, arr.Elements.Count);
        Assert.All(arr.Elements, e => Assert.IsType<StringLit>(e));
    }
    
    [Fact]
    public void NewExpr_NoArgs_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("new Foo();"));
        var ne = Assert.IsType<NewExpr>(stmt.Expr);
        Assert.Equal("Foo", ne.TypeName);
        Assert.Empty(ne.Args);
    }
 
    [Fact]
    public void NewExpr_WithArgs_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("new Vec2(1, 2);"));
        var ne = Assert.IsType<NewExpr>(stmt.Expr);
        Assert.Equal("Vec2", ne.TypeName);
        Assert.Equal(2, ne.Args.Count);
    }
 
    [Fact]
    public void NewExpr_WithTypeArgs_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("new List<int>();"));
        var ne = Assert.IsType<NewExpr>(stmt.Expr);
        Assert.Equal("List", ne.TypeName);
        Assert.Contains("int", ne.TypeArgs);
    }
    
    [Fact]
    public void IfStmt_NoElse_Parsed()
    {
        var stmt = AssertSingle<IfStmt>(Parse("if (x > 0) { return x; }"));
        Assert.IsType<BinaryExpr>(stmt.Condition);
        Assert.Single(stmt.Then);
        Assert.Null(stmt.Else);
    }
 
    [Fact]
    public void IfStmt_WithElse_Parsed()
    {
        var stmt = AssertSingle<IfStmt>(Parse("if (x) { } else { }"));
        Assert.NotNull(stmt.Else);
    }
 
    [Fact]
    public void IfStmt_ElseIf_Parsed()
    {
        var stmt = AssertSingle<IfStmt>(Parse("if (a) { } else if (b) { }"));
        var elseStmt = Assert.Single(stmt.Else!);
        Assert.IsType<IfStmt>(elseStmt);
    }
 
    [Fact]
    public void IfStmt_SingleStmtBranch_Parsed()
    {
        var stmt = AssertSingle<IfStmt>(Parse("if (x) return 1;"));
        Assert.Single(stmt.Then);
    }
    
    [Fact]
    public void WhileStmt_Parsed()
    {
        var stmt = AssertSingle<WhileStmt>(Parse("while (x > 0) { x = x - 1; }"));
        Assert.IsType<BinaryExpr>(stmt.Condition);
        Assert.Single(stmt.Body);
    }
 
    [Fact]
    public void WhileStmt_TrueCondition_Parsed()
    {
        var stmt = AssertSingle<WhileStmt>(Parse("while (true) {}"));
        var cond = Assert.IsType<BoolLit>(stmt.Condition);
        Assert.True(cond.Value);
        Assert.Empty(stmt.Body);
    }
    
    [Fact]
    public void ForStmt_Full_Parsed()
    {
        var stmt = AssertSingle<ForStmt>(Parse("for (int i = 0; i < 10; i = i + 1) {}"));
        Assert.IsType<VarDecl>(stmt.Init);
        Assert.IsType<BinaryExpr>(stmt.Condition);
        Assert.IsType<AssignExpr>(stmt.Step);
    }
 
    [Fact]
    public void ForStmt_EmptyParts_Parsed()
    {
        var stmt = AssertSingle<ForStmt>(Parse("for (;;) {}"));
        Assert.Null(stmt.Init);
        Assert.Null(stmt.Condition);
        Assert.Null(stmt.Step);
    }
 
    [Fact]
    public void ForStmt_NoInit_Parsed()
    {
        var stmt = AssertSingle<ForStmt>(Parse("for (; i < 10; i = i + 1) {}"));
        Assert.Null(stmt.Init);
        Assert.NotNull(stmt.Condition);
    }
    
    [Fact]
    public void ForeachStmt_Parsed()
    {
        var stmt = AssertSingle<ForeachStmt>(Parse("foreach (int x in numbers) {}"));
        Assert.Equal("int", stmt.ItemType.Name);
        Assert.Equal("x", stmt.ItemName);
        Assert.IsType<NameExpr>(stmt.Collection);
    }
 
    [Fact]
    public void ForeachStmt_CustomType_Parsed()
    {
        var stmt = AssertSingle<ForeachStmt>(Parse("foreach (MyObj item in list) {}"));
        Assert.Equal("MyObj", stmt.ItemType.Name);
        Assert.Equal("item", stmt.ItemName);
    }
    
    [Fact]
    public void ReturnStmt_WithValue_Parsed()
    {
        var stmt = AssertSingle<ReturnStmt>(Parse("return 42;"));
        var lit = Assert.IsType<IntLit>(stmt.Value);
        Assert.Equal(42, lit.Value);
    }
 
    [Fact]
    public void ReturnStmt_NoValue_Parsed()
    {
        var stmt = AssertSingle<ReturnStmt>(Parse("return;"));
        Assert.Null(stmt.Value);
    }
 
    [Fact]
    public void ReturnStmt_Expression_Parsed()
    {
        var stmt = AssertSingle<ReturnStmt>(Parse("return a + b;"));
        Assert.IsType<BinaryExpr>(stmt.Value);
    }
    
    [Fact]
    public void SwitchStmt_NoCases_Parsed()
    {
        var stmt = AssertSingle<SwitchStmt>(Parse("switch (x) {}"));
        Assert.Empty(stmt.Cases);
    }
 
    [Fact]
    public void SwitchStmt_WithCases_Parsed()
    {
        const string src = "switch (x) { case 1: return 1; case 2: return 2; }";
        var stmt = AssertSingle<SwitchStmt>(Parse(src));
        Assert.Equal(2, stmt.Cases.Count);
        Assert.NotNull(stmt.Cases[0].Value);
        Assert.NotNull(stmt.Cases[1].Value);
    }
 
    [Fact]
    public void SwitchStmt_WithDefault_Parsed()
    {
        const string src = "switch (x) { case 1: return 1; default: return 0; }";
        var stmt = AssertSingle<SwitchStmt>(Parse(src));
        Assert.Equal(2, stmt.Cases.Count);
        Assert.Null(stmt.Cases[1].Value); 
    }
    
    [Fact]
    public void ClassDecl_Empty_Parsed()
    {
        var cls = AssertSingle<ClassDecl>(Parse("class Foo {}"));
        Assert.Equal("Foo", cls.Name);
        Assert.Empty(cls.Fields);
        Assert.Empty(cls.Methods);
    }
 
    [Fact]
    public void ClassDecl_WithFields_Parsed()
    {
        var cls = AssertSingle<ClassDecl>(Parse("class Point { int x; int y; }"));
        Assert.Equal(2, cls.Fields.Count);
        Assert.Equal("x", cls.Fields[0].Name);
        Assert.Equal("y", cls.Fields[1].Name);
    }
 
    [Fact]
    public void ClassDecl_WithMethods_Parsed()
    {
        var cls = AssertSingle<ClassDecl>(Parse("class Foo { void bar() {} int baz() { return 1; } }"));
        Assert.Equal(2, cls.Methods.Count);
        Assert.Equal("bar", cls.Methods[0].Name);
        Assert.Equal("baz", cls.Methods[1].Name);
    }
 
    [Fact]
    public void ClassDecl_WithBase_Parsed()
    {
        var cls = AssertSingle<ClassDecl>(Parse("class Dog : Animal {}"));
        Assert.Equal("Dog", cls.Name);
        Assert.Equal("Animal", cls.Base);
    }
 
    [Fact]
    public void ClassDecl_WithInterfaces_Parsed()
    {
        var cls = AssertSingle<ClassDecl>(Parse("class Cat : Animal, IFluffy {}"));
        Assert.Equal("Animal", cls.Base);
        Assert.Contains("IFluffy", cls.Interfaces);
    }
 
    [Fact]
    public void ClassDecl_GenericTypeParam_Parsed()
    {
        var cls = AssertSingle<ClassDecl>(Parse("class Box<T> {}"));
        Assert.Equal("Box", cls.Name);
        Assert.Single(cls.TypeParams);
        Assert.Equal("T", cls.TypeParams[0].Name);
    }
 
    [Fact]
    public void ClassDecl_PrivateField_Parsed()
    {
        var cls = AssertSingle<ClassDecl>(Parse("class Foo { private int secret; }"));
        Assert.Equal(AccessMod.Private, cls.Fields[0].Access);
    }
 
    [Fact]
    public void ClassDecl_PublicMethod_Parsed()
    {
        var cls = AssertSingle<ClassDecl>(Parse("class Foo { public void doThing() {} }"));
        Assert.Equal(AccessMod.Public, cls.Methods[0].Access);
    }
 
    [Fact]
    public void ClassDecl_OverrideMethod_Parsed()
    {
        var cls = AssertSingle<ClassDecl>(Parse("class Dog : Animal { override void speak() {} }"));
        Assert.True(cls.Methods[0].IsOverride);
    }
    
    [Fact]
    public void InterfaceDecl_Empty_Parsed()
    {
        var iface = AssertSingle<InterfaceDecl>(Parse("interface IFoo {}"));
        Assert.Equal("IFoo", iface.Name);
        Assert.Empty(iface.Methods);
    }
 
    [Fact]
    public void InterfaceDecl_WithMethods_Parsed()
    {
        var iface = AssertSingle<InterfaceDecl>(Parse("interface IShape { float area(); float perimeter(); }"));
        Assert.Equal(2, iface.Methods.Count);
        Assert.Equal("area", iface.Methods[0].Name);
    }
 
    [Fact]
    public void InterfaceDecl_Extends_Parsed()
    {
        var iface = AssertSingle<InterfaceDecl>(Parse("interface IFlyingAnimal : IAnimal {}"));
        Assert.Contains("IAnimal", iface.Parents);
    }
 
    [Fact]
    public void InterfaceDecl_GenericTypeParam_Parsed()
    {
        var iface = AssertSingle<InterfaceDecl>(Parse("interface IContainer<T> {}"));
        Assert.Single(iface.TypeParams);
        Assert.Equal("T", iface.TypeParams[0].Name);
    }
    
    [Fact]
    public void StructDecl_Empty_Parsed()
    {
        var st = AssertSingle<StructDecl>(Parse("struct Empty {}"));
        Assert.Equal("Empty", st.Name);
        Assert.Empty(st.Fields);
    }
 
    [Fact]
    public void StructDecl_WithFields_Parsed()
    {
        var st = AssertSingle<StructDecl>(Parse("struct Vec2 { float x; float y; }"));
        Assert.Equal(2, st.Fields.Count);
        Assert.Equal("x", st.Fields[0].Name);
        Assert.Equal("y", st.Fields[1].Name);
    }
    
    [Fact]
    public void EnumDecl_Empty_Parsed()
    {
        var en = AssertSingle<EnumDecl>(Parse("enum Empty {}"));
        Assert.Equal("Empty", en.Name);
        Assert.Empty(en.Members);
    }
 
    [Fact]
    public void EnumDecl_WithMembers_Parsed()
    {
        var en = AssertSingle<EnumDecl>(Parse("enum Color { Red, Green, Blue }"));
        Assert.Equal(3, en.Members.Count);
        Assert.Equal("Red", en.Members[0]);
        Assert.Equal("Green", en.Members[1]);
        Assert.Equal("Blue", en.Members[2]);
    }
 
    [Fact]
    public void EnumDecl_SingleMember_Parsed()
    {
        var en = AssertSingle<EnumDecl>(Parse("enum Singleton { Only }"));
        Assert.Single(en.Members);
        Assert.Equal("Only", en.Members[0]);
    }
    
    [Fact]
    public void ImportStmt_Simple_Parsed()
    {
        var stmt = AssertSingle<ImportStmt>(Parse("import \"math\";"));
        Assert.Equal("math", stmt.Path);
        Assert.Empty(stmt.Names);
        Assert.Null(stmt.Alias);
    }
 
    [Fact]
    public void ImportStmt_WithAlias_Parsed()
    {
        var stmt = AssertSingle<ImportStmt>(Parse("import \"math\" as m;"));
        Assert.Equal("math", stmt.Path);
        Assert.Equal("m", stmt.Alias);
    }
 
    [Fact]
    public void ImportStmt_FromNames_Parsed()
    {
        var stmt = AssertSingle<ImportStmt>(Parse("import \"lib\" from foo, bar;"));
        Assert.Equal(2, stmt.Names.Count);
        Assert.Contains("foo", stmt.Names);
        Assert.Contains("bar", stmt.Names);
    }
    
    [Fact]
    public void SuperExpr_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("super;"));
        Assert.IsType<SuperExpr>(stmt.Expr);
    }
 
    [Fact]
    public void SuperExpr_MethodCall_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("super.speak();"));
        var call = Assert.IsType<CallExpr>(stmt.Expr);
        var mem = Assert.IsType<MemberExpr>(call.Callee);
        Assert.IsType<SuperExpr>(mem.Object);
        Assert.Equal("speak", mem.Member);
    }
    
    [Fact]
    public void MultipleStatements_Parsed()
    {
        var result = Parse("int x; int y; int z;");
        Assert.False(result.HasErrors, string.Join("\n", result.Errors.Select(e => e.Message)));
        Assert.Equal(3, result.Statements.Count);
        Assert.All(result.Statements, s => Assert.IsType<VarDecl>(s));
    }
 
    [Fact]
    public void FuncAndClass_TopLevel_Parsed()
    {
        var result = Parse("void helper() {} class Main {}");
        Assert.False(result.HasErrors, string.Join("\n", result.Errors.Select(e => e.Message)));
        Assert.Equal(2, result.Statements.Count);
        Assert.IsType<FuncDecl>(result.Statements[0]);
        Assert.IsType<ClassDecl>(result.Statements[1]);
    }
    
    [Fact]
    public void InvalidExpr_MissingRhs_ProducesError()
    {
        var result = Parse("int x = ;");
        Assert.True(result.HasErrors);
    }
 
    [Fact]
    public void MissingClosingParen_ProducesError()
    {
        var result = Parse("if (x {}");
        Assert.True(result.HasErrors);
    }
    
    [Fact]
    public void Complex_FibFunction_Parsed()
    {
        const string src = "int fib(int n) { if (n <= 1) return n; return fib(n - 1) + fib(n - 2); }";
        var fn = AssertSingle<FuncDecl>(Parse(src));
        Assert.Equal("fib", fn.Name);
        Assert.Equal(2, fn.Body.Count);
    }
 
    [Fact]
    public void Complex_ClassWithMethodsAndFields_Parsed()
    {
        const string src = "class Counter { private int count = 0; public void increment() { count = count + 1; } public int get() { return count; } }";
        var cls = AssertSingle<ClassDecl>(Parse(src));
        Assert.Single(cls.Fields);
        Assert.Equal(2, cls.Methods.Count);
    }
 
    [Fact]
    public void Complex_NestedLoops_Parsed()
    {
        const string src = "void matrix() { for (int i = 0; i < 10; i = i + 1) { for (int j = 0; j < 10; j = j + 1) {} } }";
        var fn = AssertSingle<FuncDecl>(Parse(src));
        var outer = Assert.IsType<ForStmt>(fn.Body[0]);
        Assert.Single(outer.Body);
        Assert.IsType<ForStmt>(outer.Body[0]);
    }
 
    [Fact]
    public void Complex_ChainedCallsAndMembers_Parsed()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("a.b(1).c[2].d();"));
        Assert.IsType<CallExpr>(stmt.Expr);
    }
 
    [Fact]
    public void Complex_ForeachWithBody_Parsed()
    {
        const string src = "void printAll(string[] items) { foreach (string s in items) { print(s); } }";
        var fn = AssertSingle<FuncDecl>(Parse(src));
        Assert.Single(fn.Body);
        Assert.IsType<ForeachStmt>(fn.Body[0]);
    }
 
    [Fact]
    public void Complex_ClassInheritsAndImplements_WithOverride_Parsed()
    {
        const string src = "class Poodle : Dog, IFluffy { override void speak() { bark(); } }";
        var cls = AssertSingle<ClassDecl>(Parse(src));
        Assert.Equal("Dog", cls.Base);
        Assert.Contains("IFluffy", cls.Interfaces);
        Assert.Single(cls.Methods);
        Assert.True(cls.Methods[0].IsOverride);
    }
 
    [Fact]
    public void Complex_GenericFunction_Parsed()
    {
        const string src = "T identity<T>(T val) { return val; }";
        var fn = AssertSingle<FuncDecl>(Parse(src));
        Assert.Equal("identity", fn.Name);
        Assert.Single(fn.TypeParams);
        Assert.Single(fn.Params);
    }
 
    [Fact]
    public void Complex_MultipleClasses_Parsed()
    {
        const string src = "class A {} class B {} class C {}";
        var result = Parse(src);
        Assert.False(result.HasErrors, string.Join("\n", result.Errors.Select(e => e.Message)));
        Assert.Equal(3, result.Statements.Count);
        Assert.All(result.Statements, s => Assert.IsType<ClassDecl>(s));
    }
    
    [Fact]
    public void Compare_With_Null()
    {
        var stmt = AssertSingle<ExprStmt>(Parse("a == null;"));
        var bin = Assert.IsType<BinaryExpr>(stmt.Expr);
        Assert.IsType<NullLit>(bin.Right);
    }

    [Fact]
    public void TernaryExpr_Parsed()
    {
        var result = Parse("a ? b : c");

        Assert.False(result.HasErrors);
        var expr = Assert.IsType<ExprStmt>(result.Statements[0]).Expr;

        var ternary = Assert.IsType<TernaryExpr>(expr);

        Assert.IsType<NameExpr>(ternary.Condition);
        Assert.IsType<NameExpr>(ternary.Then);
        Assert.IsType<NameExpr>(ternary.Else);
    }
    
    [Fact]
    public void Parse_Ternary_Precedence()
    {
        var result = Parse("a || b ? c : d");

        Assert.False(result.HasErrors);

        var expr = Assert.IsType<ExprStmt>(result.Statements[0]).Expr;
        var ternary = Assert.IsType<TernaryExpr>(expr);

        var condition = Assert.IsType<BinaryExpr>(ternary.Condition);
        Assert.Equal(TokenType.OrOr, condition.Op);
    }
    
    [Fact]
    public void Parse_Ternary_RightAssociative()
    {
        var result = Parse("a ? b : c ? d : e");

        Assert.False(result.HasErrors);

        var expr = Assert.IsType<ExprStmt>(result.Statements[0]).Expr;
        var t1 = Assert.IsType<TernaryExpr>(expr);

        Assert.IsType<NameExpr>(t1.Condition);
        Assert.IsType<NameExpr>(t1.Then);

        var t2 = Assert.IsType<TernaryExpr>(t1.Else);
    }
    
    [Fact]
    public void InterpolatedString_NoHoles_ParsedAsSingleStringLit()
    {
        var result = Parse("$\"hello world\";");
 
        Assert.False(result.HasErrors, string.Join("\n", result.Errors.Select(e => e.Message)));
        var stmt = Assert.IsType<ExprStmt>(result.Statements[0]);
        var interp = Assert.IsType<InterpolatedStringExpr>(stmt.Expr);
        var part = Assert.Single(interp.Parts);
        Assert.Equal("hello world", Assert.IsType<StringLit>(part).Value);
    }
 
    [Fact]
    public void InterpolatedString_SingleVar_HasTwoParts()
    {
        var result = Parse("$\"name is {name}\";");
 
        Assert.False(result.HasErrors, string.Join("\n", result.Errors.Select(e => e.Message)));
        var stmt = Assert.IsType<ExprStmt>(result.Statements[0]);
        var interp = Assert.IsType<InterpolatedStringExpr>(stmt.Expr);
 
        Assert.Equal(2, interp.Parts.Count);
        Assert.Equal("name is ", Assert.IsType<StringLit>(interp.Parts[0]).Value);
        Assert.Equal("name", Assert.IsType<NameExpr>(interp.Parts[1]).Name);
    }
 
    [Fact]
    public void InterpolatedString_MultipleHoles_HasCorrectParts()
    {
        var result = Parse("$\"{a} and {b}\";");
 
        Assert.False(result.HasErrors, string.Join("\n", result.Errors.Select(e => e.Message)));
        var interp = Assert.IsType<InterpolatedStringExpr>(Assert.IsType<ExprStmt>(result.Statements[0]).Expr);
 
        Assert.Equal(3, interp.Parts.Count);
        Assert.IsType<NameExpr>(interp.Parts[0]);   // a
        Assert.Equal(" and ", Assert.IsType<StringLit>(interp.Parts[1]).Value);
        Assert.IsType<NameExpr>(interp.Parts[2]);   // b
    }
 
    [Fact]
    public void InterpolatedString_ExpressionInHole_ParsedCorrectly()
    {
        var result = Parse("$\"val: {x + 1}\";");
 
        Assert.False(result.HasErrors, string.Join("\n", result.Errors.Select(e => e.Message)));
        var interp = Assert.IsType<InterpolatedStringExpr>(Assert.IsType<ExprStmt>(result.Statements[0]).Expr);
 
        Assert.Equal(2, interp.Parts.Count);
        Assert.Equal("val: ", Assert.IsType<StringLit>(interp.Parts[0]).Value);
        var bin = Assert.IsType<BinaryExpr>(interp.Parts[1]);
        Assert.Equal(TokenType.Plus, bin.Op);
    }
 
    [Fact]
    public void InterpolatedString_OnlyHole_HasSingleExprPart()
    {
        var result = Parse("$\"{x}\";");
 
        Assert.False(result.HasErrors, string.Join("\n", result.Errors.Select(e => e.Message)));
        var interp = Assert.IsType<InterpolatedStringExpr>(Assert.IsType<ExprStmt>(result.Statements[0]).Expr);
 
        var part = Assert.Single(interp.Parts);
        Assert.Equal("x", Assert.IsType<NameExpr>(part).Name);
    }
    
    [Fact]
    public void Is_Simple()
    {
        var result = Parse("x is int;");

        Assert.False(result.HasErrors);
        Assert.Single(result.Statements);

        var exprStmt = Assert.IsType<ExprStmt>(result.Statements[0]);
        var bin = Assert.IsType<BinaryExpr>(exprStmt.Expr);

        Assert.Equal(TokenType.Is, bin.Op);
        Assert.IsType<NameExpr>(bin.Left);
        Assert.IsType<NameExpr>(bin.Right);
    }
    
    [Fact]
    public void Is_With_Identifier()
    {
        var result = Parse("x is Type");

        Assert.False(result.HasErrors);

        var expr = Assert.IsType<ExprStmt>(result.Statements[0]).Expr;
        var bin = Assert.IsType<BinaryExpr>(expr);

        Assert.Equal(TokenType.Is, bin.Op);
        Assert.Equal("x", ((NameExpr)bin.Left).Name);
        Assert.Equal("Type", ((NameExpr)bin.Right).Name);
    }
}