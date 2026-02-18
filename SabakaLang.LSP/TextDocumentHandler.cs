using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using SabakaLang.Lexer;
using SabakaLang.Parser;
using SabakaLang.Exceptions;

namespace SabakaLang.LSP;

public class TextDocumentHandler : TextDocumentSyncHandlerBase
{
    private readonly ILanguageServerFacade _router;
    private readonly DocumentStore _documentStore;
    private readonly TextDocumentSelector _documentSelector = new TextDocumentSelector(
        new TextDocumentFilter { Pattern = "**/*.sabaka" },
        new TextDocumentFilter { Language = "sabaka" }
    );

    public TextDocumentHandler(ILanguageServerFacade router, DocumentStore documentStore)
    {
        _router = router;
        _documentStore = documentStore;
    }

    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri)
    {
        return new TextDocumentAttributes(uri, "sabaka");
    }

    public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri;
        var text = request.ContentChanges.First().Text;
        _documentStore.UpdateDocument(uri, text);

        _router.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = uri,
            Diagnostics = GetDiagnostics(text)
        });

        return Unit.Task;
    }

    public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri;
        var text = request.TextDocument.Text;
        _documentStore.UpdateDocument(uri, text);

        _router.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = uri,
            Diagnostics = GetDiagnostics(text)
        });

        return Unit.Task;
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken)
    {
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken)
    {
        _documentStore.RemoveDocument(request.TextDocument.Uri);
        return Unit.Task;
    }

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(TextSynchronizationCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new TextDocumentSyncRegistrationOptions
        {
            DocumentSelector = _documentSelector,
            Change = TextDocumentSyncKind.Full
        };
    }

    private Container<Diagnostic> GetDiagnostics(string source)
    {
        var diagnostics = new List<Diagnostic>();
        try
        {
            var lexer = new Lexer.Lexer(source);
            var tokens = lexer.Tokenize(false);
            var parser = new Parser.Parser(tokens);
            parser.ParseProgram();
        }
        catch (SabakaLangException ex)
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Range = PositionHelper.GetRange(source, ex.Position, ex.Position),
                Message = ex.Message,
                Source = "SabakaLang"
            });
        }
        catch (Exception ex)
        {
             // General errors might not have a position
             diagnostics.Add(new Diagnostic
             {
                 Severity = DiagnosticSeverity.Error,
                 Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(new Position(0, 0), new Position(0, 0)),
                 Message = ex.Message,
                 Source = "SabakaLang"
             });
        }

        return diagnostics;
    }
}
