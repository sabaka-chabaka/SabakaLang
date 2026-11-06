using SabakaLang.Lexer.Enums;
using SabakaLang.Lexer.Results;

namespace SabakaLang.Lexer.Tests;

public class KeywordsTests
{
    [Theory]
    [InlineData("bool", TokenType.BoolKeyword)]
    [InlineData("char", TokenType.CharKeyword)]
    [InlineData("true", TokenType.True)]
    [InlineData("false", TokenType.False)]
    [InlineData("if", TokenType.If)]
    [InlineData("else", TokenType.Else)]
    [InlineData("while", TokenType.While)]
    [InlineData("for", TokenType.For)]
    [InlineData("foreach", TokenType.Foreach)]
    [InlineData("in", TokenType.In)]
    [InlineData("int", TokenType.IntKeyword)]
    [InlineData("float", TokenType.FloatKeyword)]
    [InlineData("string", TokenType.StringKeyword)]
    [InlineData("void", TokenType.VoidKeyword)]
    [InlineData("return", TokenType.Return)]
    [InlineData("struct", TokenType.StructKeyword)]
    [InlineData("enum", TokenType.Enum)]
    [InlineData("class", TokenType.Class)]
    [InlineData("interface", TokenType.Interface)]
    [InlineData("new", TokenType.New)]
    [InlineData("override", TokenType.Override)]
    [InlineData("super", TokenType.Super)]
    [InlineData("null", TokenType.Null)]
    [InlineData("is", TokenType.Is)]
    [InlineData("switch", TokenType.Switch)]
    [InlineData("case", TokenType.Case)]
    [InlineData("default", TokenType.Default)]
    [InlineData("public", TokenType.Public)]
    [InlineData("private", TokenType.Private)]
    [InlineData("protected", TokenType.Protected)]
    [InlineData("import", TokenType.Import)]
    [InlineData("const", TokenType.Const)]
    public void Tokenize_Keywords_ReturnsExpectedTokenType(string keyword, TokenType expectedType)
    {
        LexerResult result = Utils.Tokenize(keyword);

        Assert.False(result.HasErrors);
        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Tokens.Count);
        Assert.Equal(expectedType, result.Tokens[0].Type);
        Assert.Equal(keyword, result.Tokens[0].Value);
        Assert.Equal(TokenType.Eof, result.Tokens[1].Type);
    }

    [Fact]
    public void Tokenize_MultipleKeywords_ReturnsAllKeywordsInOrder()
    {
        var source = "public class MyClass { private int x; }";
        var result = Utils.Tokenize(source);

        Assert.False(result.HasErrors);
        var expectedTypes = new[]
        {
            TokenType.Public,
            TokenType.Class,
            TokenType.Identifier,
            TokenType.LBrace,
            TokenType.Private,
            TokenType.IntKeyword,
            TokenType.Identifier,
            TokenType.Semicolon,
            TokenType.RBrace,
            TokenType.Eof
        };

        Assert.Equal(expectedTypes.Length, result.Tokens.Count);
        for (int i = 0; i < expectedTypes.Length; i++)
        {
            Assert.Equal(expectedTypes[i], result.Tokens[i].Type);
        }
    }
}
