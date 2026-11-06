using SabakaLang.AST;
using SabakaLang.Lexer.Enums;

namespace SabakaLang.Parser.Tests;

public class LiteralAndPrimaryExprTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("42", 42)]
    [InlineData("1000000", 1000000)]
    public void Parse_IntLiteral_ReturnsIntLit(string source, int expected)
    {
        var lit = Utils.ParseSingleExpr<IntLit>(source);
        Assert.Equal(expected, lit.Value);
    }

    [Theory]
    [InlineData("3.14", 3.14)]
    [InlineData("0.5", 0.5)]
    [InlineData("100.001", 100.001)]
    public void Parse_FloatLiteral_ReturnsFloatLit(string source, double expected)
    {
        var lit = Utils.ParseSingleExpr<FloatLit>(source);
        Assert.Equal(expected, lit.Value);
    }

    [Theory]
    [InlineData("\"hello world\"", "hello world")]
    [InlineData("\"\"", "")]
    [InlineData("\"escaped \\n line\"", "escaped \n line")]
    public void Parse_StringLiteral_ReturnsStringLit(string source, string expected)
    {
        var lit = Utils.ParseSingleExpr<StringLit>(source);
        Assert.Equal(expected, lit.Value);
    }

    [Theory]
    [InlineData("'a'", 'a')]
    [InlineData("'Z'", 'Z')]
    [InlineData("'0'", '0')]
    public void Parse_CharLiteral_ReturnsCharLit(string source, char expected)
    {
        var lit = Utils.ParseSingleExpr<CharLit>(source);
        Assert.Equal(expected, lit.Value);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Parse_BoolLiteral_ReturnsBoolLit(string source, bool expected)
    {
        var lit = Utils.ParseSingleExpr<BoolLit>(source);
        Assert.Equal(expected, lit.Value);
    }

    [Fact]
    public void Parse_NullLiteral_ReturnsNullLit()
    {
        var lit = Utils.ParseSingleExpr<NullLit>("null");
        Assert.NotNull(lit);
    }

    [Theory]
    [InlineData("foo")]
    [InlineData("bar123")]
    [InlineData("_myVar")]
    public void Parse_Identifier_ReturnsNameExpr(string name)
    {
        var expr = Utils.ParseSingleExpr<NameExpr>(name);
        Assert.Equal(name, expr.Name);
    }

    [Fact]
    public void Parse_SuperKeyword_ReturnsSuperExpr()
    {
        var expr = Utils.ParseSingleExpr<SuperExpr>("super");
        Assert.NotNull(expr);
    }

    [Fact]
    public void Parse_NewExpression_Simple_ReturnsNewExpr()
    {
        var expr = Utils.ParseSingleExpr<NewExpr>("new MyClass()");
        Assert.Equal("MyClass", expr.TypeName);
        Assert.Empty(expr.TypeArgs);
        Assert.Empty(expr.Args);
    }

    [Fact]
    public void Parse_NewExpression_WithGenericAndArgs_ReturnsNewExpr()
    {
        var expr = Utils.ParseSingleExpr<NewExpr>("new Container<int, string>(10, \"hello\")");
        Assert.Equal("Container", expr.TypeName);
        Assert.Equal(new[] { "int", "string" }, expr.TypeArgs);
        Assert.Equal(2, expr.Args.Count);
        Assert.Equal(10, Assert.IsType<IntLit>(expr.Args[0]).Value);
        Assert.Equal("hello", Assert.IsType<StringLit>(expr.Args[1]).Value);
    }

    [Fact]
    public void Parse_ArrayLiteral_Empty_ReturnsArrayExpr()
    {
        var expr = Utils.ParseSingleExpr<ArrayExpr>("[]");
        Assert.Empty(expr.Elements);
    }

    [Fact]
    public void Parse_ArrayLiteral_WithElements_ReturnsArrayExpr()
    {
        var expr = Utils.ParseSingleExpr<ArrayExpr>("[1, 2, 3]");
        Assert.Equal(3, expr.Elements.Count);
        Assert.Equal(1, Assert.IsType<IntLit>(expr.Elements[0]).Value);
        Assert.Equal(2, Assert.IsType<IntLit>(expr.Elements[1]).Value);
        Assert.Equal(3, Assert.IsType<IntLit>(expr.Elements[2]).Value);
    }

    [Fact]
    public void Parse_GroupedExpression_ReturnsInnerExpr()
    {
        var expr = Utils.ParseSingleExpr<BinaryExpr>("(1 + 2) * 3");
        Assert.Equal(TokenType.Star, expr.Op);
        var left = Assert.IsType<BinaryExpr>(expr.Left);
        Assert.Equal(TokenType.Plus, left.Op);
        Assert.Equal(1, Assert.IsType<IntLit>(left.Left).Value);
        Assert.Equal(2, Assert.IsType<IntLit>(left.Right).Value);
        Assert.Equal(3, Assert.IsType<IntLit>(expr.Right).Value);
    }

    [Fact]
    public void Parse_InterpolatedString_WithoutExpressions_ReturnsInterpolatedStringExpr()
    {
        var expr = Utils.ParseSingleExpr<InterpolatedStringExpr>("$\"plain text\"");
        Assert.Single(expr.Parts);
        var lit = Assert.IsType<StringLit>(expr.Parts[0]);
        Assert.Equal("plain text", lit.Value);
    }

    [Fact]
    public void Parse_InterpolatedString_WithExpression_ReturnsInterpolatedStringExpr()
    {
        var expr = Utils.ParseSingleExpr<InterpolatedStringExpr>("$\"Hello {name}!\"");
        Assert.Equal(3, expr.Parts.Count);
        Assert.Equal("Hello ", Assert.IsType<StringLit>(expr.Parts[0]).Value);
        Assert.Equal("name", Assert.IsType<NameExpr>(expr.Parts[1]).Name);
        Assert.Equal("!", Assert.IsType<StringLit>(expr.Parts[2]).Value);
    }

    [Fact]
    public void Parse_InterpolatedString_WithComplexExpression_ReturnsInterpolatedStringExpr()
    {
        var expr = Utils.ParseSingleExpr<InterpolatedStringExpr>("$\"Result: {1 + 2}\"");
        Assert.Equal(2, expr.Parts.Count);
        Assert.Equal("Result: ", Assert.IsType<StringLit>(expr.Parts[0]).Value);
        var bin = Assert.IsType<BinaryExpr>(expr.Parts[1]);
        Assert.Equal(TokenType.Plus, bin.Op);
    }
}
