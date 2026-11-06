using SabakaLang.Lexer.Enums;

namespace SabakaLang.Lexer.Tests;

public class IdentifierTests
{
    [Theory]
    [InlineData("x")]
    [InlineData("myVariable")]
    [InlineData("_hidden")]
    [InlineData("__internal__")]
    [InlineData("variable123")]
    [InlineData("snake_case_identifier")]
    [InlineData("PascalCaseIdentifier")]
    [InlineData("camelCaseIdentifier")]
    [InlineData("boolVal")]
    [InlineData("ifCondition")]
    [InlineData("classes")]
    [InlineData("my_123_var")]
    public void Tokenize_ValidIdentifiers_ReturnsIdentifierToken(string identifier)
    {
        var result = Utils.Tokenize(identifier);

        Assert.False(result.HasErrors);
        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Tokens.Count);
        Assert.Equal(TokenType.Identifier, result.Tokens[0].Type);
        Assert.Equal(identifier, result.Tokens[0].Value);
        Assert.Equal(TokenType.Eof, result.Tokens[1].Type);
    }

    [Fact]
    public void Tokenize_MultipleIdentifiers_ReturnsAllIdentifiers()
    {
        var source = "foo bar baz _qux";
        var result = Utils.Tokenize(source);

        Assert.False(result.HasErrors);
        Assert.Equal(5, result.Tokens.Count);
        Assert.Equal("foo", result.Tokens[0].Value);
        Assert.Equal(TokenType.Identifier, result.Tokens[0].Type);
        Assert.Equal("bar", result.Tokens[1].Value);
        Assert.Equal(TokenType.Identifier, result.Tokens[1].Type);
        Assert.Equal("baz", result.Tokens[2].Value);
        Assert.Equal(TokenType.Identifier, result.Tokens[2].Type);
        Assert.Equal("_qux", result.Tokens[3].Value);
        Assert.Equal(TokenType.Identifier, result.Tokens[3].Type);
        Assert.Equal(TokenType.Eof, result.Tokens[4].Type);
    }
}
