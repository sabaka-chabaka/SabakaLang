using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SabakaLang.Compiler;
using SabakaLang.Compiler.Lexing;
using SymbolKind = SabakaLang.Compiler.Binding.SymbolKind;

namespace SabakaLang.LanguageServer.Handlers;

public sealed class RenameHandler(DocumentStore store) : RenameHandlerBase, IPrepareRenameHandler
{
    protected override RenameRegistrationOptions CreateRegistrationOptions(
        RenameCapability capability,
        ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = SabakaDocumentSelector.Instance, PrepareProvider = true };

    public override Task<WorkspaceEdit?> Handle(RenameParams request, CancellationToken ct)
    {
        var analysis = store.Get(request.TextDocument.Uri.ToString());
        if (analysis is null) return Task.FromResult<WorkspaceEdit?>(null);

        var word = PositionHelper.WordAt(analysis.Source, request.Position);
        if (word is null) return Task.FromResult<WorkspaceEdit?>(null);

        var newName = request.NewName;
        var edits = new List<TextEdit>();

        foreach (var token in analysis.Lexer.Tokens)
        {
            if (token.Type != TokenType.Identifier) continue;
            if (token.Value != word) continue;

            edits.Add(new TextEdit
            {
                Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                    PositionHelper.ToLsp(token.Start),
                    PositionHelper.ToLsp(token.End)),
                NewText = newName
            });
        }

        if (edits.Count == 0) return Task.FromResult<WorkspaceEdit?>(null);

        var changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
        {
            [request.TextDocument.Uri] = edits
        };

        return Task.FromResult<WorkspaceEdit?>(new WorkspaceEdit { Changes = changes });
    }

    public Task<RangeOrPlaceholderRange?> Handle(
        PrepareRenameParams request, CancellationToken ct)
    {
        var analysis = store.Get(request.TextDocument.Uri.ToString());
        if (analysis is null) return Task.FromResult<RangeOrPlaceholderRange?>(null);

        var offset = PositionHelper.ToOffset(analysis.Source, request.Position);
        var token = analysis.Lexer.Tokens.FirstOrDefault(t =>
            t.Type == TokenType.Identifier &&
            t.Start.Offset <= offset && offset <= t.End.Offset);

        if (token.Type == TokenType.Eof)
            return Task.FromResult<RangeOrPlaceholderRange?>(null);

        if (analysis.Bind.Symbols.Lookup(token.Value)
            .Any(s => s.Kind == SymbolKind.BuiltIn))
            return Task.FromResult<RangeOrPlaceholderRange?>(null);

        var range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
            PositionHelper.ToLsp(token.Start),
            PositionHelper.ToLsp(token.End));

        return Task.FromResult<RangeOrPlaceholderRange?>(
            new RangeOrPlaceholderRange(new PlaceholderRange { Range = range, Placeholder = token.Value }));
    }
}
