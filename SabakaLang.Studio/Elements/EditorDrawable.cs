namespace SabakaLang.Studio.Elements;

public class EditorDrawable : IDrawable
{
    public EditorEngine Engine { get; set; }

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
