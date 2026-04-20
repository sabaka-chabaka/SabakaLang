namespace SabakaLang.Studio.Elements;

public class EditorDrawable : IDrawable
{
    public string Text { get; set; } = "";
    public float FontSize { get; set; } = 14;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.FillColor = Colors.Black;
        canvas.FillRectangle(dirtyRect);

        canvas.FontColor = Colors.White;
        canvas.Font = new Microsoft.Maui.Graphics.Font("JetMonoRegular");
        canvas.FontSize = FontSize;

        var lines = Text.Split('\n');
        float lineHeight = FontSize * 1.2f;

        for (int i = 0; i < lines.Length; i++)
        {
            canvas.DrawString(lines[i], 10, (i + 1) * lineHeight, HorizontalAlignment.Left);
        }
    }
}