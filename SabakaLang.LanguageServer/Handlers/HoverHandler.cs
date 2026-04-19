using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SabakaLang.Compiler;
using SymbolKind = SabakaLang.Compiler.SymbolKind;

namespace SabakaLang.LanguageServer.Handlers;

public sealed class HoverHandler(DocumentStore store) : HoverHandlerBase
{
    protected override HoverRegistrationOptions CreateRegistrationOptions(
        HoverCapability capability,
        ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = SabakaDocumentSelector.Instance };

    public override Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken)
    {
        var analysis = store.Get(request.TextDocument.Uri.ToString());
        if (analysis is null) return Task.FromResult<Hover?>(null);

        var word = PositionHelper.WordAt(analysis.Source, request.Position);
        if (word is null) return Task.FromResult<Hover?>(null);

        var symbols = analysis.Bind.Symbols.Lookup(word).ToList();
        if (symbols.Count == 0) return Task.FromResult<Hover?>(null);

        var sym = symbols[0];
        var md = FormatSymbol(sym);

        return Task.FromResult<Hover?>(new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = md
            })
        });
    }

    private static string FormatSymbol(Symbol sym)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("```sabaka");
        switch (sym.Kind)
        {
            case SymbolKind.Function:
            case SymbolKind.Method:
            case SymbolKind.BuiltIn:
                var typeParams = sym.TypeParams.Count > 0
                    ? $"<{string.Join(", ", sym.TypeParams)}>"
                    : "";
                var parent = sym.ParentName is not null ? $"{sym.ParentName}." : "";
                sb.AppendLine($"{sym.Type} {parent}{sym.Name}{typeParams}({sym.Parameters ?? ""})");
                break;

            case SymbolKind.Class:
                sb.AppendLine($"class {sym.Name}");
                break;

            case SymbolKind.Interface:
                sb.AppendLine($"interface {sym.Name}");
                break;

            case SymbolKind.Struct:
                sb.AppendLine($"struct {sym.Name}");
                break;

            case SymbolKind.Enum:
                sb.AppendLine($"enum {sym.Name}");
                break;

            case SymbolKind.EnumMember:
                sb.AppendLine($"{sym.ParentName}.{sym.Name}");
                break;

            default:
                sb.AppendLine($"{sym.Type} {sym.Name}");
                break;
        }
        sb.AppendLine("```");

        sb.Append($"**{sym.Kind}** `{sym.Name}`");
        if (sym.ParentName is not null)
            sb.Append($" in `{sym.ParentName}`");
        sb.AppendLine();
        sb.AppendLine($"Type: `{sym.Type}`");

        return sb.ToString();
    }
}