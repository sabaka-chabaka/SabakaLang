using SabakaLang.Compiler;
using SabakaLang.LanguageServer;

namespace SabakaLang.Studio.Editor.Highlighting;

public enum SyntaxKind
{
    Plain,
    Keyword,
    TypeName,
    StringLit,
    Number,
    Comment,
    Operator,
    Punctuation,
    Variable,
    Function,
    Parameter,
    ClassName,
    EnumMember,
    Property,
    Namespace,
    Unknown
}

public readonly record struct SyntaxSpan(int StartCol, int EndCol, SyntaxKind Kind);

public sealed class LineHighlight(int line, IReadOnlyList<SyntaxSpan> spans)
{
    public int Line { get; } = line;
    public IReadOnlyList<SyntaxSpan> Spans { get; } = spans;
    
    public static LineHighlight Empty(int line) => new(line, []);
}

public sealed class SyntaxHighlighter(DocumentStore store)
{
    private readonly DocumentStore _store = store;
    private string _lastSource = string.Empty;
    private LineHighlight[] _cache = [];
    
    public void Invalidate(string uri, string source)
    {
        if (_lastSource == source) return;
        _lastSource = source;
        _cache = BuildHighlights(_store.Analyze(uri, source));
    }
 
    public LineHighlight GetLine(int line) =>
        (uint)line < (uint)_cache.Length ? _cache[line] : LineHighlight.Empty(line);

    private static LineHighlight[] BuildHighlights(DocumentAnalysis analysis)
    {
        var maxLine = 0;
        foreach (var tok in analysis.Lexer.Tokens)
        {
            if (tok.Type == TokenType.Eof) break;
            if (tok.Start.Line > maxLine) maxLine = tok.Start.Line;
        }
        
        var buckets = new List<SyntaxSpan>[maxLine + 1];
        for (var i = 0; i < buckets.Length; i++) buckets[i] = [];
        
        foreach (var tok in analysis.Lexer.Tokens)
        {
            if (tok.Type == TokenType.Eof) break;
 
            var kind = ClassifyToken(tok, analysis);
            if (kind == SyntaxKind.Plain) continue;
 
            var zeroLine = tok.Start.Line - 1;
            var startCol = tok.Start.Column - 1;
            var length   = tok.Value.Length > 0 ? tok.Value.Length : 1;
 
            if ((uint)zeroLine >= (uint)buckets.Length) continue;
 
            buckets[zeroLine].Add(new SyntaxSpan(startCol, startCol + length, kind));
        }
 
        var result = new LineHighlight[buckets.Length];
        for (var i = 0; i < buckets.Length; i++)
            result[i] = new LineHighlight(i, buckets[i]);
        return result;
    }

    private static SyntaxKind ClassifyToken(Token tok, DocumentAnalysis analysis)
    {
        return tok.Type switch
        {
            TokenType.Comment => SyntaxKind.Comment,
 
            TokenType.StringLiteral or
                TokenType.InterpolatedStringLiteral => SyntaxKind.StringLit,
 
            TokenType.IntLiteral or
                TokenType.FloatLiteral => SyntaxKind.Number,
 
            TokenType.If or TokenType.Else or TokenType.While or TokenType.For or
                TokenType.Foreach or TokenType.In or TokenType.Return or TokenType.Class or
                TokenType.Interface or TokenType.StructKeyword or TokenType.Enum or
                TokenType.New or TokenType.Override or TokenType.Super or TokenType.Null or
                TokenType.Is or TokenType.Switch or TokenType.Case or TokenType.Default or
                TokenType.Import or TokenType.Public or TokenType.Private or TokenType.Protected or
                TokenType.BoolKeyword or TokenType.IntKeyword or TokenType.FloatKeyword or
                TokenType.StringKeyword or TokenType.VoidKeyword or
                TokenType.True or TokenType.False => SyntaxKind.Keyword,
 
            TokenType.Plus or TokenType.Minus or TokenType.Star or TokenType.Slash or
                TokenType.Percent or TokenType.PlusEqual or TokenType.MinusEqual or
                TokenType.StarEqual or TokenType.PlusPlus or TokenType.MinusMinus or
                TokenType.Equal or TokenType.EqualEqual or TokenType.NotEqual or
                TokenType.Greater or TokenType.Less or TokenType.GreaterEqual or
                TokenType.LessEqual or TokenType.AndAnd or TokenType.OrOr or
                TokenType.Bang or TokenType.Question or TokenType.QuestionQuestion or
                TokenType.Dot or TokenType.Colon or TokenType.ColonColon => SyntaxKind.Operator,
 
            TokenType.Identifier => ClassifyIdentifier(tok.Value, analysis),
 
            _ => SyntaxKind.Plain
        };
    }
    
    private static SyntaxKind ClassifyIdentifier(string name, DocumentAnalysis analysis)
    {
        var syms = analysis.Bind.Symbols.Lookup(name).ToList();
        if (syms.Count == 0) return SyntaxKind.Variable;
        return syms[0].Kind switch
        {
            SymbolKind.Class     => SyntaxKind.ClassName,
            SymbolKind.Interface => SyntaxKind.TypeName,
            SymbolKind.Struct    => SyntaxKind.TypeName,
            SymbolKind.Enum      => SyntaxKind.TypeName,
            SymbolKind.EnumMember=> SyntaxKind.EnumMember,
            SymbolKind.Function  => SyntaxKind.Function,
            SymbolKind.Method    => SyntaxKind.Function,
            SymbolKind.BuiltIn   => SyntaxKind.Function,
            SymbolKind.Parameter => SyntaxKind.Parameter,
            SymbolKind.Field     => SyntaxKind.Property,
            SymbolKind.Module    => SyntaxKind.Namespace,
            _                    => SyntaxKind.Variable
        };
    }
}