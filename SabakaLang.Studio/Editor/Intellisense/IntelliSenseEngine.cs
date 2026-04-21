using SabakaLang.Compiler;
using SabakaLang.LanguageServer;
using SabakaLang.Studio.Editor.Core;

namespace SabakaLang.Studio.Editor.Intellisense;

public sealed class CompletionItem
{
    public string Label      { get; set; } = "";
    public string Detail     { get; set; } = "";
    public string InsertText { get; set; } = "";
    public string KindIcon   { get; set; } = "·";
}

public sealed class CompletionState
{
    private List<CompletionItem> _items = [];
    private int _index;

    public bool IsVisible { get; private set; }
    public IReadOnlyList<CompletionItem> Items => _items;
    public CompletionItem? Selected => IsVisible && _items.Count > 0 ? _items[_index] : null;
    public int SelectedIndex => _index;

    public (float X, float Y) PopupHint { get; private set; }

    public event Action? Changed;

    public void Show(List<CompletionItem> items, float x, float y)
    {
        _items     = items;
        _index     = 0;
        IsVisible  = items.Count > 0;
        PopupHint  = (x, y);
        Changed?.Invoke();
    }

    public void Hide()  { IsVisible = false; Changed?.Invoke(); }
    public void MoveDown() { if (_items.Count > 0) { _index = (_index + 1) % _items.Count; Changed?.Invoke(); } }
    public void MoveUp()   { if (_items.Count > 0) { _index = (_index - 1 + _items.Count) % _items.Count; Changed?.Invoke(); } }

    public void FilterBy(string prefix)
    {
        if (!IsVisible) return;
    }
}

public sealed class SignatureState
{
    public bool   IsVisible  { get; private set; }
    public string Label      { get; private set; } = "";
    public int    ActiveParam{ get; private set; }
    public List<string> Params { get; private set; } = [];

    public event Action? Changed;

    public void Show(string label, List<string> parms, int activeParam)
    {
        Label       = label;
        Params      = parms;
        ActiveParam = activeParam;
        IsVisible   = true;
        Changed?.Invoke();
    }

    public void Hide() { IsVisible = false; Changed?.Invoke(); }
}

public sealed class HoverState
{
    public bool   IsVisible { get; private set; }
    public string Markdown  { get; private set; } = "";
    public (float X, float Y) Position { get; private set; }

    public event Action? Changed;

    public void Show(string md, float x, float y)
    {
        Markdown   = md;
        Position   = (x, y);
        IsVisible  = true;
        Changed?.Invoke();
    }

    public void Hide() { IsVisible = false; Changed?.Invoke(); }
}

public sealed class IntelliSenseEngine
{
    private readonly DocumentStore _store;

    private static readonly string[] Keywords =
    [
        "if","else","while","for","foreach","in","return","class","interface",
        "struct","enum","import","new","override","super","null","is","switch",
        "case","default","true","false","public","private","protected",
        "int","float","bool","string","void"
    ];

    public IntelliSenseEngine(DocumentStore store) => _store = store;

    public List<CompletionItem> GetCompletions(string uri, TextPosition caret)
    {
        var analysis = _store.Get(uri);
        if (analysis is null) return [];

        var source = analysis.Source;
        var offset = ToOffset(source, caret);
        var result = new List<CompletionItem>();

        if (IsMemberAccess(source, offset, out var memberOf))
        {
            AddMemberCompletions(result, analysis, memberOf);
            return result;
        }
        if (IsStaticAccess(source, offset, out var staticOf))
        {
            AddStaticCompletions(result, analysis, staticOf);
            return result;
        }

        var prefix = CurrentWord(source, offset);
        AddKeywordCompletions(result, prefix);
        AddSymbolCompletions(result, analysis, prefix);
        return result;
    }

    public string? GetHover(string uri, TextPosition caret)
    {
        var analysis = _store.Get(uri);
        if (analysis is null) return null;
        var word = WordAt(analysis.Source, caret);
        if (word is null) return null;
        var sym  = analysis.Bind.Symbols.Lookup(word).FirstOrDefault();
        return sym is null ? null : FormatSymbol(sym);
    }

    public (string label, List<string> parms, int activeParam)? GetSignature(string uri, TextPosition caret)
    {
        var analysis = _store.Get(uri);
        if (analysis is null) return null;
        var source = analysis.Source;
        var offset = ToOffset(source, caret);
        if (!TryCallContext(source, offset, out var name, out var arg)) return null;
        var sym  = analysis.Bind.Symbols.Lookup(name)
            .FirstOrDefault(s => s.Kind is SymbolKind.Function or SymbolKind.Method or SymbolKind.BuiltIn);
        if (sym is null) return null;
        var raw   = sym.Parameters ?? "";
        var parms = raw.Length > 0 ? raw.Split(',').Select(p => p.Trim()).ToList() : [];
        var label = $"{sym.Name}({string.Join(", ", parms)}) : {sym.Type}";
        return (label, parms, arg);
    }

    public TextPosition? GetDefinition(string uri, TextPosition caret)
    {
        var analysis = _store.Get(uri);
        if (analysis is null) return null;
        var word = WordAt(analysis.Source, caret);
        if (word is null) return null;
        var sym  = analysis.Bind.Symbols.Lookup(word)
            .FirstOrDefault(s => s.Kind is not SymbolKind.BuiltIn && s.Span.Start.Offset > 0);
        if (sym is null) return null;
        return new(sym.Span.Start.Line - 1, sym.Span.Start.Column - 1);
    }

    public List<TextPosition> FindReferences(string uri, TextPosition caret)
    {
        var analysis = _store.Get(uri);
        if (analysis is null) return [];
        var word = WordAt(analysis.Source, caret);
        if (word is null) return [];
        return analysis.Source.IndexOf_All(word);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void AddKeywordCompletions(List<CompletionItem> result, string prefix)
    {
        foreach (var kw in Keywords)
        {
            if (prefix.Length > 0 && !kw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(new CompletionItem { Label = kw, Detail = "keyword", InsertText = kw, KindIcon = "K" });
        }
    }

    private static void AddSymbolCompletions(List<CompletionItem> result, DocumentAnalysis a, string prefix)
    {
        var seen = new HashSet<string>();
        foreach (var sym in a.Bind.Symbols.All)
        {
            if (sym.ParentName is not null &&
                sym.Kind is SymbolKind.Field or SymbolKind.Method or SymbolKind.EnumMember)
                continue;
            if (!seen.Add(sym.Name)) continue;
            if (prefix.Length > 0 && !sym.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(MakeItem(sym));
        }
    }

    private static void AddMemberCompletions(List<CompletionItem> result, DocumentAnalysis a, string objName)
    {
        var sym      = a.Bind.Symbols.Lookup(objName).FirstOrDefault();
        var typeName = sym?.Type ?? objName;
        var baseType = typeName.TrimEnd('[', ']');

        foreach (var m in a.Bind.Symbols.MembersOf(baseType))
            result.Add(MakeItem(m));

        if (baseType == "string")
        {
            foreach (var (name, ret, parms) in StringBuiltins())
                result.Add(new CompletionItem
                {
                    Label = name, Detail = $"{ret} {name}({parms})",
                    InsertText = parms.Length > 0 ? $"{name}(${{0}})" : name,
                    KindIcon = parms.Length > 0 ? "M" : "P"
                });
        }

        if (typeName.EndsWith("[]"))
        {
            result.Add(new CompletionItem { Label = "length", Detail = "int", InsertText = "length", KindIcon = "P" });
            result.Add(new CompletionItem { Label = "push",   Detail = "void push(value)", InsertText = "push(${0})", KindIcon = "M" });
            result.Add(new CompletionItem { Label = "pop",    Detail = "T pop()",          InsertText = "pop()", KindIcon = "M" });
        }
    }

    private static void AddStaticCompletions(List<CompletionItem> result, DocumentAnalysis a, string typeName)
    {
        foreach (var m in a.Bind.Symbols.MembersOf(typeName).Where(s => s.Kind == SymbolKind.EnumMember))
            result.Add(new CompletionItem { Label = m.Name, Detail = $"{typeName}::{m.Name}", InsertText = m.Name, KindIcon = "E" });
    }

    private static CompletionItem MakeItem(Symbol sym)
    {
        var icon = sym.Kind switch
        {
            SymbolKind.Function  => "F", SymbolKind.Method => "M", SymbolKind.BuiltIn => "F",
            SymbolKind.Class     => "C", SymbolKind.Interface => "I", SymbolKind.Struct => "S",
            SymbolKind.Enum      => "E", SymbolKind.EnumMember => "E",
            SymbolKind.Field     => "P", SymbolKind.Parameter => "p", SymbolKind.Module => "N",
            _                    => "v"
        };
        var detail = sym.Kind is SymbolKind.Function or SymbolKind.Method or SymbolKind.BuiltIn
            ? $"{sym.Type} {sym.Name}({sym.Parameters ?? ""})"
            : $"{sym.Type} {sym.Name}";
        var insert = sym.Kind is SymbolKind.Function or SymbolKind.Method or SymbolKind.BuiltIn
            ? $"{sym.Name}(${{0}})" : sym.Name;

        return new CompletionItem { Label = sym.Name, Detail = detail, InsertText = insert, KindIcon = icon };
    }

    private static string FormatSymbol(Symbol sym)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("```sabaka");
        switch (sym.Kind)
        {
            case SymbolKind.Function: case SymbolKind.Method: case SymbolKind.BuiltIn:
                sb.AppendLine($"{sym.Type} {sym.Name}({sym.Parameters ?? ""})"); break;
            case SymbolKind.Class:     sb.AppendLine($"class {sym.Name}"); break;
            case SymbolKind.Interface: sb.AppendLine($"interface {sym.Name}"); break;
            case SymbolKind.Struct:    sb.AppendLine($"struct {sym.Name}"); break;
            case SymbolKind.Enum:      sb.AppendLine($"enum {sym.Name}"); break;
            default:                   sb.AppendLine($"{sym.Type} {sym.Name}"); break;
        }
        sb.AppendLine("```");
        sb.Append($"**{sym.Kind}** `{sym.Name}`");
        if (sym.ParentName is not null) sb.Append($" in `{sym.ParentName}`");
        return sb.ToString();
    }
    
    private static int ToOffset(string src, TextPosition p)
    {
        var off = 0; var ln = 0;
        while (off < src.Length && ln < p.Row)
            if (src[off++] == '\n') ln++;
        var col = 0;
        while (off < src.Length && col < p.Col && src[off] != '\n') { off++; col++; }
        return off;
    }

    private static string? WordAt(string src, TextPosition p)
    {
        var off = ToOffset(src, p);
        if (off >= src.Length) return null;
        static bool IsId(char c) => char.IsLetterOrDigit(c) || c == '_';
        var s = off; while (s > 0 && IsId(src[s - 1])) s--;
        var e = off; while (e < src.Length && IsId(src[e])) e++;
        return s == e ? null : src[s..e];
    }

    private static string CurrentWord(string src, int offset)
    {
        var s = offset;
        static bool IsId(char c) => char.IsLetterOrDigit(c) || c == '_';
        while (s > 0 && IsId(src[s - 1])) s--;
        return src[s..offset];
    }

    private static bool IsMemberAccess(string src, int offset, out string prefix)
    {
        prefix = "";
        var p = offset - 1;
        while (p >= 0 && char.IsWhiteSpace(src[p])) p--;
        if (p < 0 || src[p] != '.') return false;
        p--;
        var end = p + 1;
        while (p >= 0 && (char.IsLetterOrDigit(src[p]) || src[p] == '_')) p--;
        prefix = src[(p + 1)..end];
        return prefix.Length > 0;
    }

    private static bool IsStaticAccess(string src, int offset, out string prefix)
    {
        prefix = "";
        var p = offset - 1;
        while (p >= 0 && char.IsWhiteSpace(src[p])) p--;
        if (p < 1 || src[p] != ':' || src[p - 1] != ':') return false;
        p -= 2;
        var end = p + 1;
        while (p >= 0 && (char.IsLetterOrDigit(src[p]) || src[p] == '_')) p--;
        prefix = src[(p + 1)..end];
        return prefix.Length > 0;
    }

    private static bool TryCallContext(string src, int offset, out string name, out int argIdx)
    {
        name = ""; argIdx = 0;
        var depth = 0; var commas = 0; var p = offset - 1;
        while (p >= 0)
        {
            switch (src[p])
            {
                case ')': case ']': depth++; break;
                case '[':           depth--; break;
                case '(':
                    if (depth > 0) { depth--; break; }
                    argIdx = commas;
                    var ne = p - 1;
                    while (ne >= 0 && char.IsWhiteSpace(src[ne])) ne--;
                    var ns = ne;
                    while (ns > 0 && (char.IsLetterOrDigit(src[ns - 1]) || src[ns - 1] == '_')) ns--;
                    name = ns <= ne ? src[ns..(ne + 1)] : "";
                    return name.Length > 0;
                case ',' when depth == 0: commas++; break;
            }
            p--;
        }
        return false;
    }

    private static (string name, string ret, string parms)[] StringBuiltins() =>
    [
        ("length","int",""), ("toUpper","string",""), ("toLower","string",""),
        ("trim","string",""), ("contains","bool","string value"),
        ("startsWith","bool","string prefix"), ("endsWith","bool","string suffix"),
        ("indexOf","int","string value"), ("replace","string","string old, string rep"),
        ("split","string[]","string sep"), ("substring","string","int start, int len"),
        ("charAt","string","int index"), ("toInt","int",""), ("toFloat","float",""),
    ];
}

public static class StringExtensions
{
    public static List<TextPosition> IndexOf_All(this string src, string needle)
    {
        var result = new List<TextPosition>();
        var row = 0; var col = 0;
        for (var i = 0; i <= src.Length - needle.Length; i++)
        {
            if (src[i] == '\n') { row++; col = 0; continue; }
            if (src.AsSpan(i).StartsWith(needle, StringComparison.Ordinal))
                result.Add(new(row, col));
            col++;
        }
        return result;
    }
}