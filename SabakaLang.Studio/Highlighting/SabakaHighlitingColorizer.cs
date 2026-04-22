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

    private static readonly IBrush BrushKeyword    = new SolidColorBrush(Color.Parse("#569CD6"));
    private static readonly IBrush BrushString     = new SolidColorBrush(Color.Parse("#CE9178"));
    private static readonly IBrush BrushNumber     = new SolidColorBrush(Color.Parse("#B5CEA8"));
    private static readonly IBrush BrushComment    = new SolidColorBrush(Color.Parse("#6A9955"));
    private static readonly IBrush BrushOperator   = new SolidColorBrush(Color.Parse("#D4D4D4"));
    private static readonly IBrush BrushClass      = new SolidColorBrush(Color.Parse("#4EC9B0"));
    private static readonly IBrush BrushInterface  = new SolidColorBrush(Color.Parse("#B8D7A3"));
    private static readonly IBrush BrushStruct     = new SolidColorBrush(Color.Parse("#86C691"));
    private static readonly IBrush BrushEnum       = new SolidColorBrush(Color.Parse("#B8D7A3"));
    private static readonly IBrush BrushEnumMember = new SolidColorBrush(Color.Parse("#4FC1FF"));
    private static readonly IBrush BrushFunction   = new SolidColorBrush(Color.Parse("#DCDCAA"));
    private static readonly IBrush BrushMethod     = new SolidColorBrush(Color.Parse("#DCDCAA"));
    private static readonly IBrush BrushParameter  = new SolidColorBrush(Color.Parse("#9CDCFE"));
    private static readonly IBrush BrushProperty   = new SolidColorBrush(Color.Parse("#9CDCFE"));
    private static readonly IBrush BrushNamespace  = new SolidColorBrush(Color.Parse("#4EC9B0"));
    private static readonly IBrush BrushVariable   = new SolidColorBrush(Color.Parse("#9CDCFE"));
    private static readonly IBrush BrushBuiltin    = new SolidColorBrush(Color.Parse("#DCDCAA"));

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

        int lineStartOffset = line.Offset;
        int lineLength = line.Length;
        if (lineLength == 0) return;

        int compilerLine = line.LineNumber;

        foreach (var token in _analysis.Lexer.Tokens)
        {
            if (token.Type == TokenType.Eof) break;

            if (token.Start.Line != compilerLine) continue;

            IBrush? brush = ResolveColor(token);
            if (brush is null) continue;

            int startCol = token.Start.Column - 1;
            int length = token.Value.Length > 0 ? token.Value.Length : 1;

            int segStart = lineStartOffset + startCol;
            int segEnd = segStart + length;

            segStart = Math.Max(lineStartOffset, Math.Min(segStart, lineStartOffset + lineLength));
            segEnd   = Math.Max(lineStartOffset, Math.Min(segEnd,   lineStartOffset + lineLength));
            if (segStart >= segEnd) continue;

            IBrush capturedBrush = brush;
            ChangeLinePart(segStart, segEnd, element =>
            {
                element.TextRunProperties.SetForegroundBrush(capturedBrush);
            });
        }
    }

    private IBrush? ResolveColor(Token token)
    {
        switch (token.Type)
        {
            case TokenType.Comment:
                return BrushComment;

            case TokenType.StringLiteral:
            case TokenType.InterpolatedStringLiteral:
                return BrushString;

            case TokenType.IntLiteral:
            case TokenType.FloatLiteral:
                return BrushNumber;

            case TokenType.If:
            case TokenType.Else:
            case TokenType.While:
            case TokenType.For:
            case TokenType.Foreach:
            case TokenType.In:
            case TokenType.Return:
            case TokenType.Class:
            case TokenType.Interface:
            case TokenType.StructKeyword:
            case TokenType.Enum:
            case TokenType.New:
            case TokenType.Override:
            case TokenType.Super:
            case TokenType.Null:
            case TokenType.Is:
            case TokenType.Switch:
            case TokenType.Case:
            case TokenType.Default:
            case TokenType.Import:
            case TokenType.Public:
            case TokenType.Private:
            case TokenType.Protected:
            case TokenType.BoolKeyword:
            case TokenType.IntKeyword:
            case TokenType.FloatKeyword:
            case TokenType.StringKeyword:
            case TokenType.VoidKeyword:
            case TokenType.True:
            case TokenType.False:
                return BrushKeyword;

            case TokenType.Plus:
            case TokenType.Minus:
            case TokenType.Star:
            case TokenType.Slash:
            case TokenType.Percent:
            case TokenType.PlusEqual:
            case TokenType.MinusEqual:
            case TokenType.StarEqual:
            case TokenType.PlusPlus:
            case TokenType.MinusMinus:
            case TokenType.Equal:
            case TokenType.EqualEqual:
            case TokenType.NotEqual:
            case TokenType.Greater:
            case TokenType.Less:
            case TokenType.GreaterEqual:
            case TokenType.LessEqual:
            case TokenType.AndAnd:
            case TokenType.OrOr:
            case TokenType.Bang:
            case TokenType.Question:
            case TokenType.QuestionQuestion:
            case TokenType.Dot:
            case TokenType.Colon:
            case TokenType.ColonColon:
                return BrushOperator;

            case TokenType.Identifier:
                return ResolveIdentifierColor(token.Value);

            default:
                return null;
        }
    }

    private IBrush ResolveIdentifierColor(string name)
    {
        if (_analysis is null) return BrushVariable;

        var syms = _analysis.Bind.Symbols.Lookup(name).ToList();
        if (syms.Count == 0) return BrushVariable;

        var sym = syms.First();

        if (sym.Kind == SymbolKind.BuiltIn) return BrushBuiltin;

        return sym.Kind switch
        {
            SymbolKind.Class       => BrushClass,
            SymbolKind.Interface   => BrushInterface,
            SymbolKind.Struct      => BrushStruct,
            SymbolKind.Enum        => BrushEnum,
            SymbolKind.EnumMember  => BrushEnumMember,
            SymbolKind.Function    => BrushFunction,
            SymbolKind.Method      => BrushMethod,
            SymbolKind.Parameter   => BrushParameter,
            SymbolKind.Field       => BrushProperty,
            SymbolKind.TypeParam   => BrushParameter,
            SymbolKind.Module      => BrushNamespace,
            _                      => BrushVariable,
        };
    }
}