using System.Text;

namespace SabakaLang.Studio.Editor.Core;

public class EditorDocument
{
    private readonly List<string> _lines = [""];
    private readonly UndoRedoStack _history = new();
    private readonly Lock _lock = new();
    
    public event Action? TextChanged;
    
    public int LineCount { get{ lock (_lock) return _lines.Count; } }
    
    public string GetLine(int index)
    {
        lock (_lock)
            return (uint)index < (uint)_lines.Count ? _lines[index] : string.Empty;
    }
 
    public string GetText()
    {
        lock (_lock)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < _lines.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(_lines[i]);
            }
            return sb.ToString();
        }
    }
 
    public void SetText(string text)
    {
        lock (_lock) { _history.Clear(); LoadLines(text); }
        TextChanged?.Invoke();
    }
 
    public CaretPosition Insert(CaretPosition caret, string text)
    {
        var change = TextChange.ForInsert(caret, text);
        Apply(change, record: true);
        return change.NewEnd;
    }
 
    public CaretPosition Delete(CaretPosition from, CaretPosition to)
    {
        if (from == to) return from;
        if (to < from) (from, to) = (to, from);
        string deleted;
        lock (_lock) deleted = ExtractText(from, to);
        var change = TextChange.ForDelete(from, to, deleted);
        Apply(change, record: true);
        return from;
    }
 
    public CaretPosition Backspace(CaretPosition caret)
    {
        lock (_lock)
        {
            if (caret.Column > 0)
                return Delete(new CaretPosition(caret.Line, caret.Column - 1), caret);
            if (caret.Line == 0) return caret;
            var prevCol = _lines[caret.Line - 1].Length;
            return Delete(new CaretPosition(caret.Line - 1, prevCol), caret);
        }
    }
 
    public CaretPosition DeleteForward(CaretPosition caret)
    {
        lock (_lock)
        {
            if (caret.Column < _lines[caret.Line].Length)
                return Delete(caret, new CaretPosition(caret.Line, caret.Column + 1));
            if (caret.Line >= _lines.Count - 1) return caret;
            return Delete(caret, new CaretPosition(caret.Line + 1, 0));
        }
    }
 
    public CaretPosition? Undo()
    {
        lock (_lock)
        {
            if (!_history.CanUndo) return null;
            var change = _history.PopUndo();
            var inv = change.Inverse();
            if (inv.IsInsert) ApplyInsert(inv.From, inv.Text);
            else              ApplyDelete(inv.From, inv.To);
            _history.PushRedo(change);
            TextChanged?.Invoke();
            return change.From;
        }
    }
 
    public CaretPosition? Redo()
    {
        lock (_lock)
        {
            if (!_history.CanRedo) return null;
            var change = _history.PopRedo();
            if (change.IsInsert) ApplyInsert(change.From, change.Text);
            else                 ApplyDelete(change.From, change.To);
            _history.Push(change);
            TextChanged?.Invoke();
            return change.NewEnd;
        }
    }
 
    private void Apply(TextChange change, bool record)
    {
        lock (_lock)
        {
            if (change.IsInsert) ApplyInsert(change.From, change.Text);
            else                 ApplyDelete(change.From, change.To);
            if (record) _history.Push(change);
        }
        TextChanged?.Invoke();
    }
 
    private void ApplyInsert(CaretPosition at, string text)
    {
        var parts  = text.Split('\n');
        var line   = _lines[at.Line];
        var before = line[..at.Column];
        var after  = line[at.Column..];
        if (parts.Length == 1)
        {
            _lines[at.Line] = before + parts[0] + after;
        }
        else
        {
            _lines[at.Line] = before + parts[0];
            for (var i = 1; i < parts.Length - 1; i++)
                _lines.Insert(at.Line + i, parts[i]);
            _lines.Insert(at.Line + parts.Length - 1, parts[^1] + after);
        }
    }
 
    private void ApplyDelete(CaretPosition from, CaretPosition to)
    {
        if (from.Line == to.Line)
        {
            var line = _lines[from.Line];
            _lines[from.Line] = line[..from.Column] + line[to.Column..];
        }
        else
        {
            _lines[from.Line] = _lines[from.Line][..from.Column] + _lines[to.Line][to.Column..];
            _lines.RemoveRange(from.Line + 1, to.Line - from.Line);
        }
    }
 
    private string ExtractText(CaretPosition from, CaretPosition to)
    {
        if (from.Line == to.Line)
            return _lines[from.Line][from.Column..to.Column];
        var sb = new StringBuilder();
        sb.Append(_lines[from.Line][from.Column..]);
        for (var i = from.Line + 1; i < to.Line; i++) { sb.Append('\n'); sb.Append(_lines[i]); }
        sb.Append('\n'); sb.Append(_lines[to.Line][..to.Column]);
        return sb.ToString();
    }
 
    private void LoadLines(string text)
    {
        _lines.Clear();
        foreach (var l in text.Replace("\r\n", "\n").Split('\n')) _lines.Add(l);
        if (_lines.Count == 0) _lines.Add("");
    }
 
    public CaretPosition Clamp(CaretPosition pos)
    {
        lock (_lock)
        {
            var line = Math.Clamp(pos.Line, 0, _lines.Count - 1);
            var col  = Math.Clamp(pos.Column, 0, _lines[line].Length);
            return new CaretPosition(line, col);
        }
    }
 
    public (CaretPosition Start, CaretPosition End) WordAt(CaretPosition pos)
    {
        lock (_lock)
        {
            var line = GetLine(pos.Line);
            if (line.Length == 0) return (pos, pos);
            var col = Math.Clamp(pos.Column, 0, Math.Max(0, line.Length - 1));
            static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';
            var start = col; while (start > 0 && IsWord(line[start - 1])) start--;
            var end   = col; while (end < line.Length && IsWord(line[end])) end++;
            return (new CaretPosition(pos.Line, start), new CaretPosition(pos.Line, end));
        }
    }
 
    public IReadOnlyList<CaretPosition> FindAll(string needle, bool caseSensitive = false)
    {
        var cmp  = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var hits = new List<CaretPosition>();
        lock (_lock)
        {
            for (var li = 0; li < _lines.Count; li++)
            {
                var ln = _lines[li]; var idx = 0;
                while (true)
                {
                    idx = ln.IndexOf(needle, idx, cmp);
                    if (idx < 0) break;
                    hits.Add(new CaretPosition(li, idx));
                    idx += needle.Length;
                }
            }
        }
        return hits;
    }
 
    public int ReplaceAll(string needle, string replacement, bool caseSensitive = false)
    {
        var hits = FindAll(needle, caseSensitive);
        for (var i = hits.Count - 1; i >= 0; i--)
        {
            var p  = hits[i];
            var to = new CaretPosition(p.Line, p.Column + needle.Length);
            Delete(p, to);
            Insert(p, replacement);
        }
        return hits.Count;
    }
}