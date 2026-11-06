using SabakaLang.AST;
using SabakaLang.Lexer.Enums;

namespace SabakaLang.Parser.Tests;

public class OperatorAndExpressionTests
{
    [Theory]
    [InlineData("-x", TokenType.Minus)]
    [InlineData("!flag", TokenType.Bang)]
    public void Parse_UnaryExpression_ReturnsUnaryExpr(string source, TokenType expectedOp)
    {
        var expr = Utils.ParseSingleExpr<UnaryExpr>(source);
        Assert.Equal(expectedOp, expr.Op);
        Assert.IsType<NameExpr>(expr.Operand);
    }

    [Theory]
    [InlineData("a + b", TokenType.Plus)]
    [InlineData("a - b", TokenType.Minus)]
    [InlineData("a * b", TokenType.Star)]
    [InlineData("a / b", TokenType.Slash)]
    [InlineData("a % b", TokenType.Percent)]
    [InlineData("a < b", TokenType.Less)]
    [InlineData("a > b", TokenType.Greater)]
    [InlineData("a <= b", TokenType.LessEqual)]
    [InlineData("a >= b", TokenType.GreaterEqual)]
    [InlineData("a == b", TokenType.EqualEqual)]
    [InlineData("a != b", TokenType.NotEqual)]
    [InlineData("a && b", TokenType.AndAnd)]
    [InlineData("a || b", TokenType.OrOr)]
    public void Parse_BinaryExpression_ReturnsBinaryExpr(string source, TokenType expectedOp)
    {
        var expr = Utils.ParseSingleExpr<BinaryExpr>(source);
        Assert.Equal(expectedOp, expr.Op);
        Assert.Equal("a", Assert.IsType<NameExpr>(expr.Left).Name);
        Assert.Equal("b", Assert.IsType<NameExpr>(expr.Right).Name);
    }

    [Fact]
    public void Parse_IsExpression_ReturnsIsExpr()
    {
        var expr = Utils.ParseSingleExpr<IsExpr>("x is int");
        Assert.Equal("x", Assert.IsType<NameExpr>(expr.Left).Name);
        Assert.Equal("int", expr.Right.Name);
        Assert.False(expr.Right.IsArray);
        Assert.False(expr.Right.IsNullable);
    }

    [Fact]
    public void Parse_IsExpression_CustomType_ReturnsIsExpr()
    {
        var expr = Utils.ParseSingleExpr<IsExpr>("obj is MyClass");
        Assert.Equal("obj", Assert.IsType<NameExpr>(expr.Left).Name);
        Assert.Equal("MyClass", expr.Right.Name);
    }

    [Fact]
    public void Parse_NullCoalescing_ReturnsCoalesceExpr()
    {
        var expr = Utils.ParseSingleExpr<CoalesceExpr>("a ?? b");
        Assert.Equal("a", Assert.IsType<NameExpr>(expr.Left).Name);
        Assert.Equal("b", Assert.IsType<NameExpr>(expr.Right).Name);
    }

    [Fact]
    public void Parse_ChainedNullCoalescing_AssociatesCorrectly()
    {
        var expr = Utils.ParseSingleExpr<CoalesceExpr>("a ?? b ?? c");
        Assert.Equal("a", Assert.IsType<NameExpr>(expr.Left).Name);
        var right = Assert.IsType<CoalesceExpr>(expr.Right);
        Assert.Equal("b", Assert.IsType<NameExpr>(right.Left).Name);
        Assert.Equal("c", Assert.IsType<NameExpr>(right.Right).Name);
    }

    [Fact]
    public void Parse_TernaryExpression_ReturnsTernaryExpr()
    {
        var expr = Utils.ParseSingleExpr<TernaryExpr>("cond ? 1 : 2");
        Assert.Equal("cond", Assert.IsType<NameExpr>(expr.Condition).Name);
        Assert.Equal(1, Assert.IsType<IntLit>(expr.Then).Value);
        Assert.Equal(2, Assert.IsType<IntLit>(expr.Else).Value);
    }

    [Fact]
    public void Parse_TernaryExpression_NestedElse_ReturnsTernaryExpr()
    {
        var expr = Utils.ParseSingleExpr<TernaryExpr>("a ? b : c ? d : e");
        Assert.Equal("a", Assert.IsType<NameExpr>(expr.Condition).Name);
        Assert.Equal("b", Assert.IsType<NameExpr>(expr.Then).Name);
        var elseTernary = Assert.IsType<TernaryExpr>(expr.Else);
        Assert.Equal("c", Assert.IsType<NameExpr>(elseTernary.Condition).Name);
    }

    [Fact]
    public void Parse_Assignment_Simple_ReturnsAssignExpr()
    {
        var expr = Utils.ParseSingleExpr<AssignExpr>("x = 10");
        Assert.Equal("x", Assert.IsType<NameExpr>(expr.Target).Name);
        Assert.Equal(10, Assert.IsType<IntLit>(expr.Value).Value);
    }

    [Theory]
    [InlineData("x += 5", TokenType.Plus)]
    [InlineData("x -= 5", TokenType.Minus)]
    [InlineData("x *= 5", TokenType.Star)]
    public void Parse_CompoundAssignment_DesugarsToBinaryExprInsideAssign(string source, TokenType expectedOp)
    {
        var expr = Utils.ParseSingleExpr<AssignExpr>(source);
        Assert.Equal("x", Assert.IsType<NameExpr>(expr.Target).Name);
        var bin = Assert.IsType<BinaryExpr>(expr.Value);
        Assert.Equal(expectedOp, bin.Op);
        Assert.Equal("x", Assert.IsType<NameExpr>(bin.Left).Name);
        Assert.Equal(5, Assert.IsType<IntLit>(bin.Right).Value);
    }

    [Fact]
    public void Parse_PostfixIncrement_DesugarsToAssignExpr()
    {
        var expr = Utils.ParseSingleExpr<AssignExpr>("i++");
        Assert.Equal("i", Assert.IsType<NameExpr>(expr.Target).Name);
        var bin = Assert.IsType<BinaryExpr>(expr.Value);
        Assert.Equal(TokenType.Plus, bin.Op);
        Assert.Equal("i", Assert.IsType<NameExpr>(bin.Left).Name);
        Assert.Equal(1, Assert.IsType<IntLit>(bin.Right).Value);
    }

    [Fact]
    public void Parse_PostfixDecrement_DesugarsToAssignExpr()
    {
        var expr = Utils.ParseSingleExpr<AssignExpr>("i--");
        Assert.Equal("i", Assert.IsType<NameExpr>(expr.Target).Name);
        var bin = Assert.IsType<BinaryExpr>(expr.Value);
        Assert.Equal(TokenType.Minus, bin.Op);
        Assert.Equal("i", Assert.IsType<NameExpr>(bin.Left).Name);
        Assert.Equal(1, Assert.IsType<IntLit>(bin.Right).Value);
    }

    [Fact]
    public void Parse_MemberAccess_ReturnsMemberExpr()
    {
        var expr = Utils.ParseSingleExpr<MemberExpr>("user.name");
        Assert.Equal("user", Assert.IsType<NameExpr>(expr.Object).Name);
        Assert.Equal("name", expr.Member);
    }

    [Fact]
    public void Parse_ChainedMemberAccess_ReturnsNestedMemberExpr()
    {
        var expr = Utils.ParseSingleExpr<MemberExpr>("company.ceo.name");
        Assert.Equal("name", expr.Member);
        var sub = Assert.IsType<MemberExpr>(expr.Object);
        Assert.Equal("company", Assert.IsType<NameExpr>(sub.Object).Name);
        Assert.Equal("ceo", sub.Member);
    }

    [Fact]
    public void Parse_IndexExpression_ReturnsIndexExpr()
    {
        var expr = Utils.ParseSingleExpr<IndexExpr>("items[0]");
        Assert.Equal("items", Assert.IsType<NameExpr>(expr.Object).Name);
        Assert.Equal(0, Assert.IsType<IntLit>(expr.Index).Value);
    }

    [Fact]
    public void Parse_ChainedIndexExpression_ReturnsNestedIndexExpr()
    {
        var expr = Utils.ParseSingleExpr<IndexExpr>("grid[x][y]");
        Assert.Equal("y", Assert.IsType<NameExpr>(expr.Index).Name);
        var sub = Assert.IsType<IndexExpr>(expr.Object);
        Assert.Equal("grid", Assert.IsType<NameExpr>(sub.Object).Name);
        Assert.Equal("x", Assert.IsType<NameExpr>(sub.Index).Name);
    }

    [Fact]
    public void Parse_FunctionCall_NoArgs_ReturnsCallExpr()
    {
        var expr = Utils.ParseSingleExpr<CallExpr>("foo()");
        Assert.Equal("foo", Assert.IsType<NameExpr>(expr.Callee).Name);
        Assert.Empty(expr.Args);
    }

    [Fact]
    public void Parse_FunctionCall_WithArgs_ReturnsCallExpr()
    {
        var expr = Utils.ParseSingleExpr<CallExpr>("max(a, 10, \"tag\")");
        Assert.Equal("max", Assert.IsType<NameExpr>(expr.Callee).Name);
        Assert.Equal(3, expr.Args.Count);
        Assert.Equal("a", Assert.IsType<NameExpr>(expr.Args[0]).Name);
        Assert.Equal(10, Assert.IsType<IntLit>(expr.Args[1]).Value);
        Assert.Equal("tag", Assert.IsType<StringLit>(expr.Args[2]).Value);
    }

    [Fact]
    public void Parse_ChainedMethodCall_ReturnsCallExpr()
    {
        var expr = Utils.ParseSingleExpr<CallExpr>("obj.getMethod().invoke()");
        Assert.Empty(expr.Args);
        var member = Assert.IsType<MemberExpr>(expr.Callee);
        Assert.Equal("invoke", member.Member);
        var firstCall = Assert.IsType<CallExpr>(member.Object);
        var firstMember = Assert.IsType<MemberExpr>(firstCall.Callee);
        Assert.Equal("getMethod", firstMember.Member);
        Assert.Equal("obj", Assert.IsType<NameExpr>(firstMember.Object).Name);
    }

    [Fact]
    public void Parse_OperatorPrecedence_MultiplicationOverAddition()
    {
        var expr = Utils.ParseSingleExpr<BinaryExpr>("a + b * c");
        Assert.Equal(TokenType.Plus, expr.Op);
        Assert.Equal("a", Assert.IsType<NameExpr>(expr.Left).Name);
        var right = Assert.IsType<BinaryExpr>(expr.Right);
        Assert.Equal(TokenType.Star, right.Op);
        Assert.Equal("b", Assert.IsType<NameExpr>(right.Left).Name);
        Assert.Equal("c", Assert.IsType<NameExpr>(right.Right).Name);
    }

    [Fact]
    public void Parse_OperatorPrecedence_LogicalAndOverLogicalOr()
    {
        var expr = Utils.ParseSingleExpr<BinaryExpr>("a || b && c");
        Assert.Equal(TokenType.OrOr, expr.Op);
        Assert.Equal("a", Assert.IsType<NameExpr>(expr.Left).Name);
        var right = Assert.IsType<BinaryExpr>(expr.Right);
        Assert.Equal(TokenType.AndAnd, right.Op);
    }

    [Fact]
    public void Parse_MemberAssignment_ReturnsAssignExprWithMemberTarget()
    {
        var expr = Utils.ParseSingleExpr<AssignExpr>("this.field = 42");
        var member = Assert.IsType<MemberExpr>(expr.Target);
        Assert.Equal("this", Assert.IsType<NameExpr>(member.Object).Name);
        Assert.Equal("field", member.Member);
        Assert.Equal(42, Assert.IsType<IntLit>(expr.Value).Value);
    }

    [Fact]
    public void Parse_IndexAssignment_ReturnsAssignExprWithIndexTarget()
    {
        var expr = Utils.ParseSingleExpr<AssignExpr>("arr[0] = 100");
        var index = Assert.IsType<IndexExpr>(expr.Target);
        Assert.Equal("arr", Assert.IsType<NameExpr>(index.Object).Name);
        Assert.Equal(0, Assert.IsType<IntLit>(index.Index).Value);
        Assert.Equal(100, Assert.IsType<IntLit>(expr.Value).Value);
    }
}
