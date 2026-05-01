using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SabakaLang.Compiler;
using SabakaLang.Compiler.Lexing;
using SymbolKind = SabakaLang.Compiler.Binding.SymbolKind;

namespace SabakaLang.LanguageServer.Handlers;

public sealed class ReferencesHandler(DocumentStore store) : ReferencesHandlerBase
{
    protected override ReferenceRegistrationOptions CreateRegistrationOptions(
        ReferenceCapability capability,
        ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = SabakaDocumentSelector.Instance };

    public override Task<LocationContainer?> Handle(ReferenceParams request, CancellationToken cancellationToken)
    {
        var analysis = store.Get(request.TextDocument.Uri.ToString());
        if (analysis is null) return Task.FromResult<LocationContainer?>(null);
        
        var word = PositionHelper.WordAt(analysis.Source, request.Position);
        if (word is null) return Task.FromResult<LocationContainer?>(null);

        var locations = new List<Location>();
        
        if (request.Context.IncludeDeclaration)
        {
            foreach (var sym in analysis.Bind.Symbols.Lookup(word)
                         .Where(s => s.Kind is not SymbolKind.BuiltIn))
            {
                locations.Add(new Location
                {
                    Uri = request.TextDocument.Uri,
                    Range = PositionHelper.ToLspRange(sym.Span)
                });
            }
        }
        
        foreach (var token in analysis.Lexer.Tokens)
        {
            if (token.Type != TokenType.Identifier) continue;
            if (token.Value != word) continue;

            var range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                PositionHelper.ToLsp(token.Start),
                PositionHelper.ToLsp(token.End));

            if (request.Context.IncludeDeclaration)
            {
                locations.Add(new Location { Uri = request.TextDocument.Uri, Range = range });
            }
            else
            {
                var isDeclSpan = analysis.Bind.Symbols.Lookup(word)
                    .Any(s => s.Span.Start.Offset == token.Start.Offset);
                if (!isDeclSpan)
                    locations.Add(new Location { Uri = request.TextDocument.Uri, Range = range });
            }
        }
        
        var unique = locations
            .GroupBy(l => $"{l.Range.Start.Line}:{l.Range.Start.Character}")
            .Select(g => g.First())
            .ToList();

        return Task.FromResult<LocationContainer?>(new LocationContainer(unique));
    }
}