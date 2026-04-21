using Microsoft.Maui.Controls;
using SabakaLang.Studio.Editor.Core;
using SabakaLang.Studio.Editor.Intellisense;

namespace SabakaLang.Studio.Editor.Input;

public sealed class KeyboardHandler
{
    private readonly EditorDocument  _doc;
    private readonly EditorSelection _sel;
    private readonly CompletionPopup _popup;

    public event Action<CaretPosition>? CaretMoved;
    public event Action? CompletionRequested;
    public event Action? SaveRequested;
    public event Action? FindRequested;
    public event Action? FindAllRequested;
    public event Action? FormatRequested;
    public event Action? GotoDefinitionRequested;

    private static readonly Dictionary<char, char> _autoPairs = new()
    {
        ['{'] = '}', ['('] = ')', ['['] = ']',
        ['"'] = '"', ['\''] = '\''
    };

    private CaretPosition _caret;

    public CaretPosition Caret
    {
        get => _caret;
        set { _caret = value; CaretMoved?.Invoke(_caret); }
    }

    public KeyboardHandler(EditorDocument doc, EditorSelection sel, CompletionPopup popup)
    {
        _doc = doc; _sel = sel; _popup = popup;
    }
    
    public bool HandleKey(string key, bool ctrl, bool shift, bool alt)
    {
        
        if (_popup.IsOpen)
        {
            switch (key)
            {
                case "ArrowDown":  _popup.MoveSelection(+1); return true;
                case "ArrowUp":    _popup.MoveSelection(-1); return true;
                case "Enter":
                case "Tab":        _popup.CommitSelected(); return true;
                case "Escape":     _popup.Hide(); return true;
            }
        }
        
        if (ctrl && !shift && !alt)
        {
            switch (key.ToUpperInvariant())
            {
                case "Z":          HandleUndo();             return true;
                case "Y":          HandleRedo();             return true;
                case "C":          HandleCopy();             return true;
                case "X":          HandleCut();              return true;
                case "V":          HandlePasteAsync();       return true;
                case "A":          HandleSelectAll();        return true;
                case "S":          SaveRequested?.Invoke();  return true;
                case "F":          FindRequested?.Invoke();  return true;
                case "D":          HandleDuplicateLine();    return true;
                case "G":          GotoDefinitionRequested?.Invoke(); return true;
                case "SPACE":      CompletionRequested?.Invoke(); return true;
                case "/":          HandleToggleComment();    return true;
                case "K":          HandleDeleteLine();       return true;
            }
        }

        if (ctrl && shift && !alt)
        {
            switch (key.ToUpperInvariant())
            {
                case "P": FindAllRequested?.Invoke(); return true; 
                case "F": FormatRequested?.Invoke();  return true;
                case "K": HandleDeleteLine();         return true;
            }
        }

        if (alt && !ctrl)
        {
            switch (key)
            {
                case "ArrowUp":   HandleMoveLine(-1); return true;
                case "ArrowDown": HandleMoveLine(+1); return true;
            }
        }

        
        switch (key)
        {
            case "ArrowLeft":
                if (ctrl) MoveToPrevWord(shift);
                else       MoveHorizontal(-1, shift);
                return true;
            case "ArrowRight":
                if (ctrl) MoveToNextWord(shift);
                else       MoveHorizontal(+1, shift);
                return true;
            case "ArrowUp":
                MoveVertical(-1, shift);
                return true;
            case "ArrowDown":
                MoveVertical(+1, shift);
                return true;
            case "Home":
                MoveHome(ctrl, shift);
                return true;
            case "End":
                MoveEnd(ctrl, shift);
                return true;
            case "PageUp":
                MoveVertical(-20, shift);
                return true;
            case "PageDown":
                MoveVertical(+20, shift);
                return true;

            
            case "Backspace":
                HandleBackspace(ctrl);
                return true;
            case "Delete":
                HandleDelete(ctrl);
                return true;
            case "Enter":
                HandleEnter();
                return true;
            case "Tab":
                HandleTab(shift);
                return true;
        }

        return false;
    }
    
    public void HandleChar(char c)
    {
        
        if (!_sel.IsEmpty)
        {
            var (s, e) = _sel.Ordered;
            Caret = _doc.Delete(s, e);
            _sel.Clear(Caret);
        }

        
        if (_autoPairs.TryGetValue(c, out var close))
        {
            var next = NextChar();
            if (next == c && (c == '"' || c == '\''))
            {
                
                Caret = new CaretPosition(Caret.Line, Caret.Column + 1);
                return;
            }
            Caret = _doc.Insert(Caret, c.ToString() + close);
            Caret = new CaretPosition(Caret.Line, Caret.Column - 1);
            _sel.Clear(Caret);
            return;
        }

        
        if ((c == ')' || c == ']' || c == '}' || c == '"' || c == '\'') && NextChar() == c)
        {
            Caret = new CaretPosition(Caret.Line, Caret.Column + 1);
            _sel.Clear(Caret);
            return;
        }

        Caret = _doc.Insert(Caret, c.ToString());
        _sel.Clear(Caret);

        
        if (c == '.' || c == ':')
            CompletionRequested?.Invoke();
    }
    
    private void HandleUndo()
    {
        var pos = _doc.Undo();
        if (pos is not null) { Caret = pos.Value; _sel.Clear(Caret); }
    }

    private void HandleRedo()
    {
        var pos = _doc.Redo();
        if (pos is not null) { Caret = pos.Value; _sel.Clear(Caret); }
    }

    private void HandleCopy()
    {
        var text = GetSelectedText();
        if (text.Length > 0)
            Clipboard.SetTextAsync(text);
    }

    private void HandleCut()
    {
        HandleCopy();
        DeleteSelection();
    }

    private async void HandlePasteAsync()
    {
        var text = await Clipboard.GetTextAsync();
        if (string.IsNullOrEmpty(text)) return;
        DeleteSelection();
        Caret = _doc.Insert(Caret, text);
        _sel.Clear(Caret);
    }

    private void HandleSelectAll()
    {
        _sel.SelectAll(_doc.LineCount - 1, _doc.GetLine(_doc.LineCount - 1).Length);
        CaretMoved?.Invoke(Caret);
    }

    private void HandleEnter()
    {
        DeleteSelection();
        var indent = GetCurrentIndent();
        
        var line = _doc.GetLine(Caret.Line);
        if (Caret.Column > 0 && line[Caret.Column - 1] == '{')
            indent += "    ";
        Caret = _doc.Insert(Caret, "\n" + indent);
        _sel.Clear(Caret);
    }

    private void HandleBackspace(bool ctrl)
    {
        if (!_sel.IsEmpty) { DeleteSelection(); return; }
        if (ctrl)
        {
            var (start, _) = _doc.WordAt(Caret);
            if (start.Column < Caret.Column || start.Line < Caret.Line)
            {
                Caret = _doc.Delete(start, Caret);
                _sel.Clear(Caret);
                return;
            }
        }
        Caret = _doc.Backspace(Caret);
        _sel.Clear(Caret);
    }

    private void HandleDelete(bool ctrl)
    {
        if (!_sel.IsEmpty) { DeleteSelection(); return; }
        if (ctrl)
        {
            var (_, end) = _doc.WordAt(Caret);
            if (end.Column > Caret.Column || end.Line > Caret.Line)
            {
                Caret = _doc.Delete(Caret, end);
                _sel.Clear(Caret);
                return;
            }
        }
        Caret = _doc.DeleteForward(Caret);
        _sel.Clear(Caret);
    }

    private void HandleTab(bool shift)
    {
        if (!_sel.IsEmpty)
        {
            IndentSelection(shift ? -1 : +1);
            return;
        }
        if (shift)
        {
            var line = _doc.GetLine(Caret.Line);
            if (line.StartsWith("    "))
            {
                _doc.Delete(new CaretPosition(Caret.Line, 0), new CaretPosition(Caret.Line, 4));
                Caret = new CaretPosition(Caret.Line, Math.Max(0, Caret.Column - 4));
            }
            else if (line.StartsWith("\t"))
            {
                _doc.Delete(new CaretPosition(Caret.Line, 0), new CaretPosition(Caret.Line, 1));
                Caret = new CaretPosition(Caret.Line, Math.Max(0, Caret.Column - 1));
            }
        }
        else
        {
            Caret = _doc.Insert(Caret, "    ");
            _sel.Clear(Caret);
        }
    }

    private void HandleDuplicateLine()
    {
        var line = _doc.GetLine(Caret.Line);
        var ins  = new CaretPosition(Caret.Line, line.Length);
        _doc.Insert(ins, "\n" + line);
        Caret = new CaretPosition(Caret.Line + 1, Caret.Column);
        _sel.Clear(Caret);
    }

    private void HandleDeleteLine()
    {
        var from = new CaretPosition(Caret.Line, 0);
        CaretPosition to;
        if (Caret.Line < _doc.LineCount - 1)
            to = new CaretPosition(Caret.Line + 1, 0);
        else
            to = new CaretPosition(Caret.Line, _doc.GetLine(Caret.Line).Length);
        Caret = _doc.Delete(from, to);
        _sel.Clear(Caret);
    }

    private void HandleMoveLine(int delta)
    {
        var li  = Caret.Line;
        var target = li + delta;
        if (target < 0 || target >= _doc.LineCount) return;

        var lineA = _doc.GetLine(li);
        var lineB = _doc.GetLine(target);

        _doc.Delete(new CaretPosition(li,     0), new CaretPosition(li,     lineA.Length));
        _doc.Insert(new CaretPosition(li,     0), lineB);
        _doc.Delete(new CaretPosition(target, 0), new CaretPosition(target, lineB.Length));
        _doc.Insert(new CaretPosition(target, 0), lineA);

        Caret = new CaretPosition(target, Caret.Column);
        _sel.Clear(Caret);
    }

    private void HandleToggleComment()
    {
        var (start, end) = _sel.IsEmpty
            ? (Caret.Line, Caret.Line)
            : (_sel.Ordered.Start.Line, _sel.Ordered.End.Line);

        for (var li = start; li <= end; li++)
        {
            var line = _doc.GetLine(li);
            if (line.TrimStart().StartsWith("//"))
            {
                var idx = line.IndexOf("//", StringComparison.Ordinal);
                _doc.Delete(new CaretPosition(li, idx), new CaretPosition(li, idx + 2));
            }
            else
            {
                _doc.Insert(new CaretPosition(li, 0), "//");
            }
        }
    }

    private void IndentSelection(int direction)
    {
        var (s, e) = _sel.Ordered;
        for (var li = s.Line; li <= e.Line; li++)
        {
            if (direction > 0)
                _doc.Insert(new CaretPosition(li, 0), "    ");
            else
            {
                var line = _doc.GetLine(li);
                if (line.StartsWith("    "))
                    _doc.Delete(new CaretPosition(li, 0), new CaretPosition(li, 4));
            }
        }
    }

    private void MoveHorizontal(int dir, bool shift)
    {
        var newCaret = dir < 0
            ? (Caret.Column > 0
                ? new CaretPosition(Caret.Line, Caret.Column - 1)
                : Caret.Line > 0
                    ? new CaretPosition(Caret.Line - 1, _doc.GetLine(Caret.Line - 1).Length)
                    : Caret)
            : (Caret.Column < _doc.GetLine(Caret.Line).Length
                ? new CaretPosition(Caret.Line, Caret.Column + 1)
                : Caret.Line < _doc.LineCount - 1
                    ? new CaretPosition(Caret.Line + 1, 0)
                    : Caret);

        UpdateCaret(newCaret, shift);
    }

    private void MoveVertical(int delta, bool shift)
    {
        var newLine = Math.Clamp(Caret.Line + delta, 0, _doc.LineCount - 1);
        var newCol  = Math.Clamp(Caret.Column, 0, _doc.GetLine(newLine).Length);
        UpdateCaret(new CaretPosition(newLine, newCol), shift);
    }

    private void MoveToPrevWord(bool shift)
    {
        var (start, _) = _doc.WordAt(Caret);
        var newPos = start.Column < Caret.Column ? start
            : Caret.Column > 0 ? new CaretPosition(Caret.Line, 0)
            : Caret.Line > 0
                ? new CaretPosition(Caret.Line - 1, _doc.GetLine(Caret.Line - 1).Length)
                : Caret;
        UpdateCaret(newPos, shift);
    }

    private void MoveToNextWord(bool shift)
    {
        var (_, end) = _doc.WordAt(Caret);
        var line = _doc.GetLine(Caret.Line);
        var newPos = end.Column > Caret.Column ? end
            : Caret.Column < line.Length ? new CaretPosition(Caret.Line, line.Length)
            : Caret.Line < _doc.LineCount - 1
                ? new CaretPosition(Caret.Line + 1, 0)
                : Caret;
        UpdateCaret(newPos, shift);
    }

    private void MoveHome(bool ctrl, bool shift)
    {
        CaretPosition newPos;
        if (ctrl)
            newPos = CaretPosition.Zero;
        else
        {
            var line   = _doc.GetLine(Caret.Line);
            var indent = line.TakeWhile(c => c == ' ' || c == '\t').Count();
            newPos = Caret.Column == indent
                ? new CaretPosition(Caret.Line, 0)
                : new CaretPosition(Caret.Line, indent);
        }
        UpdateCaret(newPos, shift);
    }

    private void MoveEnd(bool ctrl, bool shift)
    {
        var newPos = ctrl
            ? new CaretPosition(_doc.LineCount - 1, _doc.GetLine(_doc.LineCount - 1).Length)
            : new CaretPosition(Caret.Line, _doc.GetLine(Caret.Line).Length);
        UpdateCaret(newPos, shift);
    }

    private void UpdateCaret(CaretPosition pos, bool shift)
    {
        if (shift) _sel.Extend(pos);
        else       _sel.Clear(pos);
        Caret = pos;
    }

    private string GetCurrentIndent()
    {
        var line = _doc.GetLine(Caret.Line);
        var sb   = new System.Text.StringBuilder();
        foreach (var c in line) { if (c == ' ' || c == '\t') sb.Append(c); else break; }
        return sb.ToString();
    }

    private char NextChar()
    {
        var line = _doc.GetLine(Caret.Line);
        return Caret.Column < line.Length ? line[Caret.Column] : '\0';
    }

    private string GetSelectedText()
    {
        if (_sel.IsEmpty) return "";
        var (s, e) = _sel.Ordered;
        var sb = new System.Text.StringBuilder();
        for (var li = s.Line; li <= e.Line; li++)
        {
            if (li > s.Line) sb.Append('\n');
            var line  = _doc.GetLine(li);
            var start = li == s.Line ? s.Column : 0;
            var end   = li == e.Line ? e.Column : line.Length;
            sb.Append(line[start..end]);
        }
        return sb.ToString();
    }

    private void DeleteSelection()
    {
        if (_sel.IsEmpty) return;
        var (s, e) = _sel.Ordered;
        Caret = _doc.Delete(s, e);
        _sel.Clear(Caret);
    }
}