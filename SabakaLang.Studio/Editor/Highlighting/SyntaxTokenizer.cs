using SabakaLang.Compiler;
using SabakaLang.LanguageServer;

namespace SabakaLang.Studio.Editor.Highlighting;

public readonly record struct ColorSpan(int StartCol, int EndCol, Color Color);

public sealed class LineTokens
{
    public static readonly LineTokens Empty = new(0, []);
    public int Line   { get; }
    public IReadOnlyList<ColorSpan> Spans { get; }
    public LineTokens(int line, List<ColorSpan> spans) { Line = line; Spans = spans; }
}

public sealed class SyntaxTokenizer
{
    private LineTokens[] _lines = [];
    private string       _uri   = "";

    public event Action? TokensReady;

    public void Analyze(string uri, string source, DocumentStore store)
    {
        _uri = uri;
        try
        {
            var analysis = store.Analyze(uri, source);
            _lines = BuildLines(analysis);
        }
        catch
        {
            _lines = [];
        }
        TokensReady?.Invoke();
    }

    public LineTokens Get(int row) =>
        row >= 0 && row < _lines.Length ? _lines[row] : LineTokens.Empty;

    private static LineTokens[] BuildLines(DocumentAnalysis a)
    {
        var maxLine = 0;
        foreach (var t in a.Lexer.Tokens)
        {
            if (t.Type == TokenType.Eof) break;
            if (t.Start.Line > maxLine) maxLine = t.Start.Line;
        }
        if (maxLine == 0) return [];

        var buckets = new List<ColorSpan>[maxLine];
        for (var i = 0; i < buckets.Length; i++) buckets[i] = [];

        foreach (var tok in a.Lexer.Tokens)
        {
            if (tok.Type == TokenType.Eof) break;

            var col  = ColorFor(tok, a);
            var row  = tok.Start.Line - 1;
            var cStart = tok.Start.Column - 1;
            var cEnd   = cStart + Math.Max(tok.Value.Length, 1);

            if (row < 0 || row >= buckets.Length) continue;

            buckets[row].Add(new ColorSpan(cStart, cEnd, col));
        }

        var result = new LineTokens[buckets.Length];
        for (var i = 0; i < buckets.Length; i++)
        {
            buckets[i].Sort((x, y) => x.StartCol.CompareTo(y.StartCol));
            result[i] = new LineTokens(i, buckets[i]);
        }
        return result;
    }

    private static Color ColorFor(Token tok, DocumentAnalysis a)
    {
        switch (tok.Type)
        {
            case TokenType.Comment:   return Theme.Comment;
            case TokenType.StringLiteral:
            case TokenType.InterpolatedStringLiteral: return Theme.StringColor;
            case TokenType.IntLiteral:
            case TokenType.FloatLiteral: return Theme.Number;

            case TokenType.If: case TokenType.Else: case TokenType.While:
            case TokenType.For: case TokenType.Foreach: case TokenType.In:
            case TokenType.Return: case TokenType.Class: case TokenType.Interface:
            case TokenType.StructKeyword: case TokenType.Enum: case TokenType.New:
            case TokenType.Override: case TokenType.Super: case TokenType.Null:
            case TokenType.Is: case TokenType.Switch: case TokenType.Case:
            case TokenType.Default: case TokenType.Import: case TokenType.Public:
            case TokenType.Private: case TokenType.Protected: case TokenType.BoolKeyword:
            case TokenType.IntKeyword: case TokenType.FloatKeyword: case TokenType.StringKeyword:
            case TokenType.VoidKeyword: case TokenType.True: case TokenType.False:
                return Theme.Keyword;

            case TokenType.Plus: case TokenType.Minus: case TokenType.Star:
            case TokenType.Slash: case TokenType.Percent: case TokenType.PlusEqual:
            case TokenType.MinusEqual: case TokenType.StarEqual: case TokenType.PlusPlus:
            case TokenType.MinusMinus: case TokenType.Equal: case TokenType.EqualEqual:
            case TokenType.NotEqual: case TokenType.Greater: case TokenType.Less:
            case TokenType.GreaterEqual: case TokenType.LessEqual: case TokenType.AndAnd:
            case TokenType.OrOr: case TokenType.Bang: case TokenType.Question:
            case TokenType.QuestionQuestion: case TokenType.Dot: case TokenType.Colon:
            case TokenType.ColonColon:
                return Theme.Operator;

            case TokenType.Identifier:
                return ColorForIdentifier(tok.Value, a);

            default:
                return Theme.Plain;
        }
    }

    private static Color ColorForIdentifier(string name, DocumentAnalysis a)
    {
        var syms = a.Bind.Symbols.Lookup(name).ToList();
        if (syms.Count == 0) return Theme.Plain;
        return syms[0].Kind switch
        {
            SymbolKind.Class     => Theme.ClassName,
            SymbolKind.Interface => Theme.TypeName,
            SymbolKind.Struct    => Theme.TypeName,
            SymbolKind.Enum      => Theme.TypeName,
            SymbolKind.EnumMember=> Theme.EnumMember,
            SymbolKind.Function  => Theme.Function,
            SymbolKind.Method    => Theme.Function,
            SymbolKind.BuiltIn   => Theme.Function,
            SymbolKind.Parameter => Theme.Parameter,
            SymbolKind.Field     => Theme.Property,
            SymbolKind.Module    => Theme.Namespace,
            _                    => Theme.Variable
        };
    }
}