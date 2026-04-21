using Microsoft.Maui.Controls;
using SabakaLang.Studio.Editor.Core;
using SabakaLang.Studio.Editor.Highlighting;
using SabakaLang.Studio.Editor.Intellisense;

namespace SabakaLang.Studio.Editor.Input;

public sealed class InputHandler
{
    private readonly EditorModel     _model;
    private readonly CompletionState _completion;

    private static readonly Dictionary<char, char> AutoClose = new()
        { ['{'] = '}', ['('] = ')', ['['] = ']', ['"'] = '"', ['\''] = '\'' };

    private static readonly HashSet<char> OverType = new() { '}', ')', ']', '"', '\'' };

    public bool IsDirty { get; private set; }

    public event Action? ScrollToCaret;
    public event Action? TriggerCompletion;
    public event Action? TriggerSignature;
    public event Action? FindRequested;
    public event Action? SaveRequested;
    public event Action? PaletteRequested;

    public InputHandler(EditorModel model, CompletionState completion)
    {
        _model      = model;
        _completion = completion;
        _model.Changed += () => { IsDirty = true; ScrollToCaret?.Invoke(); };
    }

    public bool OnKeyDown(string key, bool ctrl, bool shift, bool alt)
    {
        if (_completion.IsVisible)
        {
            switch (key)
            {
                case "Down":   _completion.MoveDown(); return true;
                case "Up":     _completion.MoveUp();   return true;
                case "Tab":
                case "Return": CommitCompletion();      return true;
                case "Escape": _completion.Hide();      return true;
            }
        }

        if (ctrl && !alt)
        {
            switch (key)
            {
                case "Z":       _model.Undo();              return true;
                case "Y":       _model.Redo();              return true;
                case "C":       CopyToClipboard();          return true;
                case "X":       CutToClipboard();           return true;
                case "V":       _model.Paste();             return true;
                case "A":       SelectAll();                return true;
                case "D":       _model.DuplicateLine();     return true;
                case "K":       _model.DeleteLine();        return true;
                case "Slash":   _model.ToggleComment();     return true;
                case "OEM_2":   _model.ToggleComment();     return true;  // also /
                case "S":       SaveRequested?.Invoke();    return true;
                case "F":       FindRequested?.Invoke();    return true;
                case "P":       if (shift) { PaletteRequested?.Invoke(); return true; } break;
                case "Space":   TriggerCompletion?.Invoke(); return true;
                case "Back":    _model.BackspaceWord();     return true;

                case "Left":    WordMove(-1, shift);        return true;
                case "Right":   WordMove(+1, shift);        return true;
                case "Home":    GotoDocStart(shift);        return true;
                case "End":     GotoDocEnd(shift);          return true;
                case "Up":      _model.MoveLine(-1);        return true;
                case "Down":    _model.MoveLine(+1);        return true;
            }
        }

        if (shift && !ctrl && !alt)
        {
            switch (key)
            {
                case "Tab": _model.IndentSelection(unindent: true); return true;
            }
        }

        switch (key)
        {
            case "Return": HandleEnter();            return true;
            case "Back":   _model.Backspace();       return true;
            case "Delete": _model.DeleteForward();   return true;
            case "Tab":    HandleTab(shift);         return true;
            case "Escape": _completion.Hide();       return true;

            case "Left":   MoveCaret(-1, 0, shift);  return true;
            case "Right":  MoveCaret(+1, 0, shift);  return true;
            case "Up":     MoveCaret(0, -1, shift);  return true;
            case "Down":   MoveCaret(0, +1, shift);  return true;
            case "Home":   GotoLineStart(shift);     return true;
            case "End":    GotoLineEnd(shift);       return true;
            case "Prior":  PageMove(-1, shift);      return true;  // PageUp
            case "Next":   PageMove(+1, shift);      return true;  // PageDown
        }

        return false;
    }

    public void OnChar(char ch)
    {
        if (OverType.Contains(ch))
        {
            var caret = _model.Caret;
            var ln    = _model.Buffer.Line(caret.Row);
            if (caret.Col < ln.Length && ln[caret.Col] == ch)
            {
                _model.Selection.SetCaret(new(caret.Row, caret.Col + 1));
                _model.Notify();
                return;
            }
        }

        if (!_model.Selection.IsEmpty)
        {
            if (AutoClose.TryGetValue(ch, out var close))
            {
                var (s, e) = _model.Selection.Ordered;
                var selected = _model.GetSelectedText();
                _model.TypeChar(ch + selected + close);
                return;
            }
        }

        _model.TypeChar(ch.ToString());

        if (AutoClose.TryGetValue(ch, out var closePair))
        {
            var pos = _model.Caret;
            _model.Buffer.Insert(pos, closePair.ToString());
        }

        if (ch == '.' || ch == ':')
            TriggerCompletion?.Invoke();

        if (ch == '(' || ch == ',')
            TriggerSignature?.Invoke();
    }

    private bool _mouseDown;

    public void OnPointerPressed(float px, float py, bool shift)
    {
        _mouseDown = true;
        var pos = HitTest(px, py);
        if (shift) _model.Selection.Extend(pos);
        else        _model.Selection.SetCaret(pos);
        _model.Notify();
    }

    public void OnPointerMoved(float px, float py)
    {
        if (!_mouseDown) return;
        var pos = HitTest(px, py);
        _model.Selection.Extend(pos);
        _model.Notify();
    }

    public void OnPointerReleased() => _mouseDown = false;

    public void OnDoubleTap(float px, float py)
    {
        var pos = HitTest(px, py);
        var (s, e) = _model.Buffer.WordAt(pos);
        _model.Selection.Anchor = s;
        _model.Selection.Extend(e);
        _model.Notify();
    }

    public event Action<float>? ScrollDelta;

    public void OnScroll(float deltaLines) => ScrollDelta?.Invoke(deltaLines);

    private void HandleEnter()
    {
        _completion.Hide();
        var caret  = _model.Caret;
        var indent = _model.Buffer.LeadingWhitespace(caret.Row);

        var line   = _model.Buffer.Line(caret.Row);
        var col    = caret.Col;
        if (col > 0 && line[col - 1] == '{')
            indent += "    ";

        _model.TypeChar("\n" + indent);
    }

    private void HandleTab(bool shift)
    {
        if (!_model.Selection.IsEmpty)
        {
            _model.IndentSelection(shift);
            return;
        }
        if (shift)
        {
            var ln = _model.Buffer.Line(_model.Caret.Row);
            if (ln.StartsWith("    "))
            {
                _model.Buffer.Delete(new(_model.Caret.Row, 0), new(_model.Caret.Row, 4));
                _model.Selection.SetCaret(new(_model.Caret.Row, Math.Max(0, _model.Caret.Col - 4)));
                _model.Notify();
            }
        }
        else
        {
            _model.TypeChar("    ");
        }
    }

    private void MoveCaret(int dCol, int dRow, bool shift)
    {
        var buf    = _model.Buffer;
        var caret  = _model.Selection.Caret;

        if (dRow != 0)
        {
            var newRow = Math.Clamp(caret.Row + dRow, 0, buf.LineCount - 1);
            var newCol = Math.Clamp(caret.Col, 0, buf.LineLength(newRow));
            Apply(new(newRow, newCol), shift);
        }
        else
        {
            var ln = buf.Line(caret.Row);
            if (dCol < 0)
            {
                if (caret.Col > 0)       Apply(new(caret.Row, caret.Col - 1), shift);
                else if (caret.Row > 0)  Apply(new(caret.Row - 1, buf.LineLength(caret.Row - 1)), shift);
            }
            else
            {
                if (caret.Col < ln.Length) Apply(new(caret.Row, caret.Col + 1), shift);
                else if (caret.Row < buf.LineCount - 1) Apply(new(caret.Row + 1, 0), shift);
            }
        }
    }

    private void WordMove(int dir, bool shift)
    {
        var caret = _model.Selection.Caret;
        if (dir < 0)
        {
            var (s, _) = _model.Buffer.WordAt(caret);
            var nxt = s.Col < caret.Col ? s
                : caret.Col > 0 ? new(caret.Row, 0)
                : caret.Row > 0 ? new(caret.Row - 1, _model.Buffer.LineLength(caret.Row - 1))
                : caret;
            Apply(nxt, shift);
        }
        else
        {
            var (_, e) = _model.Buffer.WordAt(caret);
            var ln  = _model.Buffer.Line(caret.Row);
            var nxt = e.Col > caret.Col ? e
                : caret.Col < ln.Length ? new(caret.Row, ln.Length)
                : caret.Row < _model.Buffer.LineCount - 1 ? new(caret.Row + 1, 0)
                : caret;
            Apply(nxt, shift);
        }
    }

    private void GotoLineStart(bool shift)
    {
        var row    = _model.Selection.Caret.Row;
        var ln     = _model.Buffer.Line(row);
        var indent = 0;
        while (indent < ln.Length && (ln[indent] == ' ' || ln[indent] == '\t')) indent++;
        var col = _model.Selection.Caret.Col == indent ? 0 : indent;
        Apply(new(row, col), shift);
    }

    private void GotoLineEnd(bool shift)
    {
        var row = _model.Selection.Caret.Row;
        Apply(new(row, _model.Buffer.LineLength(row)), shift);
    }

    private void GotoDocStart(bool shift) => Apply(TextPosition.Zero, shift);

    private void GotoDocEnd(bool shift)
    {
        var last = _model.Buffer.LineCount - 1;
        Apply(new(last, _model.Buffer.LineLength(last)), shift);
    }

    private void PageMove(int dir, bool shift)
    {
        var caret = _model.Selection.Caret;
        var delta = 20 * dir;
        var newRow = Math.Clamp(caret.Row + delta, 0, _model.Buffer.LineCount - 1);
        Apply(new(newRow, Math.Min(caret.Col, _model.Buffer.LineLength(newRow))), shift);
    }

    private void Apply(TextPosition pos, bool shift)
    {
        if (shift) _model.Selection.Extend(pos);
        else        _model.Selection.SetCaret(pos);
        _model.Notify();
    }

    private void SelectAll()
    {
        var last = _model.Buffer.LineCount - 1;
        _model.Selection.SelectAll(new(last, _model.Buffer.LineLength(last)));
        _model.Notify();
    }

    private async void CopyToClipboard()
    {
        try
        {
            var txt = _model.GetSelectedText();
            if (txt.Length > 0) await Clipboard.SetTextAsync(txt);
        }
        catch { /* ignored */ }
    }

    private async void CutToClipboard()
    {
        try
        {
            var txt = _model.GetSelectedText();
            if (txt.Length > 0)
            {
                await Clipboard.SetTextAsync(txt);
                _model.TypeChar(""); // deletes selection
            }
        }
        catch { /* ignored */ }
    }

    private void CommitCompletion()
    {
        var item = _completion.Selected;
        if (item is null) { _completion.Hide(); return; }

        var caret = _model.Caret;
        var (start, _) = _model.Buffer.WordAt(caret);
        if (start < caret)
            _model.Buffer.Delete(start, caret);

        _model.Buffer.Insert(start, item.InsertText);
        var after = new TextPosition(start.Row, start.Col + item.InsertText.Length);
        _model.Selection.SetCaret(after);
        _completion.Hide();
        _model.Notify();
    }

    private float _scrollRow;
    public void SetScroll(float scrollRow) => _scrollRow = scrollRow;

    public TextPosition HitTest(float px, float py)
    {
        var row = (int)((py + _scrollRow * Theme.LineHeight) / Theme.LineHeight);
        row = Math.Clamp(row, 0, _model.Buffer.LineCount - 1);
        var col = (int)Math.Round((px - Theme.GutterW) / Theme.CharWidth);
        col = Math.Clamp(col, 0, _model.Buffer.LineLength(row));
        return new(row, col);
    }
}

public static class ActionExt
{
    public static void Invoke_Safe(this Action? a) => a?.Invoke();
}