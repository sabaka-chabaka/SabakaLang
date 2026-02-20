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
        _symbolIndex = symbolIndex;
    }

    protected override HoverRegistrationOptions CreateRegistrationOptions(HoverCapability capability, ClientCapabilities clientCapabilities)
    {
        return new HoverRegistrationOptions
        {
            DocumentSelector = new TextDocumentSelector(
                new TextDocumentFilter { Pattern = "**/*.sabaka" },
                new TextDocumentFilter { Language = "sabaka" }
            )
        };
    }

    public override Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken)
    {
        var source = _documentStore.GetDocument(request.TextDocument.Uri);
        if (source == null) return Task.FromResult<Hover?>(null);

        int offset = PositionHelper.GetOffset(source, request.Position);
        
        var lexer = new Lexer.Lexer(source);
        var tokens = lexer.Tokenize(false);
        
        var token = tokens.FirstOrDefault(t => t.TokenStart <= offset && t.TokenEnd >= offset);
        
        if (token != null)
        {
            string detail = $"**Token:** `{token.Type}`\n\n**Value:** `{token.Value}`";
            
            if (token.Type == TokenType.Identifier)
            {
                var symbol = _symbolIndex.GetAvailableSymbols(request.TextDocument.Uri, offset)
                    .FirstOrDefault(s => s.Name == token.Value);
    
                if (symbol != null)
                {
                    string prettyType = symbol.Type switch
                    {
                        "IntKeyword" => "int",
                        "FloatKeyword" => "float", 
                        "StringKeyword" => "string",
                        "BoolKeyword" => "bool",
                        "VoidKeyword" => "void",
                        _ => symbol.Type
                    };

                    detail = $"**{symbol.Kind}:** `{symbol.Name}`\n\n";
    
                    // Показываем сигнатуру с параметрами
                    if (symbol.Kind == SabakaLang.LSP.Analysis.SymbolKind.Function || 
                        symbol.Kind == SabakaLang.LSP.Analysis.SymbolKind.Method)
                    {
                        string paramsDisplay = symbol.Parameters ?? "";
                        detail += $"**Signature:** `{symbol.Name}({paramsDisplay})` → `{prettyType}`\n\n";
                    }
    
                    if (!string.IsNullOrEmpty(prettyType) && prettyType != "unknown")
                        detail += $"**Type:** `{prettyType}`\n\n";
        
                    if (symbol.SourceFile != null && symbol.SourceFile != request.TextDocument.Uri.GetFileSystemPath())
                        detail += $"**Imported from:** `{Path.GetFileName(symbol.SourceFile)}`";
                }
                else if (token.Value == "print")
                {
                    detail = "**Built-in Function:** `print`  \nPrints the given value to the standard output.";
                }
                else if (token.Value == "input")
                {
                    detail = "**Built-in Function:** `input`  \nReads a line of string from the standard input.";
                }
            }

            return Task.FromResult<Hover?>(new Hover
            {
                Contents = new MarkedStringsOrMarkupContent(new MarkupContent
                {
                    Kind = MarkupKind.Markdown,
                    Value = detail
                }),
                Range = PositionHelper.GetRange(source, token.TokenStart, token.TokenEnd)
            });
        }

        return Task.FromResult<Hover?>(null);
    }
}
