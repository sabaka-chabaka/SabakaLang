using SabakaLang.Studio.Editor.Core;
using SabakaLang.Studio.Editor.Highlighting;

namespace SabakaLang.Studio.Editor.Rendering;

public sealed class EditorRenderer(SyntaxHighlighter highlighter, BracketMatcher brackets)
{
    public void Draw(
        ICanvas           canvas,
        RectF             dirtyRect,
        EditorDocument    doc,
        EditorSelection   selection,
        CaretPosition     caret,
        float             scrollY,
        float             scrollX,
        IReadOnlyList<DiagnosticSpan> diagnostics,
        IReadOnlyList<CaretPosition>  searchHits,
        int               searchCurrent,
        bool              caretVisible)
    {
        var theme = StudioTheme.Self;
        float lh  = Highlighting.StudioTheme.LineHeight;
        float gw  = Highlighting.StudioTheme.GutterWidth;
        float cw  = Highlighting.StudioTheme.CharWidth;

        var firstLine = Math.Max(0, (int)(scrollY / lh));
        var lastLine  = Math.Min(doc.LineCount - 1, (int)((scrollY + dirtyRect.Height) / lh) + 1);

        canvas.FillColor = Highlighting.StudioTheme.Background;
        canvas.FillRectangle(dirtyRect);

        canvas.FillColor = Highlighting.StudioTheme.GutterBackground;
        canvas.FillRectangle(0, 0, gw, dirtyRect.Height);

        var matchedBracket = brackets.FindMatch(doc, caret);

        for (var li = firstLine; li <= lastLine; li++)
        {
            float y = li * lh - scrollY;

            if (li == caret.Line)
            {
                canvas.FillColor = Highlighting.StudioTheme.LineHighlight;
                canvas.FillRectangle(gw, y, dirtyRect.Width - gw, lh);
            }

            DrawSelection(canvas, selection, li, y, lh, gw, cw, doc);

            DrawSearchHits(canvas, searchHits, searchCurrent, li, y, lh, gw, cw);

            DrawLine(canvas, doc, li, y, gw, cw, scrollX);

            DrawGutter(canvas, li, caret.Line, y, gw, lh);

            DrawDiagnostics(canvas, diagnostics, li, y, lh, gw, cw);

            if (matchedBracket is not null)
            {
                DrawBracketHighlight(canvas, matchedBracket, li, y, lh, gw, cw);
            }
        }

        if (caretVisible)
            DrawCaret(canvas, caret, gw, cw, scrollY, lh);

        canvas.StrokeColor = Highlighting.StudioTheme.Border;
        canvas.StrokeSize  = 1;
        canvas.DrawLine(gw, 0, gw, dirtyRect.Height);
    }

    private static void DrawSelection(ICanvas canvas, EditorSelection sel,
        int li, float y, float lh, float gw, float cw, EditorDocument doc)
    {
        if (sel.IsEmpty) return;
        var (start, end) = sel.Ordered;
        if (li < start.Line || li > end.Line) return;

        canvas.FillColor = Highlighting.StudioTheme.Selection;

        var lineLen = doc.GetLine(li).Length;
        float x1, x2;

        if (li == start.Line && li == end.Line)
        {
            x1 = gw + start.Column * cw;
            x2 = gw + end.Column   * cw;
        }
        else if (li == start.Line)
        {
            x1 = gw + start.Column * cw;
            x2 = gw + (lineLen + 1) * cw;
        }
        else if (li == end.Line)
        {
            x1 = gw;
            x2 = gw + end.Column * cw;
        }
        else
        {
            x1 = gw;
            x2 = gw + (lineLen + 1) * cw;
        }

        canvas.FillRectangle(x1, y, x2 - x1, lh);
    }

    private static void DrawSearchHits(ICanvas canvas, IReadOnlyList<CaretPosition> hits,
        int currentIdx, int li, float y, float lh, float gw, float cw)
    {
        for (var i = 0; i < hits.Count; i++)
        {
            var h = hits[i];
            if (h.Line != li) continue;
            canvas.FillColor = i == currentIdx ? Highlighting.StudioTheme.SearchCurrent : Highlighting.StudioTheme.SearchHighlight;
            canvas.FillRectangle(gw + h.Column * cw, y, cw * 4, lh); 
        }
    }

    private void DrawLine(ICanvas canvas, EditorDocument doc, int li,
        float y, float gw, float cw, float scrollX)
    {
        var text    = doc.GetLine(li);
        if (text.Length == 0) return;

        var hl      = highlighter.GetLine(li);
        float baseY = y + Highlighting.StudioTheme.LineHeight - 4;

        canvas.Font     = new Microsoft.Maui.Graphics.Font(Highlighting.StudioTheme.FontFamily);
        canvas.FontSize = Highlighting.StudioTheme.FontSize;

        var spans    = hl.Spans;
        var spanIdx  = 0;
        var col      = 0;

        while (col < text.Length)
        {
            SyntaxKind kind = SyntaxKind.Plain;
            int spanEnd  = text.Length;

            if (spanIdx < spans.Count)
            {
                var span = spans[spanIdx];
                if (col < span.StartCol)
                    spanEnd = span.StartCol;
                else if (col == span.StartCol)
                {
                    kind    = span.Kind;
                    spanEnd = span.EndCol;
                    spanIdx++;
                }
                else spanIdx++;
            }

            var segment = text[col..Math.Min(spanEnd, text.Length)];
            if (segment.Length == 0) break;

            float x = gw + col * cw - scrollX;
            canvas.FontColor = Highlighting.StudioTheme.ColorFor(kind);
            canvas.DrawString(segment, x, baseY, HorizontalAlignment.Left);
            col = spanEnd;
        }
    }

    private static void DrawGutter(ICanvas canvas, int li, int caretLine,
        float y, float gw, float lh)
    {
        canvas.Font     = new Microsoft.Maui.Graphics.Font(Highlighting.StudioTheme.FontFamily);
        canvas.FontSize = Highlighting.StudioTheme.FontSize - 1;
        canvas.FontColor = li == caretLine ? Highlighting.StudioTheme.CurrentLineNumber : Highlighting.StudioTheme.GutterForeground;
        var num = (li + 1).ToString();
        canvas.DrawString(num, gw - 8, y + lh - 4, HorizontalAlignment.Right);
    }

    private static void DrawCaret(ICanvas canvas, CaretPosition caret,
        float gw, float cw, float scrollY, float lh)
    {
        float x = gw + caret.Column * cw;
        float y = caret.Line * lh - scrollY;
        canvas.StrokeColor = Highlighting.StudioTheme.Caret;
        canvas.StrokeSize  = 2f;
        canvas.DrawLine(x, y, x, y + lh);
    }

    private static void DrawDiagnostics(ICanvas canvas,
        IReadOnlyList<DiagnosticSpan> diagnostics, int li, float y,
        float lh, float gw, float cw)
    {
        foreach (var d in diagnostics)
        {
            if (d.Line != li) continue;
            var color = d.IsError ? Highlighting.StudioTheme.DiagError : Highlighting.StudioTheme.DiagWarning;
            DrawSquiggle(canvas, gw + d.StartCol * cw, y + lh - 2,
                         (d.EndCol - d.StartCol) * cw, color);
        }
    }

    private static void DrawSquiggle(ICanvas canvas, float x, float y, float width, Color color)
    {
        canvas.StrokeColor = color;
        canvas.StrokeSize  = 1.5f;
        const float amp = 2f, period = 4f;
        var path = new PathF();
        path.MoveTo(x, y);
        for (float i = 0; i < width; i += 0.5f)
        {
            float dy = (float)Math.Sin((i / period) * Math.PI * 2) * amp;
            path.LineTo(x + i, y + dy);
        }
        canvas.DrawPath(path);
    }

    private static void DrawBracketHighlight(ICanvas canvas,
        BracketMatcher.BracketMatch match, int li, float y, float lh, float gw, float cw)
    {
        void Highlight(CaretPosition pos)
        {
            if (pos.Line != li) return;
            canvas.StrokeColor = Highlighting.StudioTheme.BracketMatch;
            canvas.StrokeSize  = 1.5f;
            float x = gw + pos.Column * cw;
            canvas.DrawRectangle(x, y + 1, cw, lh - 2);
        }
        Highlight(match.Open);
        Highlight(match.Close);
    }
}


public sealed record DiagnosticSpan(int Line, int StartCol, int EndCol, bool IsError, string Message);

public class StudioTheme
{
    internal static readonly StudioTheme Self = new();
}