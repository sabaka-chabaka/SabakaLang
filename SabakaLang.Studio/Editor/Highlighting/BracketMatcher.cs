using SabakaLang.Studio.Editor.Core;

namespace SabakaLang.Studio.Editor.Highlighting;

public sealed class BracketMatcher
{
    private static readonly Dictionary<char, char> _open  = new() { ['{'] = '}', ['('] = ')', ['['] = ']' };
    private static readonly Dictionary<char, char> _close = new() { ['}'] = '{', [')'] = '(', [']'] = '[' };
 
    public record BracketMatch(CaretPosition Open, CaretPosition Close);
 
    public BracketMatch? FindMatch(EditorDocument doc, CaretPosition caret)
    {
        var line = doc.GetLine(caret.Line);
        if (line.Length == 0) return null;
 
        var col = Math.Clamp(caret.Column, 0, line.Length - 1);
        var ch  = line[col];
 
        if (_open.TryGetValue(ch, out var closeCh))
            return ScanForward(doc, caret, ch, closeCh);
 
        if (_close.TryGetValue(ch, out var openCh))
            return ScanBackward(doc, caret, ch, openCh);
 
        if (col > 0)
        {
            ch = line[col - 1];
            var prevPos = new CaretPosition(caret.Line, col - 1);
            if (_open.TryGetValue(ch, out closeCh))
                return ScanForward(doc, prevPos, ch, closeCh);
            if (_close.TryGetValue(ch, out openCh))
                return ScanBackward(doc, prevPos, ch, openCh);
        }
 
        return null;
    }
 
    private static BracketMatch? ScanForward(EditorDocument doc, CaretPosition from, char open, char close)
    {
        var depth = 0;
        for (var li = from.Line; li < doc.LineCount; li++)
        {
            var ln    = doc.GetLine(li);
            var start = li == from.Line ? from.Column : 0;
            for (var ci = start; ci < ln.Length; ci++)
            {
                var c = ln[ci];
                if (c == open)  depth++;
                else if (c == close) { depth--; if (depth == 0) return new(from, new CaretPosition(li, ci)); }
            }
        }
        return null;
    }
 
    private static BracketMatch? ScanBackward(EditorDocument doc, CaretPosition from, char close, char open)
    {
        var depth = 0;
        for (var li = from.Line; li >= 0; li--)
        {
            var ln  = doc.GetLine(li);
            var end = li == from.Line ? from.Column : ln.Length - 1;
            for (var ci = end; ci >= 0; ci--)
            {
                var c = ln[ci];
                if (c == close) depth++;
                else if (c == open) { depth--; if (depth == 0) return new(new CaretPosition(li, ci), from); }
            }
        }
        return null;
    }
}