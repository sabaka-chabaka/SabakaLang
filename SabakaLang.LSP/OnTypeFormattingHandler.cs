using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace SabakaLang.LSP;

public class OnTypeFormattingHandler : DocumentOnTypeFormattingHandlerBase
{
    private readonly DocumentStore _documentStore;

    public OnTypeFormattingHandler(DocumentStore documentStore)
    {
        _documentStore = documentStore;
    }

    protected override DocumentOnTypeFormattingRegistrationOptions CreateRegistrationOptions(DocumentOnTypeFormattingCapability capability, ClientCapabilities clientCapabilities)
    {
        return new DocumentOnTypeFormattingRegistrationOptions
        {
            DocumentSelector = new TextDocumentSelector(
                new TextDocumentFilter { Pattern = "**/*.sabaka" },
                new TextDocumentFilter { Language = "sabaka" }
            ),
            FirstTriggerCharacter = "(",
            MoreTriggerCharacter = new Container<string>("{", "[", "\"", ";")
        };
    }

    public override Task<TextEditContainer?> Handle(DocumentOnTypeFormattingParams request, CancellationToken cancellationToken)
    {
        var source = _documentStore.GetDocument(request.TextDocument.Uri);
        if (source == null) return Task.FromResult<TextEditContainer?>(null);

        int offset = PositionHelper.GetOffset(source, request.Position);
        if (offset <= 0) return Task.FromResult<TextEditContainer?>(null);

        string typedChar = request.Character;

        // Auto-closing
        if (typedChar == "(" || typedChar == "{" || typedChar == "[" || typedChar == "\"")
        {
            string? closing = typedChar switch
            {
                "(" => ")",
                "{" => "}",
                "[" => "]",
                "\"" => "\"",
                _ => null
            };

            if (closing != null)
            {
                return Task.FromResult<TextEditContainer?>(new TextEditContainer(new TextEdit
                {
                    Range = new Range(request.Position, request.Position),
                    NewText = closing
                }));
            }
        }

        // Semicolon "move out"
        if (typedChar == ";")
        {
            // The semicolon was just inserted at offset - 1.
            int searchOffset = offset;
            int moveCount = 0;
            while (searchOffset < source.Length && (source[searchOffset] == ')' || source[searchOffset] == ']' || source[searchOffset] == '}'))
            {
                searchOffset++;
                moveCount++;
            }

            if (moveCount > 0)
            {
                var semicolonRange = new Range(
                    PositionHelper.GetPosition(source, offset - 1),
                    PositionHelper.GetPosition(source, offset)
                );

                var targetPosition = PositionHelper.GetPosition(source, searchOffset);

                return Task.FromResult<TextEditContainer?>(new TextEditContainer(
                    new TextEdit
                    {
                        Range = semicolonRange,
                        NewText = ""
                    },
                    new TextEdit
                    {
                        Range = new Range(targetPosition, targetPosition),
                        NewText = ";"
                    }
                ));
            }
        }

        return Task.FromResult<TextEditContainer?>(null);
    }
}
