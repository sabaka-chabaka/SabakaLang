using SabakaLang.Binder;
using SabakaLang.Lexer.Enums;

namespace SabakaLang.LanguageServer;

public static class Semantic
{
    public static readonly string[] Types =
    [
        "keyword", "type", "class", "interface", "struct", "enum", "enumMember", "function", "method",
        "variable", "parameter", "property", "string", "number", "comment", "namespace"
    ];

    public static readonly string[] Modifiers = ["declaration", "readonly"];

    private static int T(string n) => Array.IndexOf(Types, n);

    private static readonly HashSet<TokenType> Keywords =
    [
        TokenType.BoolKeyword, TokenType.True, TokenType.False, TokenType.If, TokenType.Else, TokenType.While,
        TokenType.For, TokenType.Enum, TokenType.IntKeyword, TokenType.FloatKeyword, TokenType.StringKeyword,
        TokenType.CharKeyword, TokenType.Return, TokenType.VoidKeyword, TokenType.Foreach, TokenType.In,
        TokenType.StructKeyword, TokenType.Class, TokenType.New, TokenType.Override, TokenType.Super,
        TokenType.Interface, TokenType.Null, TokenType.Is, TokenType.Switch, TokenType.Case, TokenType.Default,
        TokenType.Public, TokenType.Private, TokenType.Protected, TokenType.Import, TokenType.Const,
        TokenType.Break, TokenType.Continue
    ];

    private static (string? Type, int Mods) Of(Ref r)
    {
        var mods = r.IsDecl ? 1 : 0;
        if (r.Symbol is null) return (r.Kind, mods);
        switch (r.Symbol)
        {
            case VariableSymbol { IsConst: true }: mods |= 2; break;
            case FieldSymbol { IsConst: true }: mods |= 2; break;
            case ConstantSymbol: mods |= 2; break;
        }
        var type = r.Symbol.Kind switch
        {
            SymbolKind.Variable or SymbolKind.Constant => "variable",
            SymbolKind.Parameter => "parameter",
            SymbolKind.Field or SymbolKind.Property => "property",
            SymbolKind.Function => r.Symbol is FunctionSymbol { Owner: not null } ? "method" : "function",
            SymbolKind.FunctionGroup => r.Symbol is FunctionGroupSymbol { Overloads: [{ Owner: not null }, ..] } ? "method" : "function",
            SymbolKind.Class => "class",
            SymbolKind.Interface => "interface",
            SymbolKind.Struct => "struct",
            SymbolKind.Enum => "enum",
            SymbolKind.EnumMember => "enumMember",
            SymbolKind.TypeParam => "type",
            SymbolKind.Module => "namespace",
            _ => null
        };
        return (type, mods);
    }

    public static int[] Build(Analysis a)
    {
        var text = a.Text;
        var items = new List<(int Line, int Char, int Len, int Type, int Mods)>();

        void Emit(int start, int end, int type, int mods)
        {
            if (type < 0) return;
            var line = a.Lines.ToPosition(start).Line;
            var pos = start;
            while (pos < end && line < a.Lines.LineCount)
            {
                var segEnd = Math.Min(end, a.Lines.LineEnd(line));
                if (segEnd > pos) items.Add((line, pos - a.Lines.LineStart(line), segEnd - pos, type, mods));
                line++;
                pos = Math.Max(pos, a.Lines.LineStart(line));
            }
        }

        var inImport = false;
        foreach (var tok in a.Root.Tokens)
        {
            if (tok.Type == TokenType.Eof) break;
            var start = tok.Start.Offset;
            var end = Math.Min(tok.End.Offset + 1, text.Length);
            if (end <= start) continue;

            if (tok.Type == TokenType.Import) inImport = true;
            else if (tok.Type == TokenType.Semicolon) inImport = false;

            switch (tok.Type)
            {
                case TokenType.Comment: Emit(start, end, T("comment"), 0); break;
                case TokenType.IntLiteral or TokenType.FloatLiteral: Emit(start, end, T("number"), 0); break;
                case TokenType.StringLiteral or TokenType.CharLiteral:
                    Emit(start, end, T("string"), 0); break;
                case TokenType.InterpolatedStringLiteral:
                {
                    var cur = start;
                    foreach (var x in a.Refs.Where(x => x.Start > start && x.End <= end))
                    {
                        if (x.Start < cur) continue;
                        if (x.Start > cur) Emit(cur, x.Start, T("string"), 0);
                        var (xt, xm) = Of(x);
                        Emit(x.Start, x.End, xt is null ? T("string") : T(xt), xt is null ? 0 : xm);
                        cur = x.End;
                    }
                    if (cur < end) Emit(cur, end, T("string"), 0);
                    break;
                }
                case TokenType.IntKeyword or TokenType.FloatKeyword or TokenType.StringKeyword or TokenType.CharKeyword
                    or TokenType.BoolKeyword or TokenType.VoidKeyword:
                    Emit(start, end, T("type"), 0); break;
                case TokenType.Identifier:
                    if (inImport && tok.Value is "from" or "as") { Emit(start, end, T("keyword"), 0); break; }
                    var r = a.RefAtStart(start);
                    if (r is not null)
                    {
                        var (ty, mods) = Of(r);
                        if (ty is not null) Emit(start, end, T(ty), mods);
                    }
                    break;
                default:
                    if (Keywords.Contains(tok.Type)) Emit(start, end, T("keyword"), 0);
                    break;
            }
        }

        items.Sort((x, y) => x.Line != y.Line ? x.Line.CompareTo(y.Line) : x.Char.CompareTo(y.Char));
        var data = new List<int>(items.Count * 5);
        int pl = 0, pc = 0;
        foreach (var it in items)
        {
            var dl = it.Line - pl;
            var dc = dl == 0 ? it.Char - pc : it.Char;
            data.AddRange([dl, dc, it.Len, it.Type, it.Mods]);
            pl = it.Line;
            pc = it.Char;
        }
        return data.ToArray();
    }
}
