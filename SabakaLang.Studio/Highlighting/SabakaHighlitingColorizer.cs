using System;
using System.Linq;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using SabakaLang.Compiler;
using SabakaLang.LanguageServer;

namespace SabakaLang.Studio.Highlighting;

public sealed class SabakaHighlightingColorizer : DocumentColorizingTransformer
{
    private const string DocumentUri = "studio://active-document";

    private readonly DocumentStore _store;
    private DocumentAnalysis? _analysis;

    private static readonly IBrush BrushKeyword    = new SolidColorBrush(Color.Parse("#6CD5EB"));
    private static readonly IBrush BrushString     = new SolidColorBrush(Color.Parse("#CE9178"));
    private static readonly IBrush BrushNumber     = new SolidColorBrush(Color.Parse("#B5CEA8"));
    private static readonly IBrush BrushComment    = new SolidColorBrush(Color.Parse("#6A9955"));
    private static readonly IBrush BrushOperator   = new SolidColorBrush(Color.Parse("#D4D4D4"));
    private static readonly IBrush BrushClass      = new SolidColorBrush(Color.Parse("#C191FF"));
    private static readonly IBrush BrushInterface  = new SolidColorBrush(Color.Parse("#C191FF"));
    private static readonly IBrush BrushStruct     = new SolidColorBrush(Color.Parse("#C191FF"));
    private static readonly IBrush BrushEnum       = new SolidColorBrush(Color.Parse("#C191FF"));
    private static readonly IBrush BrushEnumMember = new SolidColorBrush(Color.Parse("#4FC1FF"));
    private static readonly IBrush BrushFunction   = new SolidColorBrush(Color.Parse("#DCDCAA"));
    private static readonly IBrush BrushMethod     = new SolidColorBrush(Color.Parse("#39CC9B"));
    private static readonly IBrush BrushParameter  = new SolidColorBrush(Color.Parse("#9CDCFE"));
    private static readonly IBrush BrushProperty   = new SolidColorBrush(Color.Parse("#9CDCFE"));
    private static readonly IBrush BrushNamespace  = new SolidColorBrush(Color.Parse("#4EC9B0"));
    private static readonly IBrush BrushVariable   = new SolidColorBrush(Color.Parse("#9CDCFE"));
    private static readonly IBrush BrushBuiltin    = new SolidColorBrush(Color.Parse("#39CC9B"));

    public SabakaHighlightingColorizer(DocumentStore store)
    {
        _store = store;
    }

    public void UpdateSource(string source)
    {
        _analysis = _store.Analyze(DocumentUri, source);
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (_analysis is null) return;
        if (line.Length == 0) return;

        var source = _analysis.Source;

        foreach (var token in _analysis.Lexer.Tokens)
        {
            if (token.Type == TokenType.Eof) break;

            IBrush? brush = ResolveColor(token);
            if (brush is null) continue;

            int segStart = token.Start.Offset;
            int segEnd   = token.End.Offset + 1;

            int lineStart = line.Offset;
            int lineEnd   = line.Offset + line.Length;

            int clampStart = Math.Max(segStart, lineStart);
            int clampEnd   = Math.Min(segEnd,   lineEnd);
            if (clampStart >= clampEnd) continue;

            IBrush capturedBrush = brush;
            ChangeLinePart(clampStart, clampEnd, element =>
            {
                element.TextRunProperties.SetForegroundBrush(capturedBrush);
            });
        }
    }

    private IBrush? ResolveColor(Token token) => token.Type switch
    {
        TokenType.Comment                   => BrushComment,
        TokenType.StringLiteral             => BrushString,
        TokenType.InterpolatedStringLiteral => BrushString,
        TokenType.IntLiteral                => BrushNumber,
        TokenType.FloatLiteral              => BrushNumber,

        TokenType.If          or TokenType.Else       or TokenType.While     or
        TokenType.For         or TokenType.Foreach    or TokenType.In        or
        TokenType.Return      or TokenType.Class      or TokenType.Interface or
        TokenType.StructKeyword or TokenType.Enum    or TokenType.New       or
        TokenType.Override    or TokenType.Super      or TokenType.Null      or
        TokenType.Is          or TokenType.Switch     or TokenType.Case      or
        TokenType.Default     or TokenType.Import     or TokenType.Public    or
        TokenType.Private     or TokenType.Protected  or TokenType.BoolKeyword or
        TokenType.IntKeyword  or TokenType.FloatKeyword or TokenType.StringKeyword or
        TokenType.VoidKeyword or TokenType.True       or TokenType.False     => BrushKeyword,

        TokenType.Plus        or TokenType.Minus      or TokenType.Star      or
        TokenType.Slash       or TokenType.Percent    or TokenType.PlusEqual or
        TokenType.MinusEqual  or TokenType.StarEqual  or TokenType.PlusPlus  or
        TokenType.MinusMinus  or TokenType.Equal      or TokenType.EqualEqual or
        TokenType.NotEqual    or TokenType.Greater    or TokenType.Less      or
        TokenType.GreaterEqual or TokenType.LessEqual or TokenType.AndAnd    or
        TokenType.OrOr        or TokenType.Bang       or TokenType.Question  or
        TokenType.QuestionQuestion or TokenType.Dot   or TokenType.Colon     or
        TokenType.ColonColon                          => BrushOperator,

        TokenType.Identifier => ResolveIdentifierColor(token.Value),

        _ => null
    };

    private IBrush ResolveIdentifierColor(string name)
    {
        if (_analysis is null) return BrushVariable;

        var syms = _analysis.Bind.Symbols.Lookup(name).ToList();
        if (syms.Count == 0) return BrushVariable;

        var sym = syms.First();
        if (sym.Kind == SymbolKind.BuiltIn) return BrushBuiltin;

        return sym.Kind switch
        {
            SymbolKind.Class      => BrushClass,
            SymbolKind.Interface  => BrushInterface,
            SymbolKind.Struct     => BrushStruct,
            SymbolKind.Enum       => BrushEnum,
            SymbolKind.EnumMember => BrushEnumMember,
            SymbolKind.Function   => BrushFunction,
            SymbolKind.Method     => BrushMethod,
            SymbolKind.Parameter  => BrushParameter,
            SymbolKind.Field      => BrushProperty,
            SymbolKind.TypeParam  => BrushParameter,
            SymbolKind.Module     => BrushNamespace,
            _                     => BrushVariable,
        };
    }
}