namespace SabakaLang.Compiler.UnitTests;

public class LexerTests
{
    private static List<Token> Tokenize(string source)
    {
        var result = new Lexer(source).Tokenize();
        return result.Tokens.ToList();
    }
    
    [Fact]
    public void IntLiteral_ReturnsCorrectToken()
    {
        var tokens = Tokenize("42");
        
        Assert.Equal(TokenType.IntLiteral, tokens[0].Type);
        Assert.Equal("42", tokens[0].Value);
    }
    
    [Fact]
    public void FloatLiteral_ReturnsCorrectToken()
    {
        var tokens = Tokenize("3.14");
        
        Assert.Equal(TokenType.FloatLiteral, tokens[0].Type);
        Assert.Equal("3.14", tokens[0].Value);
    }

    [Fact]
    public void StringLiteral_ReturnsCorrectValue()
    {
        var tokens = Tokenize("\"hello\"");
        
        Assert.Equal(TokenType.StringLiteral, tokens[0].Type);
        Assert.Equal("hello", tokens[0].Value);
    }

    [Fact]
    public void StringLiteral_WithEscapes_ParsedCorrectly()
    {
        var tokens = Tokenize("\"line1\\nline2\"");
        
        Assert.Equal(TokenType.StringLiteral, tokens[0].Type);
        Assert.Equal("line1\nline2", tokens[0].Value);
    }
    
    [Theory]
    [InlineData("if",     TokenType.If)]
    [InlineData("else",   TokenType.Else)]
    [InlineData("while",  TokenType.While)]
    [InlineData("return", TokenType.Return)]
    [InlineData("class",  TokenType.Class)]
    [InlineData("int",    TokenType.IntKeyword)]
    [InlineData("true",   TokenType.True)]
    [InlineData("false",  TokenType.False)]
    public void Keywords_RecognizedCorrectly(string source, TokenType expected)
    {
        var tokens = Tokenize(source);
 
        Assert.Equal(expected, tokens[0].Type);
    }
    
    [Fact]
    public void Identifier_NotKeyword_ReturnsIdentifier()
    {
        var tokens = Tokenize("myVariable");
        
        Assert.Equal(TokenType.Identifier, tokens[0].Type);
        Assert.Equal("myVariable", tokens[0].Value);
    }
    
    [Theory]
    [InlineData("+",  TokenType.Plus)]
    [InlineData("-",  TokenType.Minus)]
    [InlineData("*",  TokenType.Star)]
    [InlineData("/",  TokenType.Slash)]
    [InlineData("%",  TokenType.Percent)]
    [InlineData("==", TokenType.EqualEqual)]
    [InlineData("!=", TokenType.NotEqual)]
    [InlineData(">=", TokenType.GreaterEqual)]
    [InlineData("<=", TokenType.LessEqual)]
    [InlineData("&&", TokenType.AndAnd)]
    [InlineData("||", TokenType.OrOr)]
    [InlineData("::", TokenType.ColonColon)]
    public void Operators_RecognizedCorrectly(string source, TokenType expected)
    {
        var tokens = Tokenize(source);
 
        Assert.Equal(expected, tokens[0].Type);
    }

    [Fact]
    public void Token_Position_CorrectLineAndColumn()
    {
        var tokens = Tokenize("int x");
        
        Assert.Equal(1, tokens[0].Start.Line);
        Assert.Equal(1, tokens[0].Start.Column);
        
        Assert.Equal(1, tokens[1].End.Line);
        Assert.Equal(5, tokens[1].End.Column);
    }

    [Fact]
    public void Token_Multiline_CorrectLine()
    {
        var tokens = Tokenize("int\nx");
        Assert.Equal(1, tokens[0].Start.Line);
        Assert.Equal(2, tokens[1].Start.Line);
    }

    [Fact]
    public void UnexpectedCharacter_AddsError()
    {
        var result = new Lexer("@").Tokenize();
 
        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Message.Contains("@"));
    }
    
    [Fact]
    public void UnterminatedString_AddsError()
    {
        var result = new Lexer("\"hello").Tokenize();
 
        Assert.True(result.HasErrors);
    }
    
    [Fact]
    public void MultipleErrors_AllCollected()
    {
        var result = new Lexer("@ $").Tokenize();
 
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void EmptySource_ReturnsOnlyEof()
    {
        var tokens = Tokenize("");
        
        Assert.Single(tokens);
        Assert.Equal(TokenType.EOF, tokens[0].Type);
    }

    [Fact]
    public void LastToken_AlwaysEof()
    {
        var tokens = Tokenize("int x = 42;");
        
        Assert.Equal(TokenType.EOF, tokens.Last().Type);
    }

    [Fact]
    public void LineComment_ReturnsCommentToken()
    {
        var tokens = Tokenize("// this is a comment");
        
        Assert.Equal(TokenType.Comment, tokens[0].Type);
    }
    
    [Fact]
    public void LineComment_DoesNotAffectNextLine()
    {
        var tokens = Tokenize("// comment\nint");
 
        Assert.Equal(TokenType.Comment, tokens[0].Type);
        Assert.Equal(TokenType.IntKeyword, tokens[1].Type);
    }
}