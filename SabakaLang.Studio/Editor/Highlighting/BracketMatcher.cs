using SabakaLang.Studio.Editor.Core;

namespace SabakaLang.Studio.Editor.Highlighting;

public static class BracketMatcher
{
    private static readonly Dictionary<char, char> OpenToClose = new()
        { ['{'] = '}', ['('] = ')', ['['] = ']' };
    private static readonly Dictionary<char, char> CloseToOpen = new()
        { ['}'] = '{', [')'] = '(', [']'] = '[' };

    public record struct BracketPair(int OpenRow, int OpenCol, int CloseRow, int CloseCol);

    public static BracketPair? Find(TextBuffer buf, TextPosition caret)
    {
        foreach (var col in new[] { caret.Col, caret.Col - 1 })
        {
            if (col < 0 || col > buf.LineLength(caret.Row) - 1) continue;
            var ch = buf.Line(caret.Row)[col];
            var pos = new TextPosition(caret.Row, col);

            if (OpenToClose.TryGetValue(ch, out var close))
            {
                var match = ScanForward(buf, pos, ch, close);
                if (match.HasValue) return new BracketPair(pos.Row, pos.Col, match.Value.Row, match.Value.Col);
            }
            else if (CloseToOpen.TryGetValue(ch, out var open))
            {
                var match = ScanBackward(buf, pos, ch, open);
                if (match.HasValue) return new BracketPair(match.Value.Row, match.Value.Col, pos.Row, pos.Col);
            }
        }
        return null;
    }

    private static TextPosition? ScanForward(TextBuffer buf, TextPosition from, char open, char close)
    {
        var depth = 0;
        for (var row = from.Row; row < buf.LineCount; row++)
        {
            var ln    = buf.Line(row);
            var start = row == from.Row ? from.Col : 0;
            for (var c = start; c < ln.Length; c++)
            {
                if (ln[c] == open)       depth++;
                else if (ln[c] == close) { depth--; if (depth == 0) return new(row, c); }
            }
        }
        return null;
    }

    private static TextPosition? ScanBackward(TextBuffer buf, TextPosition from, char close, char open)
    {
        var depth = 0;
        for (var row = from.Row; row >= 0; row--)
        {
            var ln  = buf.Line(row);
            var end = row == from.Row ? from.Col : ln.Length - 1;
            for (var c = end; c >= 0; c--)
            {
                if (ln[c] == close) depth++;
                else if (ln[c] == open) { depth--; if (depth == 0) return new(row, c); }
            }
        }
        return null;
    }
}