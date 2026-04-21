using SabakaLang.Studio.Editor.Core;
using SabakaLang.Studio.Editor.Highlighting;
using SabakaLang.LanguageServer;

namespace SabakaLang.Studio.Editor.Render;

public static class EditorRenderer
{
    public static void Draw(
        ICanvas   canvas,
        RectF     clip,
        EditorModel            model,
        SyntaxTokenizer        tokens,
        IReadOnlyList<Diagnostic> diagnostics,
        BracketMatcher.BracketPair? bracket,
        IReadOnlyList<TextPosition> searchHits,
        int       searchCurrent,
        bool      caretOn,
        float     scrollRow)
    {
        var buf = model.Buffer;
        var sel = model.Selection;

        float lh = Theme.LineHeight;
        float gw = Theme.GutterW;
        float cw = Theme.CharWidth;

        int firstRow = Math.Max(0, (int)scrollRow);
        int lastRow  = Math.Min(buf.LineCount - 1, (int)(scrollRow + clip.Height / lh) + 2);

        canvas.FillColor = Theme.Bg;
        canvas.FillRectangle(clip);

        canvas.FillColor = Theme.GutterBg;
        canvas.FillRectangle(0, 0, gw, clip.Height);

        for (var row = firstRow; row <= lastRow; row++)
        {
            float y = RowY(row, scrollRow);

            if (row == sel.Caret.Row)
            {
                canvas.FillColor = Theme.LineHl;
                canvas.FillRectangle(gw, y, clip.Width - gw, lh);
            }

            DrawSelection(canvas, sel, row, y, buf, gw, cw, lh, clip.Width);

            DrawSearchHits(canvas, searchHits, searchCurrent, row, y, cw, gw, lh);

            DrawLineText(canvas, buf, tokens, row, y, gw, cw);

            if (bracket.HasValue)
                DrawBracketHighlight(canvas, bracket.Value, row, y, gw, cw, lh);

            DrawLineNumber(canvas, row, sel.Caret.Row, y, gw, lh);
        }
        
        foreach (var d in diagnostics)
        {
            var row = d.Start.Line - 1;  // compiler 1-based → 0-based
            if (row < firstRow || row > lastRow) continue;
            float y    = RowY(row, scrollRow);
            float xOff = gw + (d.Start.Column - 1) * cw;
            float len  = Math.Max(cw, (d.End.Column - d.Start.Column) * cw);
            DrawSquiggle(canvas, xOff, y + lh - 2, len,
                d.Severity == DiagnosticSeverity.Error ? Theme.DiagError : Theme.DiagWarn);
        }

        if (caretOn)
        {
            var caret = sel.Caret;
            float cx = gw + caret.Col * cw;
            float cy = RowY(caret.Row, scrollRow);
            canvas.StrokeColor = Theme.CaretColor;
            canvas.StrokeSize  = 2f;
            canvas.DrawLine(cx, cy, cx, cy + lh);
        }

        canvas.StrokeColor = Theme.GutterBorder;
        canvas.StrokeSize  = 1f;
        canvas.DrawLine(gw, 0, gw, clip.Height);
    }

    private static float RowY(int row, float scrollRow) =>
        (row - scrollRow) * Theme.LineHeight;

    private static void DrawSelection(
        ICanvas canvas, Selection sel, int row, float y,
        TextBuffer buf, float gw, float cw, float lh, float clipW)
    {
        if (sel.IsEmpty) return;
        var (s, e) = sel.Ordered;
        if (row < s.Row || row > e.Row) return;

        float x1, x2;
        var lineLen = buf.LineLength(row);

        if (row == s.Row && row == e.Row)
        {
            x1 = gw + s.Col * cw;
            x2 = gw + e.Col * cw;
        }
        else if (row == s.Row)
        {
            x1 = gw + s.Col * cw;
            x2 = gw + (lineLen + 1) * cw;
        }
        else if (row == e.Row)
        {
            x1 = gw;
            x2 = gw + e.Col * cw;
        }
        else
        {
            x1 = gw;
            x2 = gw + (lineLen + 1) * cw;
        }

        canvas.FillColor = Theme.Selection;
        canvas.FillRectangle(x1, y, x2 - x1, lh);
    }

    private static void DrawSearchHits(
        ICanvas canvas, IReadOnlyList<TextPosition> hits, int current,
        int row, float y, float cw, float gw, float lh)
    {
        for (var i = 0; i < hits.Count; i++)
        {
            var h = hits[i];
            if (h.Row != row) continue;
            canvas.FillColor = i == current ? Theme.SearchActive : Theme.SearchHit;
            canvas.FillRectangle(gw + h.Col * cw, y, cw * 4, lh);
        }
    }

    private static void DrawLineText(
        ICanvas canvas, TextBuffer buf, SyntaxTokenizer tokens,
        int row, float y, float gw, float cw)
    {
        var text = buf.Line(row);
        if (text.Length == 0) return;

        var lt      = tokens.Get(row);
        var spans   = lt.Spans;
        float baseY = y + Theme.LineHeight - 4f;

        canvas.Font     = new Microsoft.Maui.Graphics.Font(Theme.Font);
        canvas.FontSize = Theme.FontSize;

        var spanIdx = 0;
        var col     = 0;

        while (col < text.Length)
        {
            Color color   = Theme.Plain;
            int   runEnd  = text.Length;

            if (spanIdx < spans.Count)
            {
                var sp = spans[spanIdx];
                if (col < sp.StartCol)
                {
                    runEnd = sp.StartCol;
                }
                else if (col >= sp.StartCol && col < sp.EndCol)
                {
                    color  = sp.Color;
                    runEnd = sp.EndCol;
                    if (col == sp.StartCol) spanIdx++; // advance only once per span
                }
                else
                {
                    spanIdx++;
                    continue;
                }
            }

            runEnd = Math.Min(runEnd, text.Length);
            var segment = text[col..runEnd];
            if (segment.Length == 0) break;

            canvas.FontColor = color;
            canvas.DrawString(segment, gw + col * cw, baseY, HorizontalAlignment.Left);
            col = runEnd;
        }
    }

    private static void DrawLineNumber(
        ICanvas canvas, int row, int caretRow, float y, float gw, float lh)
    {
        canvas.Font      = new Microsoft.Maui.Graphics.Font(Theme.Font);
        canvas.FontSize  = Theme.FontSize - 1;
        canvas.FontColor = row == caretRow ? Theme.LineNumActive : Theme.LineNum;
        canvas.DrawString((row + 1).ToString(), gw - 6, y + lh - 4, HorizontalAlignment.Right);
    }

    private static void DrawBracketHighlight(
        ICanvas canvas, BracketMatcher.BracketPair pair, int row, float y,
        float gw, float cw, float lh)
    {
        void Hl(int pRow, int pCol)
        {
            if (pRow != row) return;
            canvas.StrokeColor = Theme.BracketHl;
            canvas.StrokeSize  = 1.5f;
            canvas.DrawRectangle(gw + pCol * cw, y + 1, cw, lh - 2);
        }
        Hl(pair.OpenRow,  pair.OpenCol);
        Hl(pair.CloseRow, pair.CloseCol);
    }

    private static void DrawSquiggle(ICanvas canvas, float x, float y, float width, Color color)
    {
        canvas.StrokeColor = color;
        canvas.StrokeSize  = 1.5f;
        var path = new PathF();
        path.MoveTo(x, y);
        const float period = 4f, amp = 2f;
        for (float i = 0; i <= width; i += 0.5f)
            path.LineTo(x + i, y + (float)Math.Sin(i / period * Math.PI * 2) * amp);
        canvas.DrawPath(path);
    }
}