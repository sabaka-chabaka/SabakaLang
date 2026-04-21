using Microsoft.Maui.Controls;
using SabakaLang.Studio.Editor.Core;
using SabakaLang.Studio.Editor.Highlighting;

namespace SabakaLang.Studio.Editor.Input;

public sealed class MouseHandler
{
    private readonly EditorDocument  _doc;
    private readonly EditorSelection _sel;

    private bool  _dragging;
    private float _scrollY;
    private float _scrollX;

    public event Action<CaretPosition>? CaretMoved;
    public event Action<float>?         ScrollRequested; // delta Y
    public event Action<string, CaretPosition>? ContextMenuRequested;

    public MouseHandler(EditorDocument doc, EditorSelection sel)
    {
        _doc = doc; _sel = sel;
    }

    public void SetScroll(float scrollY, float scrollX)
    {
        _scrollY = scrollY; _scrollX = scrollX;
    }

    public CaretPosition HitTest(float x, float y)
    {
        float lh = StudioTheme.LineHeight;
        float gw = StudioTheme.GutterWidth;
        float cw = StudioTheme.CharWidth;

        var line = Math.Clamp((int)((y + _scrollY) / lh), 0, _doc.LineCount - 1);
        var col  = (int)Math.Round((x - gw + _scrollX) / cw);
        col = Math.Clamp(col, 0, _doc.GetLine(line).Length);
        return new CaretPosition(line, col);
    }

    public CaretPosition OnPointerDown(float x, float y, bool shift, bool isDouble)
    {
        var pos = HitTest(x, y);

        if (isDouble)
        {
            var (start, end) = _doc.WordAt(pos);
            _sel.Anchor = start;
            _sel.Active = end;
            CaretMoved?.Invoke(end);
            return end;
        }

        if (shift)
            _sel.Extend(pos);
        else
            _sel.Clear(pos);

        _dragging = true;
        CaretMoved?.Invoke(pos);
        return pos;
    }

    public CaretPosition? OnPointerMove(float x, float y)
    {
        if (!_dragging) return null;
        var pos = HitTest(x, y);
        _sel.Extend(pos);
        CaretMoved?.Invoke(pos);
        return pos;
    }
    
    public void OnPointerUp() => _dragging = false;

    public void OnScroll(float deltaY) => ScrollRequested?.Invoke(deltaY);

    public void OnRightClick(float x, float y)
    {
        var pos = HitTest(x, y);
        ContextMenuRequested?.Invoke("editor", pos);
    }
}