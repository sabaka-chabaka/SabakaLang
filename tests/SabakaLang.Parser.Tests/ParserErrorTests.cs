using SabakaLang.AST;

namespace SabakaLang.Parser.Tests;

public class ParserErrorTests
{
    [Fact]
    public void Parse_InvalidAssignmentTarget_ReportsError()
    {
        var result = Utils.Parse("1 + 2 = 3;");
        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Message.Contains("Invalid assignment target"));
    }

    [Fact]
    public void Parse_InvalidCompoundAssignmentTarget_ReportsError()
    {
        var result = Utils.Parse("10 += 5;");
        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Message.Contains("Invalid assignment target"));
    }

    [Fact]
    public void Parse_InvalidIncrementTarget_ReportsError()
    {
        var result = Utils.Parse("(a + b)++;");
        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Message.Contains("Invalid increment target"));
    }

    [Fact]
    public void Parse_UnexpectedTokenAtStart_ReportsError()
    {
        var result = Utils.Parse("}");
        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Message.Contains("Unexpected token"));
    }

    [Fact]
    public void Parse_MissingExpectedToken_ReportsError()
    {
        var result = Utils.Parse("if x > 0 { }");
        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Message.Contains("Expected LParen"));
    }

    [Fact]
    public void Parse_SwitchInvalidToken_ReportsError()
    {
        var result = Utils.Parse("switch (x) { invalid_token: 1; }");
        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Message.Contains("Expected 'case' or 'default'"));
    }

    [Fact]
    public void Parse_InterpolatedString_InvalidExpression_ReportsError()
    {
        var result = Utils.Parse("$\"Hello { }\"");
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void Parse_ErrorRecovery_ParsesSubsequentValidStatements()
    {
        var source = @"
            * 5;
            int x = 42;
        ";
        var result = Utils.Parse(source);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Statements, s => s is VarDecl { Name: "x" });
    }
}
