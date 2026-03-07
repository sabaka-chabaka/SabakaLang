using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SabakaLang.LSP.Analysis;
using System.IO;
using SabakaSymbolKind = SabakaLang.LSP.Analysis.SymbolKind;

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

    private static readonly string[] Keywords =
    {
        "if", "else", "while", "for", "foreach", "in", "func", "return", "void",
        "int", "float", "string", "bool", "true", "false",
        "struct", "class", "interface", "new", "override", "super",
        "switch", "case", "default", "enum",
        "public", "private", "protected", "import", "as"
    };

    private static readonly string[] BuiltIns =
    {
        "print", "input", "sleep",
        "readFile", "writeFile", "appendFile", "fileExists", "deleteFile", "readLines",
        "time", "timeMs", "httpGet", "httpPost", "httpPostJson", "ord", "chr"
    };

    protected override CompletionRegistrationOptions CreateRegistrationOptions(
        CompletionCapability cap, ClientCapabilities cc) => new()
    {
        DocumentSelector = new TextDocumentSelector(
            new TextDocumentFilter { Pattern = "**/*.sabaka" },
            new TextDocumentFilter { Language = "sabaka" }
        ),
        TriggerCharacters = new Container<string>(".", ":"),
        ResolveProvider = false
    };

    public override Task<CompletionList> Handle(CompletionParams request,
        CancellationToken cancellationToken)
    {
        var source = _documentStore.GetDocument(request.TextDocument.Uri);
        if (source == null) return Task.FromResult(new CompletionList());

        int offset = PositionHelper.GetOffset(source, request.Position);

        // ── dot completion: something.| ───────────────────────────────────────
        if (offset > 0 && source[offset - 1] == '.')
        {
            string target = ExtractWordBefore(source, offset - 1);
            if (!string.IsNullOrEmpty(target))
            {
                // 1. Is it a module alias? (import X as alias)
                var moduleSymbols = _symbolIndex.GetModuleMembers(
                    request.TextDocument.Uri, target.ToLower());
                var moduleList = moduleSymbols.ToList();
                if (moduleList.Count > 0)
                    return Task.FromResult(new CompletionList(
                        moduleList.Select(s => MakeItem(s, showFile: true))));

                // 2. Is it a class/struct/object type?
                var sym = _symbolIndex
                    .GetAvailableSymbols(request.TextDocument.Uri, offset - target.Length - 1)
                    .FirstOrDefault(s => s.Name == target);

                string typeName = sym != null
                    ? (sym.Kind == SabakaSymbolKind.Class ? sym.Name : sym.Type)
                    : target; // fallback: assume it IS the type name

                typeName = NormalizeType(typeName);

                var members = _symbolIndex.GetMembers(typeName).ToList();
                if (members.Count > 0)
                    return Task.FromResult(new CompletionList(
                        members.Select(s => MakeItem(s, showFile: true))));
            }

            // Nothing found — empty list (don't show general completions after dot)
            return Task.FromResult(new CompletionList());
        }

        // ── general completion ─────────────────────────────────────────────────
        var items = new List<CompletionItem>();

        items.AddRange(Keywords.Select(k => new CompletionItem
        {
            Label  = k,
            Kind   = CompletionItemKind.Keyword,
            Detail = "keyword"
        }));

        items.AddRange(BuiltIns.Select(f => new CompletionItem
        {
            Label  = f,
            Kind   = CompletionItemKind.Function,
            Detail = "built-in"
        }));

        var available = _symbolIndex.GetAvailableSymbols(request.TextDocument.Uri, offset);
        items.AddRange(available
            .Where(s => s.Kind != SabakaSymbolKind.BuiltIn)
            .Select(s => MakeItem(s,
                showFile: s.SourceFile != null &&
                          s.SourceFile != request.TextDocument.Uri.GetFileSystemPath())));

        return Task.FromResult(new CompletionList(items));
    }

    public override Task<CompletionItem> Handle(CompletionItem request,
        CancellationToken cancellationToken) => Task.FromResult(request);

    // ── helpers ───────────────────────────────────────────────────────────────

    private CompletionItem MakeItem(Symbol s, bool showFile)
    {
        string detail = s.Kind switch
        {
            SabakaSymbolKind.Function or
            SabakaSymbolKind.Method   => $"{NormalizeType(s.Type)} {s.Name}({s.Parameters ?? ""})",
            SabakaSymbolKind.Module   => $"module {s.Name}",
            SabakaSymbolKind.Class    => $"class {s.Name}",
            _                        => NormalizeType(s.Type)
        };

        string? doc = (showFile && s.SourceFile != null)
            ? $"From: {Path.GetFileName(s.SourceFile)}"
            : null;

        return new CompletionItem
        {
            Label         = s.Name,
            Kind          = MapKind(s.Kind),
            Detail        = detail,
            Documentation = doc != null
                ? new StringOrMarkupContent(new MarkupContent
                    { Kind = MarkupKind.Markdown, Value = doc })
                : null
        };
    }

    private static string ExtractWordBefore(string source, int beforeIndex)
    {
        int end = beforeIndex - 1;
        while (end >= 0 && char.IsWhiteSpace(source[end])) end--;
        int start = end;
        while (start >= 0 && (char.IsLetterOrDigit(source[start]) || source[start] == '_'))
            start--;
        if (end < start) return "";
        return source.Substring(start + 1, end - start);
    }

    private static string NormalizeType(string t) => t switch
    {
        "IntKeyword"    => "int",
        "FloatKeyword"  => "float",
        "StringKeyword" => "string",
        "BoolKeyword"   => "bool",
        "VoidKeyword"   => "void",
        _               => t
    };

    private static CompletionItemKind MapKind(SabakaSymbolKind k) => k switch
    {
        SabakaSymbolKind.Variable  => CompletionItemKind.Variable,
        SabakaSymbolKind.Function  => CompletionItemKind.Function,
        SabakaSymbolKind.Class     => CompletionItemKind.Class,
        SabakaSymbolKind.Parameter => CompletionItemKind.Variable,
        SabakaSymbolKind.Method    => CompletionItemKind.Method,
        SabakaSymbolKind.Field     => CompletionItemKind.Field,
        SabakaSymbolKind.Module    => CompletionItemKind.Module,
        _                         => CompletionItemKind.Text
    };
}