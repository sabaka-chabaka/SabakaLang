using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SabakaLang.Lexer;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace SabakaLang.LSP;

public class SemanticTokensHandler : SemanticTokensHandlerBase
{
    private readonly DocumentStore _documentStore;
    private readonly SymbolIndex _symbolIndex;

    public SemanticTokensHandler(DocumentStore documentStore, SymbolIndex symbolIndex)
    {
        _documentStore = documentStore;
        _symbolIndex = symbolIndex;
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
                    "keyword", "type", "number", "string", "comment", "operator", "variable", "function", "parameter", "class"
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
            var type = MapToken(token, identifier.TextDocument.Uri);
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

    private string? MapToken(Token token, DocumentUri uri)
    {
        if (token.Type == TokenType.Identifier)
        {
            if (token.Value == "print" || token.Value == "input")
            {
                return "function";
            }

            // Look up in symbol index
            var symbols = _symbolIndex.GetAvailableSymbols(uri, token.TokenStart);
            var symbol = symbols.FirstOrDefault(s => s.Name == token.Value);
            
            if (symbol == null)
            {
                // If not in available symbols, it might be a member. 
                // Look up in ALL symbols as a fallback for highlighting.
                symbol = _symbolIndex.GetAllSymbols().FirstOrDefault(s => s.Name == token.Value);
            }

            if (symbol != null)
            {
                return MapSymbolKindToTokenType(symbol.Kind);
            }
        }

        return MapTokenType(token.Type);
    }

    private string? MapSymbolKindToTokenType(SabakaLang.LSP.Analysis.SymbolKind kind)
    {
        return kind switch
        {
            SabakaLang.LSP.Analysis.SymbolKind.Variable => "variable",
            SabakaLang.LSP.Analysis.SymbolKind.Function => "function",
            SabakaLang.LSP.Analysis.SymbolKind.Class => "class",
            SabakaLang.LSP.Analysis.SymbolKind.Parameter => "parameter",
            SabakaLang.LSP.Analysis.SymbolKind.BuiltIn => "function",
            SabakaLang.LSP.Analysis.SymbolKind.Method => "function",
            SabakaLang.LSP.Analysis.SymbolKind.Field => "variable",
            _ => null
        };
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
            TokenType.Public or TokenType.Private or TokenType.Protected or TokenType.Import => "keyword",

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
