using SabakaLang.Lexer.Enums;
using SabakaLang.Lexer.Structures;

namespace SabakaLang.Lexer.Tests;

public class PositionAndLayoutTests
{
    [Fact]
    public void Tokenize_EmptyString_ReturnsSingleEofTokenAtStart()
    {
        var result = Utils.Tokenize("");

        Assert.False(result.HasErrors);
        Assert.Single(result.Tokens);
        Assert.Equal(TokenType.Eof, result.Tokens[0].Type);
        Assert.Equal(1, result.Tokens[0].Start.Line);
        Assert.Equal(1, result.Tokens[0].Start.Column);
        Assert.Equal(0, result.Tokens[0].Start.Offset);
    }

    [Fact]
    public void Tokenize_WhitespaceOnly_ReturnsSingleEofToken()
    {
        var result = Utils.Tokenize("   \t  \n   ");

        Assert.False(result.HasErrors);
        Assert.Single(result.Tokens);
        Assert.Equal(TokenType.Eof, result.Tokens[0].Type);
    }

    [Fact]
    public void Tokenize_MultiLineSource_TracksLineAndColumnCorrectly()
    {
        var source = "int a = 10;\nfloat b = 20.5;";
        var result = Utils.Tokenize(source);

        Assert.False(result.HasErrors);
        
        // Line 1: int (1:1 - 1:3), a (1:5 - 1:5), = (1:7 - 1:7), 10 (1:9 - 1:10), ; (1:11 - 1:11)
        var tokenInt = result.Tokens[0];
        Assert.Equal(TokenType.IntKeyword, tokenInt.Type);
        Assert.Equal(new Position(1, 1, 0), tokenInt.Start);
        Assert.Equal(new Position(1, 3, 2), tokenInt.End);

        var tokenA = result.Tokens[1];
        Assert.Equal(TokenType.Identifier, tokenA.Type);
        Assert.Equal(new Position(1, 5, 4), tokenA.Start);
        Assert.Equal(new Position(1, 5, 4), tokenA.End);

        // Line 2: float (2:1 - 2:5)
        var tokenFloat = result.Tokens[5];
        Assert.Equal(TokenType.FloatKeyword, tokenFloat.Type);
        Assert.Equal(new Position(2, 1, 12), tokenFloat.Start);
        Assert.Equal(new Position(2, 5, 16), tokenFloat.End);
    }

    [Fact]
    public void Token_ToString_FormatsCorrectly()
    {
        var intToken = new Token(TokenType.IntLiteral, "42", new Position(1, 5, 4), new Position(1, 6, 5));
        var floatToken = new Token(TokenType.FloatLiteral, "3.14", new Position(2, 1, 10), new Position(2, 4, 13));
        var plusToken = new Token(TokenType.Plus, "+", new Position(3, 2, 20), new Position(3, 2, 20));

        Assert.Equal("IntLiteral(42) at 1:5", intToken.ToString());
        Assert.Equal("FloatLiteral(3.14) at 2:1", floatToken.ToString());
        Assert.Equal("Plus at 3:2", plusToken.ToString());
    }

    [Fact]
    public void Tokenize_NewlineBoundaries_CalculatesPositionsAccurately()
    {
        var source = "a\nb\r\nc";
        var result = Utils.Tokenize(source);

        Assert.False(result.HasErrors);
        Assert.Equal(4, result.Tokens.Count);
        Assert.Equal("a", result.Tokens[0].Value);
        Assert.Equal(1, result.Tokens[0].Start.Line);
        Assert.Equal("b", result.Tokens[1].Value);
        Assert.Equal(2, result.Tokens[1].Start.Line);
        Assert.Equal("c", result.Tokens[2].Value);
        Assert.Equal(3, result.Tokens[2].Start.Line);
    }
}
