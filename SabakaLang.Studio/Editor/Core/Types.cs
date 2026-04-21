namespace SabakaLang.Studio.Editor.Core;

public readonly record struct TextPosition(int Row, int Col) : IComparable<TextPosition>
{
    public static readonly TextPosition Zero = new(0, 0);

    public int CompareTo(TextPosition o)
    {
        var c = Row.CompareTo(o.Row);
        return c != 0 ? c : Col.CompareTo(o.Col);
    }

    public static bool operator <(TextPosition a, TextPosition b) => a.CompareTo(b) < 0;
    public static bool operator >(TextPosition a, TextPosition b) => a.CompareTo(b) > 0;
    public static bool operator <=(TextPosition a, TextPosition b) => a.CompareTo(b) <= 0;
    public static bool operator >=(TextPosition a, TextPosition b) => a.CompareTo(b) >= 0;

    public override string ToString() => $"{Row}:{Col}";
}

public sealed class Selection
{
    public TextPosition Anchor { get; set; } = TextPosition.Zero;
    public TextPosition Caret  { get; set; } = TextPosition.Zero;

    public bool IsEmpty => Anchor == Caret;

    public (TextPosition Start, TextPosition End) Ordered =>
        Anchor <= Caret ? (Anchor, Caret) : (Caret, Anchor);

    public void SetCaret(TextPosition p) { Anchor = p; Caret = p; }
    public void Extend(TextPosition p)   { Caret  = p; }

    public void SelectAll(TextPosition last)
    {
        Anchor = TextPosition.Zero;
        Caret  = last;
    }

    public bool CoversLine(int row)
    {
        var (s, e) = Ordered;
        return row >= s.Row && row <= e.Row;
    }
}

public sealed class Edit
{
    public TextPosition From     { get; }
    public TextPosition To       { get; }   // only for delete
    public string       Text     { get; }   // inserted text (or deleted for inverse)
    public bool         IsInsert { get; }
    public TextPosition After    { get; }   // caret after applying

    private Edit(TextPosition from, TextPosition to, string text, bool insert, TextPosition after)
    { From = from; To = to; Text = text; IsInsert = insert; After = after; }

    public static Edit Insert(TextPosition at, string text, TextPosition after)
        => new(at, at, text, true, after);

    public static Edit Delete(TextPosition from, TextPosition to, string removed, TextPosition after)
        => new(from, to, removed, false, after);

    public Edit Inverse() => IsInsert
        ? Delete(From, After, Text, From)
        : Insert(From, Text, To);
}

public sealed class UndoStack
{
    private readonly Stack<Edit> _undo = new();
    private readonly Stack<Edit> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Push(Edit e)      { _undo.Push(e); _redo.Clear(); }
    public Edit PopUndo()         => _undo.Pop();
    public Edit PopRedo()         => _redo.Pop();
    public void PushRedo(Edit e)  => _redo.Push(e);
    public void Clear()           { _undo.Clear(); _redo.Clear(); }
}