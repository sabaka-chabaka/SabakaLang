using SabakaLang.Lexer.Enums;

namespace SabakaLang.Lexer.Tests;

public class LiteralTests
{
    [Theory]
    [InlineData("0", "0")]
    [InlineData("123", "123")]
    [InlineData("9999999", "9999999")]
    public void Tokenize_IntLiterals_ReturnsIntLiteralToken(string source, string expectedValue)
    {
        var result = Utils.Tokenize(source);

        Assert.False(result.HasErrors);
        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Tokens.Count);
        Assert.Equal(TokenType.IntLiteral, result.Tokens[0].Type);
        Assert.Equal(expectedValue, result.Tokens[0].Value);
        Assert.Equal(TokenType.Eof, result.Tokens[1].Type);
    }

    [Theory]
    [InlineData("0.0", "0.0")]
    [InlineData("3.14159", "3.14159")]
    [InlineData("100.5", "100.5")]
    [InlineData("0.12345", "0.12345")]
    public void Tokenize_FloatLiterals_ReturnsFloatLiteralToken(string source, string expectedValue)
    {
        var result = Utils.Tokenize(source);

        Assert.False(result.HasErrors);
        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Tokens.Count);
        Assert.Equal(TokenType.FloatLiteral, result.Tokens[0].Type);
        Assert.Equal(expectedValue, result.Tokens[0].Value);
        Assert.Equal(TokenType.Eof, result.Tokens[1].Type);
    }

    [Fact]
    public void Tokenize_NumberWithMultipleDots_ReportsError()
    {
        var result = Utils.Tokenize("1.2.3");

        Assert.True(result.HasErrors);
        Assert.Single(result.Errors);
        Assert.Equal("Invalid number: multiple dots.", result.Errors[0].Message);
    }

    [Theory]
    [InlineData("\"\"", "")]
    [InlineData("\"hello world\"", "hello world")]
    [InlineData("\"escape: \\\"quoted\\\"\"", "escape: \"quoted\"")]
    [InlineData("\"backslash: \\\\\"", "backslash: \\")]
    [InlineData("\"newline: \\n\"", "newline: \n")]
    [InlineData("\"carriage return: \\r\"", "carriage return: \r")]
    [InlineData("\"tab: \\t\"", "tab: \t")]
    [InlineData("\"null byte: \\0\"", "null byte: \0")]
    [InlineData("\"other escape: \\a\"", "other escape: a")]
    public void Tokenize_StringLiterals_ParsesAndUnescapesCorrectly(string source, string expectedValue)
    {
        var result = Utils.Tokenize(source);

        Assert.False(result.HasErrors);
        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Tokens.Count);
        Assert.Equal(TokenType.StringLiteral, result.Tokens[0].Type);
        Assert.Equal(expectedValue, result.Tokens[0].Value);
        Assert.Equal(TokenType.Eof, result.Tokens[1].Type);
    }

    [Fact]
    public void Tokenize_UnterminatedString_ReportsError()
    {
        var result = Utils.Tokenize("\"unterminated string");

        Assert.True(result.HasErrors);
        Assert.Single(result.Errors);
        Assert.Equal("Unterminated string literal", result.Errors[0].Message);
        Assert.Equal(1, result.Errors[0].Position.Line);
        Assert.Equal(1, result.Errors[0].Position.Column);
    }

    [Theory]
    [InlineData("'a'", "a")]
    [InlineData("'Z'", "Z")]
    [InlineData("'0'", "0")]
    [InlineData("'_'", "_")]
    [InlineData("' '", " ")]
    public void Tokenize_CharLiterals_ReturnsCharLiteralToken(string source, string expectedValue)
    {
        var result = Utils.Tokenize(source);

        Assert.False(result.HasErrors);
        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Tokens.Count);
        Assert.Equal(TokenType.CharLiteral, result.Tokens[0].Type);
        Assert.Equal(expectedValue, result.Tokens[0].Value);
        Assert.Equal(TokenType.Eof, result.Tokens[1].Type);
    }

    [Theory]
    [InlineData("'")]
    [InlineData("'a")]
    [InlineData("'ab")]
    public void Tokenize_UnterminatedChar_ReportsError(string source)
    {
        var result = Utils.Tokenize(source);

        Assert.True(result.HasErrors);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Message == "Unterminated char literal");
    }

    [Theory]
    [InlineData("$\"\"", "")]
    [InlineData("$\"Hello {name}!\"", "Hello {name}!")]
    [InlineData("$\"Value: {x + y}, Result: {z}\"", "Value: {x + y}, Result: {z}")]
    [InlineData("$\"Nested {foo({bar})}\"", "Nested {foo({bar})}")]
    [InlineData("$\"Escape \\\" \\\\ \\n \\r \\t \\0 \\z\"", "Escape \" \\ \n \r \t \0 z")]
    public void Tokenize_InterpolatedStringLiterals_ReturnsInterpolatedStringToken(string source, string expectedValue)
    {
        var result = Utils.Tokenize(source);

        Assert.False(result.HasErrors);
        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Tokens.Count);
        Assert.Equal(TokenType.InterpolatedStringLiteral, result.Tokens[0].Type);
        Assert.Equal(expectedValue, result.Tokens[0].Value);
        Assert.Equal(TokenType.Eof, result.Tokens[1].Type);
    }

    [Fact]
    public void Tokenize_UnterminatedInterpolatedString_ReportsError()
    {
        var result = Utils.Tokenize("$\"Hello {name}");

        Assert.True(result.HasErrors);
        Assert.Single(result.Errors);
        Assert.Equal("Unterminated interpolated string", result.Errors[0].Message);
    }
}
