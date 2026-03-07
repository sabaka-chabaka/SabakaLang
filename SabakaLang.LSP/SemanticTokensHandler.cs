using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SabakaLang.Lexer;
using SabakaLang.LSP.Analysis;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using SymbolKind = SabakaLang.LSP.Analysis.SymbolKind;

namespace SabakaLang.LSP;

public class SemanticTokensHandler : SemanticTokensHandlerBase
{
    private readonly DocumentStore _documentStore;
    private readonly SymbolIndex _symbolIndex;

    public SemanticTokensHandler(DocumentStore documentStore, SymbolIndex symbolIndex)
    {
        _documentStore = documentStore;
        _symbolIndex   = symbolIndex;
    }

    protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(
        SemanticTokensCapability cap, ClientCapabilities cc) => new()
    {
        DocumentSelector = new TextDocumentSelector(
            new TextDocumentFilter { Pattern = "**/*.sabaka" },
            new TextDocumentFilter { Language = "sabaka" }
        ),
        Legend = new SemanticTokensLegend
        {
            TokenTypes = new[]
            {
                "keyword", "type", "number", "string", "comment",
                "operator", "variable", "function", "parameter", "class", "module"
            }.Select(x => new SemanticTokenType(x)).ToArray(),
            TokenModifiers = Array.Empty<SemanticTokenModifier>()
        },
        Full  = true,
        Range = false
    };

    protected override async Task Tokenize(SemanticTokensBuilder builder,
        ITextDocumentIdentifierParams id, CancellationToken ct)
    {
        var source = _documentStore.GetDocument(id.TextDocument.Uri);
        if (source == null) return;

        var lexer  = new Lexer.Lexer(source);
        var tokens = lexer.Tokenize(true);

        // Build a fast lookup of all symbols visible in this document
        var allSymbols = _symbolIndex
            .GetAvailableSymbols(id.TextDocument.Uri, int.MaxValue / 2)
            .ToDictionary(s => s.Name, s => s, StringComparer.Ordinal);

        foreach (var token in tokens)
        {
            var type = ClassifyToken(token, id.TextDocument.Uri, allSymbols);
            if (type != null)
            {
                var range = PositionHelper.GetRange(source, token.TokenStart, token.TokenEnd);
                builder.Push(range, type);
            }
        }
    }

    protected override Task<SemanticTokensDocument> GetSemanticTokensDocument(
        ITextDocumentIdentifierParams id, CancellationToken ct) =>
        Task.FromResult(new SemanticTokensDocument(RegistrationOptions.Legend));

    private string? ClassifyToken(Token token, DocumentUri uri,
        Dictionary<string, Symbol> symbols)
    {
        if (token.Type == TokenType.Identifier)
        {
            // Check module alias first
            var moduleMembers = _symbolIndex
                .GetModuleMembers(uri, token.Value.ToLower())
                .ToList();
            if (moduleMembers.Count > 0) return "module";

            if (symbols.TryGetValue(token.Value, out var sym))
                return SymbolKindToTokenType(sym.Kind);

            // Built-in functions
            if (token.Value is "print" or "input" or "sleep" or
                "readFile" or "writeFile" or "appendFile" or
                "fileExists" or "deleteFile" or "readLines" or
                "time" or "timeMs" or "httpGet" or "httpPost" or
                "httpPostJson" or "ord" or "chr")
                return "function";
        }

        return MapTokenType(token.Type);
    }

    private static string SymbolKindToTokenType(SymbolKind k) => k switch
    {
        SymbolKind.Variable  => "variable",
        SymbolKind.Function  => "function",
        SymbolKind.Class     => "class",
        SymbolKind.Parameter => "parameter",
        SymbolKind.BuiltIn   => "function",
        SymbolKind.Method    => "function",
        SymbolKind.Field     => "variable",
        SymbolKind.Module    => "module",
        _                   => "variable"
    };

    private static string? MapTokenType(TokenType t) => t switch
    {
        TokenType.BoolKeyword or TokenType.True or TokenType.False or
        TokenType.If or TokenType.Else or TokenType.While or TokenType.For or
        TokenType.Enum or TokenType.Function or TokenType.Return or
        TokenType.VoidKeyword or TokenType.Foreach or TokenType.In or
        TokenType.StructKeyword or TokenType.Class or TokenType.New or
        TokenType.Override or TokenType.Super or TokenType.Interface or
        TokenType.Switch or TokenType.Case or TokenType.Default or
        TokenType.Public or TokenType.Private or TokenType.Protected or
        TokenType.Import => "keyword",

        TokenType.IntKeyword or TokenType.FloatKeyword or
        TokenType.StringKeyword => "type",

        TokenType.IntLiteral or TokenType.Number or TokenType.FloatLiteral => "number",

        TokenType.StringLiteral => "string",

        TokenType.Comment => "comment",

        TokenType.Plus or TokenType.Minus or TokenType.Star or
        TokenType.Slash or TokenType.Percent or TokenType.Equal or
        TokenType.EqualEqual or TokenType.NotEqual or TokenType.Greater or
        TokenType.Less or TokenType.GreaterEqual or TokenType.LessEqual or
        TokenType.Dot or TokenType.AndAnd or TokenType.OrOr or
        TokenType.Bang or TokenType.Colon or TokenType.ColonColon => "operator",

        _ => null
    };
}