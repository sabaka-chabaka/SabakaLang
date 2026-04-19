using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace SabakaLang.LanguageServer.Handlers;

public sealed class DefinitionHandler(DocumentStore store) : DefinitionHandlerBase
{
    protected override DefinitionRegistrationOptions CreateRegistrationOptions(
        DefinitionCapability capability,
        ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = SabakaDocumentSelector.Instance };
    
    public override Task<LocationOrLocationLinks?> Handle(DefinitionParams request, CancellationToken ct)
    {
        var analysis = store.Get(request.TextDocument.Uri.ToString());
        if (analysis is null) return Task.FromResult<LocationOrLocationLinks?>(null);

        var word = PositionHelper.WordAt(analysis.Source, request.Position);
        if (word is null) return Task.FromResult<LocationOrLocationLinks?>(null);

        var symbols = analysis.Bind.Symbols.Lookup(word).ToList();
        if (symbols.Count == 0) return Task.FromResult<LocationOrLocationLinks?>(null);

        var locations = symbols
            .Where(s => s.Span.Start.Offset > 0 || s.Span.End.Offset > 0)
            .Where(s => s.Kind is not Compiler.SymbolKind.BuiltIn)
            .Select(s => new Location
            {
                Uri = request.TextDocument.Uri,
                Range = PositionHelper.ToLspRange(s.Span)
            })
            .ToList();

        if (locations.Count == 0) return Task.FromResult<LocationOrLocationLinks?>(null);

        return Task.FromResult<LocationOrLocationLinks?>(
            new LocationOrLocationLinks(locations.Select(l => new LocationOrLocationLink(l))));
    }
}