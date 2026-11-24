using System.Text.RegularExpressions;
using SabakaLang.AST;
using SabakaLang.AST.Interfaces;
using SabakaLang.Binder;
using SabakaLang.Lexer.Enums;
using SabakaLang.Lexer.Structures;

namespace SabakaLang.LanguageServer;

public sealed record Ref(int Start, int Length, Symbol? Symbol, string? Kind, bool IsDecl)
{
    public int End => Start + Length;
}

public enum NameKind { Type, Function, EnumMember, Variable }

public static class Locator
{
    public static int LowerBound(IReadOnlyList<Token> code, int offset)
    {
        int lo = 0, hi = code.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (code[mid].Start.Offset < offset) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    public static int ChainLast(IReadOnlyList<Token> code, int i)
    {
        while (i + 2 < code.Count && code[i + 1].Type == TokenType.Dot && code[i + 2].Type == TokenType.Identifier) i += 2;
        return i;
    }

    public static int Find(IReadOnlyList<Token> code, string name, NameKind kind, int startOffset, int endOffset)
    {
        for (var i = LowerBound(code, startOffset); i < code.Count; i++)
        {
            var t = code[i];
            if (t.Type == TokenType.Eof) break;
            if (endOffset > startOffset && t.Start.Offset >= endOffset) break;
            if (t.Type != TokenType.Identifier || t.Value != name) continue;
            var prev = i > 0 ? code[i - 1].Type : TokenType.Eof;
            var next = i + 1 < code.Count ? code[i + 1].Type : TokenType.Eof;
            var ok = kind switch
            {
                NameKind.Type => prev is TokenType.Class or TokenType.Interface or TokenType.StructKeyword or TokenType.Enum,
                NameKind.Function => next is TokenType.LParen or TokenType.Less && prev is not TokenType.New and not TokenType.Dot,
                NameKind.EnumMember => prev is TokenType.LBrace or TokenType.Comma,
                _ => next is TokenType.Equal or TokenType.Semicolon or TokenType.Comma or TokenType.RParen
                    or TokenType.In or TokenType.RBrace or TokenType.Eof
            };
            if (ok) return i;
        }
        return -1;
    }

    public static NameKind KindOf(Symbol s) => s.Kind switch
    {
        SymbolKind.Class or SymbolKind.Interface or SymbolKind.Struct or SymbolKind.Enum => NameKind.Type,
        SymbolKind.Function => NameKind.Function,
        SymbolKind.EnumMember => NameKind.EnumMember,
        _ => NameKind.Variable
    };
}

internal sealed class RefBuilder : AstWalker
{
    private readonly Analysis _a;
    private readonly IReadOnlyList<Token> _code;
    private readonly SemanticModel _model;
    private readonly List<Ref> _refs = [];
    private readonly HashSet<int> _seen = [];
    private readonly Dictionary<string, TypeSymbol> _types = new();
    private static readonly Regex NewRegex = new(@"\Gnew\s+(?:[A-Za-z_]\w*\.)*([A-Za-z_]\w*)");

    private RefBuilder(Analysis a)
    {
        _a = a;
        _code = a.Root.Code;
        _model = a.Bind!.Model;
        foreach (var t in a.Bind.AllSymbols.OfType<TypeSymbol>().OrderBy(t => t.Module == a.Root.Name ? 0 : 1))
            _types.TryAdd(t.Name, t);
    }

    public static List<Ref> Build(Analysis a)
    {
        if (a.Bind is null) return [];
        var b = new RefBuilder(a);
        b.Declarations();
        b.WalkAll(a.Root.Statements);
        return b.Result();
    }

    private List<Ref> Result()
    {
        _refs.Sort((x, y) => x.Start.CompareTo(y.Start));
        return _refs;
    }

    private void Add(int start, string text, Symbol? sym, string? kind, bool decl)
    {
        if (start < 0 || start + text.Length > _a.Text.Length) return;
        if (string.CompareOrdinal(_a.Text, start, text, 0, text.Length) != 0) return;
        if (!_seen.Add(start)) return;
        _refs.Add(new Ref(start, text.Length, sym, kind, decl));
    }

    private void Declarations()
    {
        var all = new List<Symbol>(_a.Bind!.AllSymbols);
        foreach (var t in _a.Bind.AllSymbols.OfType<TypeSymbol>().ToList())
        {
            all.AddRange(t.Fields);
            all.AddRange(t.Methods);
            if (t.Constructor is not null) all.Add(t.Constructor);
            all.AddRange(t.Members.Values.OfType<EnumMemberSymbol>());
        }
        foreach (var s in all.Distinct())
        {
            if (s.IsNative || s.Module != _a.Root.Name || s.Span.Start.Line <= 0) continue;
            if (s.Name.StartsWith('<') || s.Name == "this") continue;
            if (s.Kind is SymbolKind.TypeParam or SymbolKind.Module or SymbolKind.FunctionGroup) continue;
            var idx = Locator.Find(_code, s.Name, Locator.KindOf(s), s.Span.Start.Offset, s.Span.End.Offset);
            if (idx >= 0) Add(_code[idx].Start.Offset, s.Name, s, null, true);
        }
    }

    protected override void OnStmt(IStmt s)
    {
        if (s is ClassDecl or InterfaceDecl) HeaderTypes(s.Span.Start.Offset);
    }

    private void HeaderTypes(int offset)
    {
        var i = Locator.LowerBound(_code, offset);
        while (i < _code.Count && _code[i].Type is not (TokenType.Colon or TokenType.LBrace or TokenType.Eof)) i++;
        if (i >= _code.Count || _code[i].Type != TokenType.Colon) return;
        for (i++; i < _code.Count && _code[i].Type is not (TokenType.LBrace or TokenType.Eof); i++)
            if (_code[i].Type == TokenType.Identifier)
            {
                i = Locator.ChainLast(_code, i);
                AddType(i);
            }
    }

    private void AddType(int idx)
    {
        var t = _code[idx];
        _types.TryGetValue(t.Value, out var sym);
        Add(t.Start.Offset, t.Value, sym, "type", false);
    }

    protected override void OnType(TypeRef t)
    {
        if (t.Span.Start.Line <= 0) return;
        var i = Locator.LowerBound(_code, t.Span.Start.Offset);
        if (i >= _code.Count || _code[i].Type != TokenType.Identifier) return;
        i = Locator.ChainLast(_code, i);
        AddType(i);
        if (i + 1 < _code.Count && _code[i + 1].Type == TokenType.Less)
        {
            var depth = 0;
            for (var j = i + 1; j < _code.Count; j++)
            {
                var tt = _code[j].Type;
                if (tt == TokenType.Less) depth++;
                else if (tt == TokenType.Greater) { if (--depth == 0) break; }
                else if (tt == TokenType.Identifier) AddType(j);
                else if (tt is not (TokenType.Comma or TokenType.Dot or TokenType.IntKeyword or TokenType.FloatKeyword
                         or TokenType.StringKeyword or TokenType.BoolKeyword or TokenType.CharKeyword
                         or TokenType.LBracket or TokenType.RBracket or TokenType.Question)) break;
            }
        }
    }

    protected override void OnExpr(IExpr e)
    {
        switch (e)
        {
            case NameExpr n:
                if (n.Name == "this") Add(n.Span.Start.Offset, "this", null, "keyword", false);
                else if (_model.Names.TryGetValue(n, out var ns)) Add(n.Span.Start.Offset, n.Name, ns, null, false);
                break;
            case CallExpr c:
                var target = _model.Calls.TryGetValue(c, out var cb) ? cb.Target : null;
                if (c.Callee is MemberExpr cm) AddMember(cm, target, true);
                else if (c.Callee is NameExpr cn && cn.Name != "this")
                {
                    Symbol? s = target;
                    if (s is null) _model.Names.TryGetValue(cn, out s);
                    if (s is not null) Add(cn.Span.Start.Offset, cn.Name, s, null, false);
                }
                break;
            case MemberExpr m: AddMember(m, null, false); break;
            case NewExpr nw:
                if (nw.Span.Start.Offset >= 0 && nw.Span.Start.Offset <= _a.Text.Length)
                {
                    var nm = NewRegex.Match(_a.Text, nw.Span.Start.Offset);
                    if (nm.Success)
                    {
                        Symbol? ts = _model.News.TryGetValue(nw, out var nb) ? nb.Type : null;
                        Add(nm.Groups[1].Index, nm.Groups[1].Value, ts, "class", false);
                    }
                }
                break;
        }
    }

    private int ScanLastWord(string word, int from, int to)
    {
        var text = _a.Text;
        to = Math.Min(to, text.Length);
        if (to <= 0 || from < 0) return -1;
        var k = text.LastIndexOf(word, to - 1, StringComparison.Ordinal);
        while (k >= from)
        {
            var before = k == 0 || !(char.IsLetterOrDigit(text[k - 1]) || text[k - 1] == '_');
            var afterPos = k + word.Length;
            var after = afterPos >= text.Length || !(char.IsLetterOrDigit(text[afterPos]) || text[afterPos] == '_');
            if (before && after) return k;
            if (k == 0) break;
            k = text.LastIndexOf(word, k - 1, StringComparison.Ordinal);
        }
        return -1;
    }

    private void AddMember(MemberExpr m, Symbol? sym, bool isCall)
    {
        var from = m.Span.Start.Offset;
        var to = m.Span.End.Offset;
        var hi = Locator.LowerBound(_code, to) - 1;
        var idx = -1;
        for (var i = Math.Min(hi, _code.Count - 1); i >= 0 && _code[i].Start.Offset >= from; i--)
            if (_code[i].Type == TokenType.Identifier && _code[i].Value == m.Member) { idx = i; break; }
        var pos = idx >= 0 ? _code[idx].Start.Offset : ScanLastWord(m.Member, from, to);
        if (pos < 0) return;
        if (sym is null && _model.Members.TryGetValue(m, out var mb)) sym = mb.Symbol;
        Add(pos, m.Member, sym, isCall ? "method" : "property", false);
    }
}
