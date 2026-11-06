using SabakaLang.Lexer.Enums;

namespace SabakaLang.Lexer.Tests;

public class CommentTests
{
    [Fact]
    public void Tokenize_SingleLineComment_ReturnsCommentToken()
    {
        var result = Utils.Tokenize("// This is a comment");

        Assert.False(result.HasErrors);
        Assert.Equal(2, result.Tokens.Count);
        Assert.Equal(TokenType.Comment, result.Tokens[0].Type);
        Assert.Equal("// This is a comment", result.Tokens[0].Value);
        Assert.Equal(TokenType.Eof, result.Tokens[1].Type);
    }
    
    [Fact]
    public void Tokenize_CommentFollowedByCode_TokenizesCorrectly()
    {
        var source = "// comment line\nint x = 42;";
        var result = Utils.Tokenize(source);

        Assert.False(result.HasErrors);
        Assert.Equal(7, result.Tokens.Count);
        Assert.Equal(TokenType.Comment, result.Tokens[0].Type);
        Assert.Equal("// comment line", result.Tokens[0].Value);
        Assert.Equal(TokenType.IntKeyword, result.Tokens[1].Type);
        Assert.Equal(TokenType.Identifier, result.Tokens[2].Type);
        Assert.Equal(TokenType.Equal, result.Tokens[3].Type);
        Assert.Equal(TokenType.IntLiteral, result.Tokens[4].Type);
        Assert.Equal(TokenType.Semicolon, result.Tokens[5].Type);
        Assert.Equal(TokenType.Eof, result.Tokens[6].Type);
    }

    [Fact]
    public void Tokenize_CommentWithCarriageReturn_EndsAtCarriageReturn()
    {
        var source = "// comment\r\nint y;";
        var result = Utils.Tokenize(source);

        Assert.False(result.HasErrors);
        Assert.Equal(TokenType.Comment, result.Tokens[0].Type);
        Assert.Equal("// comment", result.Tokens[0].Value);
        Assert.Equal(TokenType.IntKeyword, result.Tokens[1].Type);
    }

    [Fact]
    public void Tokenize_DivisionVsComment_DistinguishesSlashAndComment()
    {
        var source = "10 / 2 // division";
        var result = Utils.Tokenize(source);

        Assert.False(result.HasErrors);
        Assert.Equal(5, result.Tokens.Count);
        Assert.Equal(TokenType.IntLiteral, result.Tokens[0].Type);
        Assert.Equal(TokenType.Slash, result.Tokens[1].Type);
        Assert.Equal(TokenType.IntLiteral, result.Tokens[2].Type);
        Assert.Equal(TokenType.Comment, result.Tokens[3].Type);
        Assert.Equal(TokenType.Eof, result.Tokens[4].Type);
    }
}
