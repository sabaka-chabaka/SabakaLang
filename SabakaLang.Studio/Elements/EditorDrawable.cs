namespace SabakaLang.Studio.Elements;

public class EditorDrawable : IDrawable
{
    public EditorEngine? Engine { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (Engine == null) return;
        
        canvas.FillColor = Color.FromArgb("#191a1c");
        canvas.FillRectangle(dirtyRect);

        canvas.Font = new Microsoft.Maui.Graphics.Font("JetBrainsMono");
        canvas.FontSize = 14;
        canvas.FontColor = Colors.White;

        float lineHeight = 18f;
        float charWidth = 8.4f;

        if (Engine.HasSelection)
        {
            var startRow = Engine.SelectionStartRow;
            var startCol = Engine.SelectionStartCol;
            var endRow = Engine.CursorRow;
            var endCol = Engine.CursorCol;

            if (startRow > endRow || (startRow == endRow && startCol > endCol))
            {
                (startRow, endRow) = (endRow, startRow);
                (startCol, endCol) = (endCol, startCol);
            }

            canvas.FillColor = Color.FromRgba(255, 255, 255, 50);
            for (int r = startRow; r <= endRow; r++)
            {
                float y = r * lineHeight + 4;
                float xStart = (r == startRow) ? (10 + startCol * charWidth) : 10;
                float xEnd = (r == endRow) ? (10 + endCol * charWidth) : (10 + Engine.Lines[r].Length * charWidth + 5);
                canvas.FillRectangle(xStart, y, xEnd - xStart, lineHeight);
            }
        }

        for (int i = 0; i < Engine.Lines.Count; i++)
        {
            canvas.DrawString(Engine.Lines[i].ToString(), 10, (i + 1) * lineHeight, HorizontalAlignment.Left);
        }

        canvas.FillColor = Colors.Orange;
        float cursorX = 10 + (Engine.CursorCol * charWidth);
        float cursorY = (Engine.CursorRow * lineHeight) + 4;
        canvas.FillRectangle(cursorX, cursorY, 2, lineHeight);
    }
}
