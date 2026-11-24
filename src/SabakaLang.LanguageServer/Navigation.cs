using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SabakaLang.Binder;

namespace SabakaLang.LanguageServer;

public static class Navigation
{
    private static readonly string[] Reserved =
    [
        "bool", "char", "true", "false", "if", "else", "while", "for", "foreach", "in", "int", "float", "string",
        "void", "return", "struct", "enum", "class", "interface", "new", "override", "super", "null", "is",
        "switch", "case", "default", "public", "private", "protected", "import", "const", "break", "continue"
    ];

    private static Symbol Norm(Symbol s)
    {
        if (s is FunctionSymbol { IsConstructor: true, Owner: { } owner }) return owner;
        if (s is FunctionGroupSymbol g && g.Overloads.Count > 0) return g.Overloads[0];
        return s;
    }

    private static string? Key(Symbol? s)
    {
        if (s is null) return null;
        s = Norm(s);
        if (s.IsNative || s.Span.Start.Line <= 0) return null;
        return $"{s.Module}|{(int)s.Kind}|{s.Name}|{s.Span.Start.Offset}";
    }

    public static List<Analysis> Scope(Analyzer analyzer, IEnumerable<Analysis> open, Symbol target)
    {
        var list = open.ToList();
        var t = Norm(target);
        foreach (var a in list.ToList())
        {
            var m = a.Modules.FirstOrDefault(x => x.Name == t.Module);
            if (m is null) continue;
            if (list.Any(x => string.Equals(Paths.ToPath(x.Uri), m.Id, StringComparison.OrdinalIgnoreCase))) break;
            if (!File.Exists(m.Id)) break;
            try { list.Add(analyzer.Analyze(Paths.ToUri(m.Id), File.ReadAllText(m.Id))); } catch { }
            break;
        }
        return list;
    }

    private static List<(Analysis A, Ref R)> Collect(IEnumerable<Analysis> scope, Symbol target, bool includeDecl)
    {
        var result = new List<(Analysis, Ref)>();
        var key = Key(target);
        if (key is null) return result;
        foreach (var a in scope)
            foreach (var r in a.Refs)
            {
                if (r.Symbol is null || (!includeDecl && r.IsDecl)) continue;
                if (Key(r.Symbol) == key) result.Add((a, r));
            }
        return result;
    }

    public static JsonArray References(IEnumerable<Analysis> scope, Symbol? target, bool includeDecl)
    {
        var arr = new JsonArray();
        if (target is null) return arr;
        foreach (var (a, r) in Collect(scope, target, includeDecl))
            arr.Add(new JsonObject { ["uri"] = a.Uri, ["range"] = Features.Range(a, r.Start, r.End) });
        return arr;
    }

    public static JsonArray Highlights(Analysis a, int offset)
    {
        var arr = new JsonArray();
        var key = Key(a.RefAt(offset)?.Symbol);
        if (key is null) return arr;
        foreach (var r in a.Refs)
            if (Key(r.Symbol) == key)
                arr.Add(new JsonObject { ["range"] = Features.Range(a, r.Start, r.End), ["kind"] = r.IsDecl ? 3 : 2 });
        return arr;
    }

    public static JsonNode? PrepareRename(Analysis a, int offset)
    {
        var r = a.RefAt(offset);
        if (r?.Symbol is null) return null;
        var s = Norm(r.Symbol);
        if (Key(s) is null || s.Kind is SymbolKind.Module or SymbolKind.TypeParam) return null;
        if (!a.Modules.Any(m => m.Name == s.Module)) return null;
        return new JsonObject
        {
            ["range"] = Features.Range(a, r.Start, r.End),
            ["placeholder"] = a.Text.Substring(r.Start, r.Length)
        };
    }

    public static JsonNode Rename(IEnumerable<Analysis> scope, Symbol target, string newName)
    {
        if (!Regex.IsMatch(newName, "^[A-Za-z_][A-Za-z0-9_]*$") || Reserved.Contains(newName))
            throw new InvalidOperationException($"'{newName}' is not a valid identifier.");

        var changes = new JsonObject();
        foreach (var group in Collect(scope, target, true).GroupBy(x => x.A.Uri))
        {
            var edits = new JsonArray();
            foreach (var (a, r) in group.DistinctBy(x => x.R.Start).OrderBy(x => x.R.Start))
                edits.Add(new JsonObject { ["range"] = Features.Range(a, r.Start, r.End), ["newText"] = newName });
            changes[group.Key] = edits;
        }
        return new JsonObject { ["changes"] = changes };
    }
}
