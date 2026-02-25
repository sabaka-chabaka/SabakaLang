using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SabakaLang.LSP.Analysis;
using SabakaSymbolKind = SabakaLang.LSP.Analysis.SymbolKind;
using System.IO; 

namespace SabakaLang.LSP;

public class CompletionHandler : CompletionHandlerBase
{
    private readonly SymbolIndex _symbolIndex;
    private readonly DocumentStore _documentStore;

    public CompletionHandler(SymbolIndex symbolIndex, DocumentStore documentStore)
    {
        _symbolIndex = symbolIndex;
        _documentStore = documentStore;
    }

    private readonly string[] _keywords = {
        "if", "else", "while", "for", "foreach", "in", "func", "return", "void",
        "int", "float", "string", "bool", "true", "false",
        "struct", "class", "interface", "new", "override", "super",
        "switch", "case", "default", "enum",
        "public", "private", "protected", "import"
    };

    private readonly string[] _builtInFunctions = {
        "print", "input", "sleep"
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
        var source = _documentStore.GetDocument(request.TextDocument.Uri);
        if (source == null) return Task.FromResult(new CompletionList());

        int offset = PositionHelper.GetOffset(source, request.Position);

        // Check for member completion (e.g., "a." or "E::")
        if (offset > 0)
        {
            int triggerOffset = -1;
            if (source[offset - 1] == '.') triggerOffset = offset - 1;
            else if (offset > 1 && source[offset - 1] == ':' && source[offset - 2] == ':') triggerOffset = offset - 2;

            if (triggerOffset >= 0)
            {
                int end = triggerOffset - 1;
                while (end >= 0 && char.IsWhiteSpace(source[end])) end--;
                
                int start = end;
                while (start >= 0 && (char.IsLetterOrDigit(source[start]) || source[start] == '_')) start--;
                
                if (end >= 0)
                {
                    string targetName = source.Substring(start + 1, end - start);
                    var symbol = _symbolIndex.GetAvailableSymbols(request.TextDocument.Uri, end)
                        .FirstOrDefault(s => s.Name == targetName);
                    
                    if (symbol != null)
                    {
                        string typeName = symbol.Kind == SabakaSymbolKind.Class ? symbol.Name : symbol.Type;
                        
                        // Handle built-in type names which might be TokenTypes as strings
                        if (typeName == "IntKeyword") typeName = "int";
                        else if (typeName == "FloatKeyword") typeName = "float";
                        else if (typeName == "StringKeyword") typeName = "string";
                        else if (typeName == "BoolKeyword") typeName = "bool";
                        else if (typeName == "VoidKeyword") typeName = "void";

                        var members = _symbolIndex.GetMembers(typeName)
                            .Select(s => new CompletionItem
                            {
                                Label = s.Name,
                                Kind = MapSymbolKind(s.Kind),
                                Detail = GetPrettyType(s.Type),
                                Documentation = s.SourceFile != null ? $"From: {Path.GetFileName(s.SourceFile)}" : null  // ДОБАВЛЕНО
                            });
                        
                        return Task.FromResult(new CompletionList(members));
                    }
                }
            }
        }

        var items = _keywords.Select(k => new CompletionItem
        {
            Label = k,
            Kind = CompletionItemKind.Keyword,
            Detail = "Keyword"
        }).Concat(_builtInFunctions.Select(f => new CompletionItem
        {
            Label = f,
            Kind = CompletionItemKind.Function,
            Detail = "Built-in Function"
        }));

        var symbols = _symbolIndex.GetAvailableSymbols(request.TextDocument.Uri, offset)
            .Where(s => s.Kind != SabakaSymbolKind.BuiltIn)
            .Select(s => new CompletionItem
            {
                Label = s.Name,
                Kind = MapSymbolKind(s.Kind),
                Detail = GetPrettyType(s.Type),
                Documentation = s.SourceFile != null && s.SourceFile != request.TextDocument.Uri.GetFileSystemPath() 
                    ? $"Imported from: {Path.GetFileName(s.SourceFile)}"
                    : null
            });

        return Task.FromResult(new CompletionList(items.Concat(symbols)));
    }

    private CompletionItemKind MapSymbolKind(SabakaSymbolKind kind)
    {
        return kind switch
        {
            SabakaSymbolKind.Variable => CompletionItemKind.Variable,
            SabakaSymbolKind.Function => CompletionItemKind.Function,
            SabakaSymbolKind.Class => CompletionItemKind.Class,
            SabakaSymbolKind.Parameter => CompletionItemKind.Variable,
            SabakaSymbolKind.Method => CompletionItemKind.Method,
            SabakaSymbolKind.Field => CompletionItemKind.Field,
            _ => CompletionItemKind.Text
        };
    }

    private string GetPrettyType(string type)
    {
        return type switch
        {
            "IntKeyword" => "int",
            "FloatKeyword" => "float",
            "StringKeyword" => "string",
            "BoolKeyword" => "bool",
            "VoidKeyword" => "void",
            _ => type
        };
    }

    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken cancellationToken)
    {
        return Task.FromResult(request);
    }
}
