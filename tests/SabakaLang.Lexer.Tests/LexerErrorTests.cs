using SabakaLang.Lexer.Enums;

namespace SabakaLang.Lexer.Tests;

public class LexerErrorTests
{
    [Fact]
    public void Tokenize_UnexpectedCharacter_GeneratesError()
    {
        var result = Utils.Tokenize("@");

        Assert.True(result.HasErrors);
        Assert.Single(result.Errors);
        Assert.Equal("Unexpected character: '@'", result.Errors[0].Message);
        Assert.Equal(1, result.Errors[0].Position.Line);
        Assert.Equal(1, result.Errors[0].Position.Column);
        Assert.Equal(0, result.Errors[0].Position.Offset);
    }

    [Fact]
    public void Tokenize_SingleAmpersand_GeneratesExpectedError()
    {
        var result = Utils.Tokenize("&");

        Assert.True(result.HasErrors);
        Assert.Single(result.Errors);
        Assert.Equal("Expected '&' after '&'", result.Errors[0].Message);
    }

    [Fact]
    public void Tokenize_SinglePipe_GeneratesExpectedError()
    {
        var result = Utils.Tokenize("|");

        Assert.True(result.HasErrors);
        Assert.Single(result.Errors);
        Assert.Equal("Expected '|' after '|'", result.Errors[0].Message);
    }

    [Fact]
    public void Tokenize_DollarWithoutQuote_GeneratesUnexpectedCharacterError()
    {
        var result = Utils.Tokenize("$");

        Assert.True(result.HasErrors);
        Assert.Single(result.Errors);
        Assert.Equal("Unexpected character: '$'", result.Errors[0].Message);
    }

    [Fact]
    public void Tokenize_MultipleErrors_AllRecorded()
    {
        var result = Utils.Tokenize("@ # $ &");

        Assert.True(result.HasErrors);
        Assert.Equal(4, result.Errors.Count);
        Assert.Equal("Unexpected character: '@'", result.Errors[0].Message);
        Assert.Equal("Unexpected character: '#'", result.Errors[1].Message);
        Assert.Equal("Unexpected character: '$'", result.Errors[2].Message);
        Assert.Equal("Expected '&' after '&'", result.Errors[3].Message);
    }
}
