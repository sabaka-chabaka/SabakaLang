using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;

namespace SabakaLang.LanguageServer.Handlers;

public sealed class TextDocumentSyncHandler(
    DocumentStore store,
    ILanguageServerFacade server)
    : TextDocumentSyncHandlerBase
{
    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
        TextSynchronizationCapability capability,
        ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = SabakaDocumentSelector.Instance,
            Change = TextDocumentSyncKind.Full,
            Save = new SaveOptions { IncludeText = true }
        };

    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri) =>
        new(uri, "sabaka");

    public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken ct)
    {
        var analysis = store.Analyze(request.TextDocument.Uri.ToString(), request.TextDocument.Text);
        PublishDiagnostics(request.TextDocument.Uri, analysis);
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken ct)
    {
        var text = request.ContentChanges.LastOrDefault()?.Text ?? "";
        var analysis = store.Analyze(request.TextDocument.Uri.ToString(), text);
        PublishDiagnostics(request.TextDocument.Uri, analysis);
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken ct)
    {
        if (request.Text is not null)
        {
            var analysis = store.Analyze(request.TextDocument.Uri.ToString(), request.Text);
            PublishDiagnostics(request.TextDocument.Uri, analysis);
        }
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken ct)
    {
        store.Remove(request.TextDocument.Uri.ToString());
        // clear diagnostics
        server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = request.TextDocument.Uri,
            Diagnostics = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>()
        });
        return Unit.Task;
    }

    private void PublishDiagnostics(DocumentUri uri, DocumentAnalysis analysis)
    {
        var lspDiags = analysis.Diagnostics.Select(d => new OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic
        {
            Severity = d.Severity switch
            {
                DiagnosticSeverity.Error       => OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Error,
                DiagnosticSeverity.Warning     => OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Warning,
                DiagnosticSeverity.Information => OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Information,
                _                              => OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Hint
            },
            Message = d.Message,
            Range = PositionHelper.ToLspRange(d.Start, d.End),
            Source = "sabaka-ls"
        }).ToList();

        server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = uri,
            Diagnostics = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>(lspDiags)
        });
    }
}
