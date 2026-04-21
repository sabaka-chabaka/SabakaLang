namespace SabakaLang.Studio.Editor.Core;

public readonly record struct CaretPosition(int Line, int Column) : IComparable<CaretPosition>
{
    public static readonly CaretPosition Zero = new(0, 0);

    public int CompareTo(CaretPosition other)
    {
        var cmp = Line.CompareTo(other.Line);
        return cmp != 0 ? cmp : Column.CompareTo(other.Column);
    }
    
    public static bool operator <(CaretPosition a, CaretPosition b)  => a.CompareTo(b) < 0;
    public static bool operator >(CaretPosition a, CaretPosition b)  => a.CompareTo(b) > 0;
    public static bool operator <=(CaretPosition a, CaretPosition b) => a.CompareTo(b) <= 0;
    public static bool operator >=(CaretPosition a, CaretPosition b) => a.CompareTo(b) >= 0;
 
    public override string ToString() => $"L{Line}:C{Column}";
}

public sealed class EditorSelection
{
    public CaretPosition Anchor { get; set; } = CaretPosition.Zero;
    public CaretPosition Active { get; set; } = CaretPosition.Zero;
    
    public bool IsEmpty => Anchor == Active;
    
    public (CaretPosition Start, CaretPosition End) Ordered =>
        Anchor <= Active ? (Anchor, Active) : (Active, Anchor);

    public bool ContainsLine(int line)
    {
        var (s, e) = Ordered;
        return line >= s.Line && line <= e.Line;
    }
    
    public void Clear(CaretPosition pos) { Anchor = pos; Active = pos; }
    public void Extend(CaretPosition pos) { Active = pos; }
 
    public void SelectAll(int lastLine, int lastCol)
    {
        Anchor = CaretPosition.Zero;
        Active = new CaretPosition(lastLine, lastCol);
    }
 
    public EditorSelection Clone() => new() { Anchor = Anchor, Active = Active };
}

public sealed class TextChange
{
    public CaretPosition From { get; }
    public CaretPosition To   { get; }
    public string Text        { get; }
    public bool IsInsert      { get; }
 
    public CaretPosition NewEnd { get; }
 
    private TextChange(CaretPosition from, CaretPosition to, string text, bool isInsert, CaretPosition newEnd)
    {
        From = from; To = to; Text = text; IsInsert = isInsert; NewEnd = newEnd;
    }
 
    public static TextChange ForInsert(CaretPosition at, string text)
    {
        var lines = text.Split('\n');
        var newEnd = lines.Length == 1
            ? new CaretPosition(at.Line, at.Column + text.Length)
            : new CaretPosition(at.Line + lines.Length - 1, lines[^1].Length);
        return new(at, at, text, true, newEnd);
    }
 
    public static TextChange ForDelete(CaretPosition from, CaretPosition to, string deletedText)
        => new(from, to, deletedText, false, from);
 
    public TextChange Inverse() =>
        IsInsert
            ? ForDelete(From, NewEnd, Text)
            : ForInsert(From, Text);
}

public sealed class UndoRedoStack
{
    private readonly Stack<TextChange> _undo = new();
    private readonly Stack<TextChange> _redo = new();
 
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
 
    public void Push(TextChange change) { _undo.Push(change); _redo.Clear(); }
    public TextChange PopUndo() => _undo.Pop();
    public TextChange PopRedo() => _redo.Pop();
 
    public void PushRedo(TextChange change) => _redo.Push(change);
 
    public void Clear() { _undo.Clear(); _redo.Clear(); }
}