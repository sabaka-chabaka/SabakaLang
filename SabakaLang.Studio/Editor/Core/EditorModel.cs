namespace SabakaLang.Studio.Editor.Core;

public sealed class EditorModel
{
    public  readonly TextBuffer Buffer    = new();
    public  readonly Selection  Selection = new();
    private readonly UndoStack  _undo     = new();

    public event Action? Changed;

    public TextPosition Caret
    {
        get => Selection.Caret;
        set => Selection.SetCaret(value);
    }

    public string Text
    {
        get => Buffer.FullText();
        set { Buffer.SetText(value); _undo.Clear(); Selection.SetCaret(TextPosition.Zero); Notify(); }
    }

    public void TypeChar(string text)
    {
        DeleteSelectionIfAny();
        var from  = Selection.Caret;
        var after = Buffer.Insert(from, text);
        var edit  = Edit.Insert(from, text, after);
        _undo.Push(edit);
        Selection.SetCaret(after);
        Notify();
    }

    public void Backspace()
    {
        if (!Selection.IsEmpty) { DeleteSelectionIfAny(); Notify(); return; }
        var caret = Selection.Caret;
        TextPosition from;
        if (caret.Col > 0)
            from = new(caret.Row, caret.Col - 1);
        else if (caret.Row > 0)
            from = new(caret.Row - 1, Buffer.LineLength(caret.Row - 1));
        else
            return;
        var removed = Buffer.Extract(from, caret);
        var after   = Buffer.Delete(from, caret);
        _undo.Push(Edit.Delete(from, caret, removed, from));
        Selection.SetCaret(after);
        Notify();
    }

    public void BackspaceWord()
    {
        if (!Selection.IsEmpty) { DeleteSelectionIfAny(); Notify(); return; }
        var caret = Selection.Caret;
        var (start, _) = Buffer.WordAt(caret);
        var from = start.Col < caret.Col ? start
            : caret.Col > 0 ? new(caret.Row, 0)
            : caret.Row > 0 ? new(caret.Row - 1, Buffer.LineLength(caret.Row - 1))
            : caret;
        if (from == caret) return;
        var removed = Buffer.Extract(from, caret);
        Buffer.Delete(from, caret);
        _undo.Push(Edit.Delete(from, caret, removed, from));
        Selection.SetCaret(from);
        Notify();
    }

    public void DeleteForward()
    {
        if (!Selection.IsEmpty) { DeleteSelectionIfAny(); Notify(); return; }
        var caret = Selection.Caret;
        TextPosition to;
        if (caret.Col < Buffer.LineLength(caret.Row))
            to = new(caret.Row, caret.Col + 1);
        else if (caret.Row < Buffer.LineCount - 1)
            to = new(caret.Row + 1, 0);
        else
            return;
        var removed = Buffer.Extract(caret, to);
        Buffer.Delete(caret, to);
        _undo.Push(Edit.Delete(caret, to, removed, caret));
        Notify();
    }

    public void DeleteLine()
    {
        var row = Selection.Caret.Row;
        TextPosition from, to;
        if (row < Buffer.LineCount - 1)
        {
            from = new(row, 0);
            to   = new(row + 1, 0);
        }
        else if (row > 0)
        {
            from = new(row - 1, Buffer.LineLength(row - 1));
            to   = new(row, Buffer.LineLength(row));
        }
        else
        {
            Buffer.SetText("");
            Selection.SetCaret(TextPosition.Zero);
            _undo.Clear();
            Notify();
            return;
        }
        var removed = Buffer.Extract(from, to);
        Buffer.Delete(from, to);
        _undo.Push(Edit.Delete(from, to, removed, from));
        Selection.SetCaret(Buffer.Clamp(from));
        Notify();
    }

    public void Undo()
    {
        if (!_undo.CanUndo) return;
        var e   = _undo.PopUndo();
        var inv = e.Inverse();
        if (inv.IsInsert) Buffer.Insert(inv.From, inv.Text);
        else              Buffer.Delete(inv.From, inv.To);
        _undo.PushRedo(e);
        Selection.SetCaret(e.From);
        Notify();
    }

    public void Redo()
    {
        if (!_undo.CanRedo) return;
        var e = _undo.PopRedo();
        TextPosition after;
        if (e.IsInsert) after = Buffer.Insert(e.From, e.Text);
        else            after = Buffer.Delete(e.From, e.To);
        _undo.Push(e);
        Selection.SetCaret(after);
        Notify();
    }
    
    public string GetSelectedText()
    {
        if (Selection.IsEmpty) return "";
        var (s, e) = Selection.Ordered;
        return Buffer.Extract(s, e);
    }

    public async void Paste()
    {
        try
        {
            var text = await Clipboard.GetTextAsync();
            if (!string.IsNullOrEmpty(text)) TypeChar(text);
        }
        catch { /* ignored */ }
    }
    
    public void IndentSelection(bool unindent)
    {
        var (s, e) = Selection.Ordered;
        for (var r = s.Row; r <= e.Row; r++)
        {
            if (unindent)
            {
                var ln = Buffer.Line(r);
                if (ln.StartsWith("    "))
                    Buffer.Delete(new(r, 0), new(r, 4));
                else if (ln.StartsWith("\t"))
                    Buffer.Delete(new(r, 0), new(r, 1));
            }
            else
            {
                Buffer.Insert(new(r, 0), "    ");
            }
        }
        Notify();
    }
    
    public void ToggleComment()
    {
        var (s, e) = Selection.IsEmpty
            ? (new TextPosition(Caret.Row, 0), new TextPosition(Caret.Row, 0))
            : Selection.Ordered;

        for (var r = s.Row; r <= e.Row; r++)
        {
            var ln  = Buffer.Line(r);
            var idx = ln.IndexOf("//", StringComparison.Ordinal);
            if (idx >= 0 && ln[..idx].All(char.IsWhiteSpace))
                Buffer.Delete(new(r, idx), new(r, idx + 2));
            else
                Buffer.Insert(new(r, 0), "//");
        }
        Notify();
    }

    public void DuplicateLine()
    {
        var row = Caret.Row;
        var ln  = Buffer.Line(row);
        Buffer.Insert(new(row, ln.Length), "\n" + ln);
        Selection.SetCaret(new(row + 1, Caret.Col));
        Notify();
    }

    public void MoveLine(int delta)
    {
        var row = Caret.Row;
        var target = row + delta;
        if (target < 0 || target >= Buffer.LineCount) return;

        var a = Buffer.Line(row);
        var b = Buffer.Line(target);

        Buffer.Delete(new(row, 0),    new(row, a.Length));
        Buffer.Insert(new(row, 0),    b);
        Buffer.Delete(new(target, 0), new(target, b.Length));
        Buffer.Insert(new(target, 0), a);

        Selection.SetCaret(new(target, Math.Min(Caret.Col, a.Length)));
        Notify();
    }

    public int ReplaceAll(string needle, string replacement, bool caseSensitive = false)
    {
        var hits = Buffer.FindAll(needle, caseSensitive);
        for (var i = hits.Count - 1; i >= 0; i--)
        {
            var p  = hits[i];
            var to = new TextPosition(p.Row, p.Col + needle.Length);
            Buffer.Delete(p, to);
            Buffer.Insert(p, replacement);
        }
        Notify();
        return hits.Count;
    }

    private void DeleteSelectionIfAny()
    {
        if (Selection.IsEmpty) return;
        var (s, e) = Selection.Ordered;
        var removed = Buffer.Extract(s, e);
        Buffer.Delete(s, e);
        _undo.Push(Edit.Delete(s, e, removed, s));
        Selection.SetCaret(s);
    }

    public void Notify() => Changed?.Invoke();
}