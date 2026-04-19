using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SabakaLang.Compiler;
using SymbolKind = SabakaLang.Compiler.SymbolKind;

namespace SabakaLang.LanguageServer.Handlers;

public sealed class CompletionHandler(DocumentStore store) : CompletionHandlerBase
{
    private static readonly string[] Keywords =
    [
        "if", "else", "while", "for", "foreach", "in", "return",
        "class", "interface", "struct", "enum", "import", "new",
        "override", "super", "null", "is", "switch", "case", "default",
        "true", "false", "public", "private", "protected",
        "int", "float", "bool", "string", "void"
    ];

    protected override CompletionRegistrationOptions CreateRegistrationOptions(
        CompletionCapability capability,
        ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = SabakaDocumentSelector.Instance,
            TriggerCharacters = new Container<string>(".", ":"),
            ResolveProvider = false
        };

    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken ct)
    {
        var analysis = store.Get(request.TextDocument.Uri.ToString());
        if (analysis is null)
            return Task.FromResult(new CompletionList());

        var items = new List<CompletionItem>();
        var source = analysis.Source;
        var offset = PositionHelper.ToOffset(source, request.Position);

        if (IsMemberAccess(source, offset, out var memberPrefix))
        {
            items.AddRange(GetMemberCompletions(analysis, memberPrefix));
            return Task.FromResult(new CompletionList(items));
        }

        if (IsStaticAccess(source, offset, out var staticPrefix))
        {
            items.AddRange(GetStaticCompletions(analysis, staticPrefix));
            return Task.FromResult(new CompletionList(items));
        }

        items.AddRange(GetKeywordCompletions());
        items.AddRange(GetSymbolCompletions(analysis));

        return Task.FromResult(new CompletionList(items));
    }

    private static bool IsMemberAccess(string source, int offset, out string prefix)
    {
        prefix = "";
        var pos = offset - 1;
        while (pos >= 0 && char.IsWhiteSpace(source[pos])) pos--;
        if (pos < 0 || source[pos] != '.') return false;
        pos--;
        var end = pos + 1;
        while (pos >= 0 && (char.IsLetterOrDigit(source[pos]) || source[pos] == '_')) pos--;
        prefix = source[(pos + 1)..end];
        return prefix.Length > 0;
    }

    private static bool IsStaticAccess(string source, int offset, out string prefix)
    {
        prefix = "";
        var pos = offset - 1;
        while (pos >= 0 && char.IsWhiteSpace(source[pos])) pos--;
        if (pos < 1 || source[pos] != ':' || source[pos - 1] != ':') return false;
        pos -= 2;
        var end = pos + 1;
        while (pos >= 0 && (char.IsLetterOrDigit(source[pos]) || source[pos] == '_')) pos--;
        prefix = source[(pos + 1)..end];
        return prefix.Length > 0;
    }

    private static IEnumerable<CompletionItem> GetMemberCompletions(
        DocumentAnalysis analysis, string objectName)
    {
        var sym = analysis.Bind.Symbols.Lookup(objectName).FirstOrDefault();
        var typeName = sym?.Type ?? objectName;
        var baseType = typeName.TrimEnd('[', ']');

        foreach (var member in analysis.Bind.Symbols.MembersOf(baseType))
        {
            yield return MakeSymbolItem(member);
        }

        if (baseType == "string")
        {
            foreach (var m in StringBuiltins())
                yield return m;
        }

        if (typeName.EndsWith("[]"))
        {
            yield return new CompletionItem { Label = "length", Kind = CompletionItemKind.Property, Detail = "int" };
            yield return new CompletionItem { Label = "push", Kind = CompletionItemKind.Method, Detail = "void push(value)" };
            yield return new CompletionItem { Label = "pop", Kind = CompletionItemKind.Method, Detail = "T pop()" };
            yield return new CompletionItem { Label = "contains", Kind = CompletionItemKind.Method, Detail = "bool contains(value)" };
            yield return new CompletionItem { Label = "indexOf", Kind = CompletionItemKind.Method, Detail = "int indexOf(value)" };
        }
    }

    private static IEnumerable<CompletionItem> GetStaticCompletions(
        DocumentAnalysis analysis, string typeName)
    {
        foreach (var member in analysis.Bind.Symbols.MembersOf(typeName)
                     .Where(s => s.Kind == SymbolKind.EnumMember))
        {
            yield return new CompletionItem
            {
                Label = member.Name,
                Kind = CompletionItemKind.EnumMember,
                Detail = $"{typeName}.{member.Name}",
                Documentation = new StringOrMarkupContent(new MarkupContent
                {
                    Kind = MarkupKind.Markdown,
                    Value = $"**Enum member** `{typeName}::{member.Name}`"
                })
            };
        }
    }

    private static IEnumerable<CompletionItem> GetKeywordCompletions() =>
        Keywords.Select(kw => new CompletionItem
        {
            Label = kw,
            Kind = CompletionItemKind.Keyword,
            SortText = $"z_{kw}"
        });

    private static IEnumerable<CompletionItem> GetSymbolCompletions(DocumentAnalysis analysis)
    {
        var seen = new HashSet<string>();
        foreach (var sym in analysis.Bind.Symbols.All)
        {
            if (sym.ParentName is not null &&
                sym.Kind is SymbolKind.Field or SymbolKind.Method or SymbolKind.EnumMember)
                continue;

            if (!seen.Add(sym.Name)) continue;
            yield return MakeSymbolItem(sym);
        }
    }

    private static CompletionItem MakeSymbolItem(Symbol sym)
    {
        var kind = sym.Kind switch
        {
            SymbolKind.Function  => CompletionItemKind.Function,
            SymbolKind.Method    => CompletionItemKind.Method,
            SymbolKind.BuiltIn   => CompletionItemKind.Function,
            SymbolKind.Class     => CompletionItemKind.Class,
            SymbolKind.Interface => CompletionItemKind.Interface,
            SymbolKind.Struct    => CompletionItemKind.Struct,
            SymbolKind.Enum      => CompletionItemKind.Enum,
            SymbolKind.EnumMember=> CompletionItemKind.EnumMember,
            SymbolKind.Field     => CompletionItemKind.Field,
            SymbolKind.Parameter => CompletionItemKind.Variable,
            SymbolKind.Variable  => CompletionItemKind.Variable,
            SymbolKind.TypeParam => CompletionItemKind.TypeParameter,
            SymbolKind.Module    => CompletionItemKind.Module,
            _                    => CompletionItemKind.Text
        };

        var detail = sym.Kind is SymbolKind.Function or SymbolKind.Method or SymbolKind.BuiltIn
            ? $"{sym.Type} {sym.Name}({sym.Parameters ?? ""})"
            : $"{sym.Type} {sym.Name}";

        var insertText = sym.Kind is SymbolKind.Function or SymbolKind.Method or SymbolKind.BuiltIn
            ? $"{sym.Name}($0)"
            : sym.Name;

        return new CompletionItem
        {
            Label = sym.Name,
            Kind = kind,
            Detail = detail,
            InsertText = insertText,
            InsertTextFormat = sym.Kind is SymbolKind.Function or SymbolKind.Method or SymbolKind.BuiltIn
                ? InsertTextFormat.Snippet
                : InsertTextFormat.PlainText,
            SortText = $"a_{sym.Name}"
        };
    }

    private static IEnumerable<CompletionItem> StringBuiltins()
    {
        var methods = new[]
        {
            ("length",     "int",    ""),
            ("toUpper",    "string", ""),
            ("toLower",    "string", ""),
            ("trim",       "string", ""),
            ("trimStart",  "string", ""),
            ("trimEnd",    "string", ""),
            ("contains",   "bool",   "string value"),
            ("startsWith", "bool",   "string prefix"),
            ("endsWith",   "bool",   "string suffix"),
            ("indexOf",    "int",    "string value"),
            ("replace",    "string", "string old, string new"),
            ("split",      "string[]","string separator"),
            ("substring",  "string", "int start, int length"),
            ("charAt",     "string", "int index"),
            ("toInt",      "int",    ""),
            ("toFloat",    "float",  ""),
        };

        foreach (var (name, ret, parms) in methods)
        {
            yield return new CompletionItem
            {
                Label = name,
                Kind = name == "length" ? CompletionItemKind.Property : CompletionItemKind.Method,
                Detail = $"{ret} {name}({parms})",
                InsertText = parms.Length > 0 ? $"{name}($0)" : name,
                InsertTextFormat = parms.Length > 0 ? InsertTextFormat.Snippet : InsertTextFormat.PlainText
            };
        }
    }

    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken ct) =>
        Task.FromResult(request);
}