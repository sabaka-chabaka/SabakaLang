using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SabakaLang.LSP.Analysis;
using SabakaSymbolKind = SabakaLang.LSP.Analysis.SymbolKind;

namespace SabakaLang.LSP;

public class CompletionHandler : CompletionHandlerBase
{
    private readonly SymbolIndex _symbolIndex;
    private readonly DocumentStore _documentStore;

    public CompletionHandler(SymbolIndex symbolIndex, DocumentStore documentStore)
    {
        _symbolIndex = symbolIndex;
        _documentStore = documentStore;
    }

    private readonly string[] _keywords = {
        "if", "else", "while", "for", "foreach", "in", "func", "return", "void",
        "int", "float", "string", "bool", "true", "false",
        "struct", "class", "interface", "new", "override", "super",
        "switch", "case", "default", "enum",
        "public", "private", "protected"
    };

    private readonly string[] _builtInFunctions = {
        "print", "input"
    };

    protected override CompletionRegistrationOptions CreateRegistrationOptions(CompletionCapability capability, ClientCapabilities clientCapabilities)
    {
        return new CompletionRegistrationOptions
        {
            DocumentSelector = new TextDocumentSelector(
                new TextDocumentFilter { Pattern = "**/*.sabaka" },
                new TextDocumentFilter { Language = "sabaka" }
            ),
            TriggerCharacters = new Container<string>(".", ":")
        };
    }

    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
    {
        var items = _keywords.Select(k => new CompletionItem
        {
            Label = k,
            Kind = CompletionItemKind.Keyword,
            Detail = "Keyword"
        }).Concat(_builtInFunctions.Select(f => new CompletionItem
        {
            Label = f,
            Kind = CompletionItemKind.Function,
            Detail = "Built-in Function"
        }));

        var source = _documentStore.GetDocument(request.TextDocument.Uri);
        int offset = source != null ? PositionHelper.GetOffset(source, request.Position) : 0;

        var symbols = _symbolIndex.GetAvailableSymbols(request.TextDocument.Uri, offset)
            .Where(s => s.Kind != SabakaSymbolKind.BuiltIn)
            .Select(s => new CompletionItem
            {
                Label = s.Name,
                Kind = MapSymbolKind(s.Kind),
                Detail = s.Type
            });

        return Task.FromResult(new CompletionList(items.Concat(symbols)));
    }

    private CompletionItemKind MapSymbolKind(SabakaSymbolKind kind)
    {
        return kind switch
        {
            SabakaSymbolKind.Variable => CompletionItemKind.Variable,
            SabakaSymbolKind.Function => CompletionItemKind.Function,
            SabakaSymbolKind.Class => CompletionItemKind.Class,
            SabakaSymbolKind.Parameter => CompletionItemKind.Variable,
            _ => CompletionItemKind.Text
        };
    }

    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken cancellationToken)
    {
        return Task.FromResult(request);
    }
}
