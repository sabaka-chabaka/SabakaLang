using System.Text;
using System.Text.Json.Nodes;
using SabakaLang.Binder;
using SabakaLang.Extensions;
using SabakaLang.Lexer.Enums;

namespace SabakaLang.LanguageServer;

public static class Signature
{
    private sealed record Sig(string Label, List<(int Start, int End)> Params, bool Variadic);

    private static Sig Build(string ret, string name, IEnumerable<(string Type, string Name)> ps, bool variadic)
    {
        var list = ps.ToList();
        var sb = new StringBuilder(ret.Length == 0 ? name + "(" : $"{ret} {name}(");
        var spans = new List<(int, int)>();
        for (var i = 0; i < list.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            var s = sb.Length;
            sb.Append(list[i].Type);
            if (variadic && i == list.Count - 1) sb.Append("...");
            sb.Append(' ').Append(list[i].Name);
            spans.Add((s, sb.Length));
        }
        sb.Append(')');
        return new Sig(sb.ToString(), spans, variadic);
    }

    private static Sig FromFn(FunctionSymbol f) =>
        Build(f.IsConstructor ? "" : f.ReturnType.ToString(), f.Name,
            f.Parameters.Where(p => p.Name != "this").Select(p => (p.Type.ToString(), p.Name)), f.IsVariadic);

    private static Sig FromNative(NativeSignature s) =>
        Build(s.ReturnType, s.Name, s.Parameters.Select(p => (p.Type, p.Name)), s.IsVariadic);

    private static IEnumerable<Sig> FromSymbol(Symbol s)
    {
        switch (s)
        {
            case FunctionSymbol f: yield return FromFn(f); break;
            case FunctionGroupSymbol g:
                foreach (var o in g.Overloads) yield return FromFn(o);
                break;
            case TypeSymbol { Constructor: { } c }: yield return FromFn(c); break;
        }
    }

    private static IEnumerable<Sig> ByName(Analysis a, string name, bool isMember, bool isNew)
    {
        if (a.Bind is null) yield break;
        var types = a.Bind.AllSymbols.OfType<TypeSymbol>().ToList();

        if (isNew)
        {
            foreach (var t in types.Where(t => t.Name == name))
                foreach (var s in FromSymbol(t)) yield return s;
            yield break;
        }

        if (!isMember)
        {
            foreach (var f in a.Bind.AllSymbols.OfType<FunctionSymbol>().Where(f => f.Name == name && f.Owner is null))
                yield return FromFn(f);
            foreach (var m in CompletionProvider.VisibleModules(a))
                foreach (var nf in m.Functions.Where(nf => nf.Name == name))
                    yield return FromNative(nf.Signature);
            yield break;
        }

        foreach (var t in types)
            if (t.Members.TryGetValue(name, out var member))
                foreach (var s in FromSymbol(member)) yield return s;
        foreach (var m in CompletionProvider.VisibleModules(a))
            foreach (var ext in m.Extensions.Where(e => e.Name == name))
                yield return FromNative(ext.Signature);
    }

    public static JsonNode? Help(Analysis a, int offset)
    {
        var code = a.Root.Code;
        var i = Locator.LowerBound(code, offset) - 1;
        int depth = 0, commas = 0, open = -1;
        for (; i >= 0; i--)
        {
            var t = code[i].Type;
            if (t is TokenType.RParen or TokenType.RBracket or TokenType.RBrace) depth++;
            else if (t == TokenType.LParen) { if (depth == 0) { open = i; break; } depth--; }
            else if (t is TokenType.LBracket or TokenType.LBrace) { if (depth == 0) return null; depth--; }
            else if (t == TokenType.Semicolon && depth == 0) return null;
            else if (t == TokenType.Comma && depth == 0) commas++;
        }
        if (open <= 0 || code[open - 1].Type != TokenType.Identifier) return null;

        var calleeIdx = open - 1;
        var callee = code[calleeIdx];
        var isMember = calleeIdx > 0 && code[calleeIdx - 1].Type == TokenType.Dot;
        var isNew = calleeIdx > 0 && code[calleeIdx - 1].Type == TokenType.New;

        var sigs = new List<Sig>();
        if (a.RefAtStart(callee.Start.Offset)?.Symbol is { } sym) sigs.AddRange(FromSymbol(sym));
        if (sigs.Count == 0) sigs.AddRange(ByName(a, callee.Value, isMember, isNew));
        sigs = sigs.DistinctBy(s => s.Label).ToList();
        if (sigs.Count == 0) return null;

        var active = sigs.FindIndex(s => s.Variadic || s.Params.Count > commas);
        var arr = new JsonArray();
        foreach (var s in sigs)
        {
            var ps = new JsonArray();
            foreach (var (st, en) in s.Params)
                ps.Add(new JsonObject { ["label"] = new JsonArray(st, en) });
            arr.Add(new JsonObject { ["label"] = s.Label, ["parameters"] = ps });
        }
        return new JsonObject
        {
            ["signatures"] = arr,
            ["activeSignature"] = Math.Max(active, 0),
            ["activeParameter"] = commas
        };
    }
}
