using SabakaLang.Lexer.Enums;

namespace SabakaLang.Lexer.Tests;

public class OperatorAndDelimiterTests
{
    [Theory]
    [InlineData("+", TokenType.Plus)]
    [InlineData("++", TokenType.PlusPlus)]
    [InlineData("+=", TokenType.PlusEqual)]
    [InlineData("-", TokenType.Minus)]
    [InlineData("--", TokenType.MinusMinus)]
    [InlineData("-=", TokenType.MinusEqual)]
    [InlineData("*", TokenType.Star)]
    [InlineData("*=", TokenType.StarEqual)]
    [InlineData("/", TokenType.Slash)]
    [InlineData("%", TokenType.Percent)]
    [InlineData("(", TokenType.LParen)]
    [InlineData(")", TokenType.RParen)]
    [InlineData("{", TokenType.LBrace)]
    [InlineData("}", TokenType.RBrace)]
    [InlineData("[", TokenType.LBracket)]
    [InlineData("]", TokenType.RBracket)]
    [InlineData(";", TokenType.Semicolon)]
    [InlineData(",", TokenType.Comma)]
    [InlineData(".", TokenType.Dot)]
    [InlineData("?", TokenType.Question)]
    [InlineData("??", TokenType.QuestionQuestion)]
    [InlineData("=", TokenType.Equal)]
    [InlineData("==", TokenType.EqualEqual)]
    [InlineData("!", TokenType.Bang)]
    [InlineData("!=", TokenType.NotEqual)]
    [InlineData(">", TokenType.Greater)]
    [InlineData(">=", TokenType.GreaterEqual)]
    [InlineData("<", TokenType.Less)]
    [InlineData("<=", TokenType.LessEqual)]
    [InlineData("&&", TokenType.AndAnd)]
    [InlineData("||", TokenType.OrOr)]
    [InlineData(":", TokenType.Colon)]
    [InlineData("::", TokenType.ColonColon)]
    public void Tokenize_OperatorsAndDelimiters_ReturnsExpectedTokenType(string symbol, TokenType expectedType)
    {
        var result = Utils.Tokenize(symbol);

        Assert.False(result.HasErrors);
        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Tokens.Count);
        Assert.Equal(expectedType, result.Tokens[0].Type);
        Assert.Equal(symbol, result.Tokens[0].Value);
        Assert.Equal(TokenType.Eof, result.Tokens[1].Type);
    }

    [Fact]
    public void Tokenize_ConsecutiveOperators_AreTokenizedCorrectly()
    {
        var source = "a += ++b - c-- * d /= e % f";
        var result = Utils.Tokenize(source);

        Assert.False(result.HasErrors);
        var expectedTypes = new[]
        {
            TokenType.Identifier, // a
            TokenType.PlusEqual,  // +=
            TokenType.PlusPlus,   // ++
            TokenType.Identifier, // b
            TokenType.Minus,      // -
            TokenType.Identifier, // c
            TokenType.MinusMinus, // --
            TokenType.Star,       // *
            TokenType.Identifier, // d
            TokenType.Slash,      // /
            TokenType.Equal,      // = (since /= is Slash then Equal in this grammar!)
            TokenType.Identifier, // e
            TokenType.Percent,    // %
            TokenType.Identifier, // f
            TokenType.Eof
        };

        Assert.Equal(expectedTypes.Length, result.Tokens.Count);
        for (int i = 0; i < expectedTypes.Length; i++)
        {
            Assert.Equal(expectedTypes[i], result.Tokens[i].Type);
        }
    }
}
