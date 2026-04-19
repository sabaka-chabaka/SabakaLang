using SabakaLang.Compiler;
using SabakaLang.LanguageServer;
using SabakaLang.LanguageServer.Services;

namespace SabakaLang.LanguageServer.UnitTests;

/// <summary>
/// Tests semantic token classification logic directly via the DocumentStore
/// (avoids full LSP plumbing for builder-based API).
/// </summary>
public class SemanticTokensTests
{
    private static DocumentAnalysis Analyze(string source) =>
        new DocumentStore().Analyze("file:///test.sabaka", source);

    // Helper: classify a single token by its type
    private static int ClassifyTokenType(Token token, DocumentAnalysis analysis)
    {
        return token.Type switch
        {
            TokenType.Comment                  => SemanticTokensLegend.Comment,
            TokenType.StringLiteral            => SemanticTokensLegend.StringType,
            TokenType.InterpolatedStringLiteral=> SemanticTokensLegend.StringType,
            TokenType.IntLiteral               => SemanticTokensLegend.Number,
            TokenType.FloatLiteral             => SemanticTokensLegend.Number,
            TokenType.If or TokenType.Else
                or TokenType.While or TokenType.For
                or TokenType.Foreach or TokenType.Return
                or TokenType.Class or TokenType.Interface
                or TokenType.New or TokenType.Null
                or TokenType.True or TokenType.False
                or TokenType.IntKeyword or TokenType.FloatKeyword
                or TokenType.StringKeyword or TokenType.BoolKeyword
                or TokenType.VoidKeyword       => SemanticTokensLegend.Keyword,
            TokenType.Identifier => ClassifyIdentifier(token.Value, analysis),
            _ => -1
        };

        static int ClassifyIdentifier(string name, DocumentAnalysis a)
        {
            var sym = a.Bind.Symbols.Lookup(name).FirstOrDefault();
            if (sym is null) return SemanticTokensLegend.Variable;
            return sym.Kind switch
            {
                SymbolKind.Class     => SemanticTokensLegend.Class,
                SymbolKind.Interface => SemanticTokensLegend.Interface,
                SymbolKind.Struct    => SemanticTokensLegend.Struct,
                SymbolKind.Enum      => SemanticTokensLegend.Enum,
                SymbolKind.EnumMember=> SemanticTokensLegend.EnumMember,
                SymbolKind.Function  => SemanticTokensLegend.Function,
                SymbolKind.Method    => SemanticTokensLegend.Method,
                SymbolKind.BuiltIn   => SemanticTokensLegend.Function,
                SymbolKind.Parameter => SemanticTokensLegend.Parameter,
                SymbolKind.Field     => SemanticTokensLegend.Property,
                SymbolKind.TypeParam => SemanticTokensLegend.TypeParameter,
                _                    => SemanticTokensLegend.Variable
            };
        }
    }

    [Fact]
    public void Comments_ClassifiedAsComment()
    {
        var analysis = Analyze("// this is a comment\nint x = 1;");
        var commentToken = analysis.Lexer.Tokens.First(t => t.Type == TokenType.Comment);
        ClassifyTokenType(commentToken, analysis).Should().Be(SemanticTokensLegend.Comment);
    }

    [Fact]
    public void StringLiteral_ClassifiedAsString()
    {
        var analysis = Analyze("string s = \"hello\";");
        var strToken = analysis.Lexer.Tokens.First(t => t.Type == TokenType.StringLiteral);
        ClassifyTokenType(strToken, analysis).Should().Be(SemanticTokensLegend.StringType);
    }

    [Fact]
    public void IntLiteral_ClassifiedAsNumber()
    {
        var analysis = Analyze("int x = 42;");
        var numToken = analysis.Lexer.Tokens.First(t => t.Type == TokenType.IntLiteral);
        ClassifyTokenType(numToken, analysis).Should().Be(SemanticTokensLegend.Number);
    }

    [Fact]
    public void FloatLiteral_ClassifiedAsNumber()
    {
        var analysis = Analyze("float x = 3.14;");
        var numToken = analysis.Lexer.Tokens.First(t => t.Type == TokenType.FloatLiteral);
        ClassifyTokenType(numToken, analysis).Should().Be(SemanticTokensLegend.Number);
    }

    [Fact]
    public void Keyword_If_ClassifiedAsKeyword()
    {
        var analysis = Analyze("if (true) {}");
        var kw = analysis.Lexer.Tokens.First(t => t.Type == TokenType.If);
        ClassifyTokenType(kw, analysis).Should().Be(SemanticTokensLegend.Keyword);
    }

    [Fact]
    public void ClassName_ClassifiedAsClass()
    {
        var analysis = Analyze("class MyClass {}\nMyClass obj = new MyClass();");
        var classToken = analysis.Lexer.Tokens
            .First(t => t.Type == TokenType.Identifier && t.Value == "MyClass");
        ClassifyTokenType(classToken, analysis).Should().Be(SemanticTokensLegend.Class);
    }

    [Fact]
    public void FunctionName_ClassifiedAsFunction()
    {
        var analysis = Analyze("int calculate(int x) { return x; }");
        var funcToken = analysis.Lexer.Tokens
            .First(t => t.Type == TokenType.Identifier && t.Value == "calculate");
        ClassifyTokenType(funcToken, analysis).Should().Be(SemanticTokensLegend.Function);
    }

    [Fact]
    public void EnumName_ClassifiedAsEnum()
    {
        var analysis = Analyze("enum Direction { North, South }");
        var enumToken = analysis.Lexer.Tokens
            .First(t => t.Type == TokenType.Identifier && t.Value == "Direction");
        ClassifyTokenType(enumToken, analysis).Should().Be(SemanticTokensLegend.Enum);
    }

    [Fact]
    public void BuiltinFunction_ClassifiedAsFunction()
    {
        var analysis = Analyze("print(\"hello\");");
        var printToken = analysis.Lexer.Tokens
            .First(t => t.Type == TokenType.Identifier && t.Value == "print");
        ClassifyTokenType(printToken, analysis).Should().Be(SemanticTokensLegend.Function);
    }

    [Fact]
    public void InterfaceName_ClassifiedAsInterface()
    {
        var analysis = Analyze("interface IRunner { void run(); }");
        var token = analysis.Lexer.Tokens
            .First(t => t.Type == TokenType.Identifier && t.Value == "IRunner");
        ClassifyTokenType(token, analysis).Should().Be(SemanticTokensLegend.Interface);
    }

    [Fact]
    public void InterpolatedString_ClassifiedAsString()
    {
        var analysis = Analyze("string s = $\"hello {1 + 1}\";");
        var tok = analysis.Lexer.Tokens
            .FirstOrDefault(t => t.Type == TokenType.InterpolatedStringLiteral);
        if (tok.Type != TokenType.Eof)
            ClassifyTokenType(tok, analysis).Should().Be(SemanticTokensLegend.StringType);
    }

    [Fact]
    public void Legend_TokenTypes_NotEmpty()
    {
        SemanticTokensLegend.TokenTypes.Should().NotBeEmpty();
        SemanticTokensLegend.TokenModifiers.Should().NotBeEmpty();
    }

    [Fact]
    public void Legend_IndexConstants_InBounds()
    {
        SemanticTokensLegend.Comment.Should().BeLessThan(SemanticTokensLegend.TokenTypes.Length);
        SemanticTokensLegend.Function.Should().BeLessThan(SemanticTokensLegend.TokenTypes.Length);
        SemanticTokensLegend.Keyword.Should().BeLessThan(SemanticTokensLegend.TokenTypes.Length);
        SemanticTokensLegend.Class.Should().BeLessThan(SemanticTokensLegend.TokenTypes.Length);
    }
}
