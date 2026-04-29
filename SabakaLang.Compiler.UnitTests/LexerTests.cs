using System.Runtime.InteropServices;

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

    [Fact]
    public void CharLiteral_ReturnsCorrectToken()
    {
        var tokens = Tokenize("'a'");
        
        Assert.Equal(TokenType.CharLiteral, tokens[0].Type);
        Assert.Equal("a", tokens[0].Value);
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
    [InlineData("null",   TokenType.Null)]
    [InlineData("char", TokenType.CharKeyword)]
    [InlineData("is", TokenType.Is)]
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
    [InlineData("+=", TokenType.PlusEqual)]
    [InlineData("-=", TokenType.MinusEqual)]
    [InlineData("*=", TokenType.StarEqual)]
    [InlineData("++", TokenType.PlusPlus)]
    [InlineData("--", TokenType.MinusMinus)]
    [InlineData("?",  TokenType.Question)]
    [InlineData("??", TokenType.QuestionQuestion)]
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
        Assert.Equal(TokenType.Eof, tokens[0].Type);
    }

    [Fact]
    public void LastToken_AlwaysEof()
    {
        var tokens = Tokenize("int x = 42;");
        
        Assert.Equal(TokenType.Eof, tokens.Last().Type);
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
    
    [Fact]
    public void InterpolatedString_SimpleVar_EmitsToken()
    {
        var tokens = Tokenize("$\"hello {name}\"");
 
        Assert.Equal(TokenType.InterpolatedStringLiteral, tokens[0].Type);
        Assert.Equal("hello {name}", tokens[0].Value);
    }
 
    [Fact]
    public void InterpolatedString_NoHoles_EmitsToken()
    {
        var tokens = Tokenize("$\"just text\"");
 
        Assert.Equal(TokenType.InterpolatedStringLiteral, tokens[0].Type);
        Assert.Equal("just text", tokens[0].Value);
    }
 
    [Fact]
    public void InterpolatedString_MultipleHoles_PreservesRawContent()
    {
        var tokens = Tokenize("$\"{a} and {b}\"");
 
        Assert.Equal(TokenType.InterpolatedStringLiteral, tokens[0].Type);
        Assert.Equal("{a} and {b}", tokens[0].Value);
    }
 
    [Fact]
    public void InterpolatedString_ExpressionHole_PreservesExpression()
    {
        var tokens = Tokenize("$\"result: {x + 1}\"");
 
        Assert.Equal(TokenType.InterpolatedStringLiteral, tokens[0].Type);
        Assert.Equal("result: {x + 1}", tokens[0].Value);
    }
 
    [Fact]
    public void InterpolatedString_FollowedBySemicolon_TokenizedCorrectly()
    {
        var tokens = Tokenize("$\"{x}\";");

        Assert.Equal(TokenType.InterpolatedStringLiteral, tokens[0].Type);
        Assert.Equal(TokenType.Semicolon, tokens[1].Type);
        Assert.Equal(TokenType.Eof, tokens[2].Type);
    }

    [Fact]
    public void Lexer_MultipleDots_InNumber_Error()
    {
        var lexer = new Lexer("1.2.3");
        var result = lexer.Tokenize();
        Assert.True(result.HasErrors);
        Assert.Contains("multiple dots", result.Errors[0].Message);
    }

    [Fact]
    public void Lexer_Unterminated_InterpolatedString_Error()
    {
        var lexer = new Lexer("$\"hello {x}");
        var result = lexer.Tokenize();
        Assert.True(result.HasErrors);
        Assert.Contains("Unterminated interpolated string", result.Errors[0].Message);
    }

    [Fact]
    public void Lexer_InterpolatedString_Escapes()
    {
        var tokens = Tokenize("$\"\\\\\\n\\r\\t\\0\\\"\"");
        Assert.Equal(TokenType.InterpolatedStringLiteral, tokens[0].Type);
        Assert.Equal("\\\n\r\t\0\"", tokens[0].Value);
    }

    [Fact]
    public void Lexer_DoubleOperator_MissingSecondChar_Error()
    {
        var lexer = new Lexer("& ");
        var result = lexer.Tokenize();
        Assert.True(result.HasErrors);
        Assert.Contains("Expected '&'", result.Errors[0].Message);
    }

    [Fact]
    public void Lexer_Token_ToString_Literal()
    {
        var tokens = Tokenize("123 1.23");
        Assert.Contains("IntLiteral(123)", tokens[0].ToString());
        Assert.Contains("FloatLiteral(1.23)", tokens[1].ToString());
    }
}