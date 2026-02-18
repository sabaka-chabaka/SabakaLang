using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SabakaLang.Lexer;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace SabakaLang.LSP;

public class SemanticTokensHandler : SemanticTokensHandlerBase
{
    private readonly DocumentStore _documentStore;

    public SemanticTokensHandler(DocumentStore documentStore)
    {
        _documentStore = documentStore;
    }

    protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(SemanticTokensCapability capability, ClientCapabilities clientCapabilities)
    {
        return new SemanticTokensRegistrationOptions
        {
            DocumentSelector = new TextDocumentSelector(
                new TextDocumentFilter { Pattern = "**/*.sabaka" },
                new TextDocumentFilter { Language = "sabaka" }
            ),
            Legend = new SemanticTokensLegend
            {
                TokenTypes = new[]
                {
                    "keyword", "type", "number", "string", "comment", "operator", "variable", "function"
                }.Select(x => new SemanticTokenType(x)).ToArray(),
                TokenModifiers = new SemanticTokenModifier[0]
            },
            Full = true,
            Range = false
        };
    }

    protected override async Task Tokenize(SemanticTokensBuilder builder, ITextDocumentIdentifierParams identifier, CancellationToken cancellationToken)
    {
        var source = _documentStore.GetDocument(identifier.TextDocument.Uri);
        if (source == null) return;

        var lexer = new Lexer.Lexer(source);
        var tokens = lexer.Tokenize(true); // true to include comments

        foreach (var token in tokens)
        {
            var type = MapTokenType(token.Type);
            if (type != null)
            {
                var range = PositionHelper.GetRange(source, token.TokenStart, token.TokenEnd);
                builder.Push(range, type);
            }
        }
    }

    protected override Task<SemanticTokensDocument> GetSemanticTokensDocument(ITextDocumentIdentifierParams identifier, CancellationToken cancellationToken)
    {
        return Task.FromResult(new SemanticTokensDocument(RegistrationOptions.Legend));
    }

    private string? MapTokenType(TokenType type)
    {
        return type switch
        {
            TokenType.BoolKeyword or TokenType.True or TokenType.False or 
            TokenType.If or TokenType.Else or TokenType.While or TokenType.For or 
            TokenType.Enum or TokenType.Function or TokenType.Return or 
            TokenType.VoidKeyword or TokenType.Foreach or TokenType.In or 
            TokenType.StructKeyword or TokenType.Class or TokenType.New or 
            TokenType.Override or TokenType.Super or TokenType.Interface or 
            TokenType.Switch or TokenType.Case or TokenType.Default or 
            TokenType.Public or TokenType.Private or TokenType.Protected => "keyword",

            TokenType.IntKeyword or TokenType.FloatKeyword or TokenType.StringKeyword => "type",

            TokenType.IntLiteral or TokenType.Number or TokenType.FloatLiteral => "number",

            TokenType.StringLiteral => "string",

            TokenType.Comment => "comment",

            TokenType.Plus or TokenType.Minus or TokenType.Star or TokenType.Slash or 
            TokenType.Equal or TokenType.EqualEqual or TokenType.NotEqual or 
            TokenType.Greater or TokenType.Less or TokenType.GreaterEqual or 
            TokenType.LessEqual or TokenType.Dot or TokenType.AndAnd or 
            TokenType.OrOr or TokenType.Bang or TokenType.Colon or 
            TokenType.ColonColon => "operator",

            _ => null
        };
    }
}
