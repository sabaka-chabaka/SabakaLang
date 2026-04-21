using SabakaLang.Studio.Editor.Core;
using SabakaLang.Studio.Editor.Highlighting;

namespace SabakaLang.Studio.Editor.Gutter;

public sealed class GutterView : GraphicsView, IDrawable
{
    private EditorDocument? _doc;
    private int             _caretLine;
    private float           _scrollY;
    private HashSet<int>    _breakpoints = new();
    private Dictionary<int, GutterMarker> _gitMarkers = new();
    
    public event Action<int>? BreakpointToggled;
 
    public GutterView()
    {
        Drawable          = this;
        BackgroundColor   = StudioTheme.GutterBackground;
        WidthRequest      = StudioTheme.GutterWidth;
    }
    
    public void Update(EditorDocument doc, int caretLine, float scrollY,
        Dictionary<int, GutterMarker>? gitMarkers = null)
    {
        _doc         = doc;
        _caretLine   = caretLine;
        _scrollY     = scrollY;
        if (gitMarkers is not null) _gitMarkers = gitMarkers;
        Invalidate();
    }
 
    public void ToggleBreakpoint(int line)
    {
        if (!_breakpoints.Add(line)) _breakpoints.Remove(line);
        Invalidate();
    }
    
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) =>
        {
            var pt = e.GetPosition(this);
            if (pt is null) return;
            var line = (int)((pt.Value.Y + _scrollY) / StudioTheme.LineHeight);
            ToggleBreakpoint(line);
            BreakpointToggled?.Invoke(line);
        };
        GestureRecognizers.Add(tap);
    }
    
    public void Draw(ICanvas canvas, RectF dirty)
    {
        if (_doc is null) return;
        float lh = StudioTheme.LineHeight;
        float gw = StudioTheme.GutterWidth;
 
        canvas.FillColor = StudioTheme.GutterBackground;
        canvas.FillRectangle(dirty);
 
        var first = Math.Max(0, (int)(_scrollY / lh));
        var last  = Math.Min(_doc.LineCount - 1, (int)((_scrollY + dirty.Height) / lh) + 1);
 
        canvas.Font     = new Microsoft.Maui.Graphics.Font(StudioTheme.FontFamily);
        canvas.FontSize = StudioTheme.FontSize - 1;
 
        for (var li = first; li <= last; li++)
        {
            float y = li * lh - _scrollY;
 
            if (_gitMarkers.TryGetValue(li, out var marker))
            {
                canvas.FillColor = marker switch
                {
                    GutterMarker.Added    => StudioTheme.GitAdded,
                    GutterMarker.Modified => StudioTheme.GitModified,
                    GutterMarker.Deleted  => StudioTheme.GitDeleted,
                    _                     => Colors.Transparent
                };
                canvas.FillRectangle(0, y, 3, lh);
            }
 
            if (_breakpoints.Contains(li))
            {
                canvas.FillColor = StudioTheme.DiagError;
                canvas.FillEllipse(4, y + (lh - 10) / 2, 10, 10);
            }
 
            var isActive = li == _caretLine;
            canvas.FontColor = isActive ? StudioTheme.CurrentLineNumber : StudioTheme.GutterForeground;
            canvas.DrawString((li + 1).ToString(), gw - 8, y + lh - 4, HorizontalAlignment.Right);
        }
 
        canvas.StrokeColor = StudioTheme.Border;
        canvas.StrokeSize  = 1;
        canvas.DrawLine(gw - 1, 0, gw - 1, dirty.Height);
    }
}

public enum GutterMarker { None, Added, Modified, Deleted }