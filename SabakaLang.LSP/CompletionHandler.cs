using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace SabakaLang.LSP;

public class CompletionHandler : CompletionHandlerBase
{
    private readonly string[] _keywords = {
        "if", "else", "while", "for", "foreach", "in", "func", "return", "void",
        "int", "float", "string", "bool", "true", "false",
        "struct", "class", "interface", "new", "override", "super",
        "switch", "case", "default", "enum",
        "public", "private", "protected"
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
        });

        return Task.FromResult(new CompletionList(items));
    }

    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken cancellationToken)
    {
        return Task.FromResult(request);
    }
}
