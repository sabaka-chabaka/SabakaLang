using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SabakaLang.Lexer;
using System.IO;

namespace SabakaLang.LSP;

public class HoverHandler : HoverHandlerBase
{
    private readonly DocumentStore _documentStore;
    private readonly SymbolIndex _symbolIndex;

    public HoverHandler(DocumentStore documentStore, SymbolIndex symbolIndex)
    {
        _documentStore = documentStore;
        _symbolIndex   = symbolIndex;
    }

    protected override HoverRegistrationOptions CreateRegistrationOptions(
        HoverCapability cap, ClientCapabilities cc) => new()
    {
        DocumentSelector = new TextDocumentSelector(
            new TextDocumentFilter { Pattern = "**/*.sabaka" },
            new TextDocumentFilter { Language = "sabaka" }
        )
    };

    public override Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken)
    {
        var source = _documentStore.GetDocument(request.TextDocument.Uri);
        if (source == null) return Task.FromResult<Hover?>(null);

        int offset = PositionHelper.GetOffset(source, request.Position);

        var lexer  = new Lexer.Lexer(source);
        var tokens = lexer.Tokenize(false);

        var token = tokens.FirstOrDefault(t => t.TokenStart <= offset && t.TokenEnd >= offset);
        if (token == null) return Task.FromResult<Hover?>(null);

        string md;

        if (token.Type == TokenType.Identifier)
        {
            // Check if this identifier is a module alias
            var moduleMembers = _symbolIndex
                .GetModuleMembers(request.TextDocument.Uri, token.Value.ToLower())
                .ToList();

            if (moduleMembers.Count > 0)
            {
                string file = moduleMembers.FirstOrDefault(s => s.SourceFile != null)?.SourceFile;
                md = $"**module** `{token.Value}`";
                if (file != null) md += $"  \nFrom: `{Path.GetFileName(file)}`";
                md += $"  \n{moduleMembers.Count} exported symbol(s)";
            }
            else
            {
                var sym = _symbolIndex
                    .GetAvailableSymbols(request.TextDocument.Uri, offset)
                    .FirstOrDefault(s => s.Name == token.Value);

                if (sym != null)
                {
                    string type = Normalize(sym.Type);
                    md = $"**{sym.Kind}** `{sym.Name}`";

                    if (sym.Kind is Analysis.SymbolKind.Function or Analysis.SymbolKind.Method)
                        md += $"  \n`{sym.Name}({sym.Parameters ?? ""})` → `{type}`";
                    else if (!string.IsNullOrEmpty(type) && type != "unknown")
                        md += $"  \nType: `{type}`";

                    if (sym.SourceFile != null &&
                        sym.SourceFile != request.TextDocument.Uri.GetFileSystemPath())
                        md += $"  \nFrom: `{Path.GetFileName(sym.SourceFile)}`";
                }
                else
                {
                    md = $"`{token.Value}`";
                }
            }
        }
        else
        {
            md = $"`{token.Type}` — `{token.Value}`";
        }

        return Task.FromResult<Hover?>(new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind  = MarkupKind.Markdown,
                Value = md
            }),
            Range = PositionHelper.GetRange(source, token.TokenStart, token.TokenEnd)
        });
    }

    private static string Normalize(string t) => t switch
    {
        "IntKeyword"    => "int",
        "FloatKeyword"  => "float",
        "StringKeyword" => "string",
        "BoolKeyword"   => "bool",
        "VoidKeyword"   => "void",
        _               => t
    };
}