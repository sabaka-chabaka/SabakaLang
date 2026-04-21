using System.Text;

namespace SabakaLang.Studio.Editor.Core;

public sealed class TextBuffer
{
    private readonly List<string> _lines = [""];
    private readonly object _gate = new();

    public int LineCount { get { lock (_gate) return _lines.Count; } }

    public string Line(int index)
    {
        lock (_gate)
            return index >= 0 && index < _lines.Count ? _lines[index] : "";
    }

    public string FullText()
    {
        lock (_gate)
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

    public TextPosition Clamp(TextPosition p)
    {
        lock (_gate)
        {
            var row = Math.Clamp(p.Row, 0, _lines.Count - 1);
            var col = Math.Clamp(p.Col, 0, _lines[row].Length);
            return new(row, col);
        }
    }
    
    public void SetText(string text)
    {
        lock (_gate)
        {
            _lines.Clear();
            foreach (var l in text.ReplaceLineEndings("\n").Split('\n'))
                _lines.Add(l);
            if (_lines.Count == 0) _lines.Add("");
        }
    }

    public TextPosition Insert(TextPosition at, string text)
    {
        at = Clamp(at);
        if (text.Length == 0) return at;

        lock (_gate)
        {
            var parts = text.ReplaceLineEndings("\n").Split('\n');
            var line  = _lines[at.Row];
            var before = line[..at.Col];
            var after  = line[at.Col..];

            if (parts.Length == 1)
            {
                _lines[at.Row] = before + parts[0] + after;
                return new(at.Row, at.Col + parts[0].Length);
            }

            _lines[at.Row] = before + parts[0];
            for (var i = 1; i < parts.Length - 1; i++)
                _lines.Insert(at.Row + i, parts[i]);
            var lastRow = at.Row + parts.Length - 1;
            _lines.Insert(lastRow, parts[^1] + after);
            return new(lastRow, parts[^1].Length);
        }
    }

    public TextPosition Delete(TextPosition from, TextPosition to)
    {
        from = Clamp(from);
        to   = Clamp(to);
        if (from == to) return from;
        if (to < from) (from, to) = (to, from);

        lock (_gate)
        {
            if (from.Row == to.Row)
            {
                _lines[from.Row] = _lines[from.Row][..from.Col] + _lines[from.Row][to.Col..];
            }
            else
            {
                var tail = _lines[to.Row][to.Col..];
                _lines[from.Row] = _lines[from.Row][..from.Col] + tail;
                _lines.RemoveRange(from.Row + 1, to.Row - from.Row);
            }
        }
        return from;
    }

    public string Extract(TextPosition from, TextPosition to)
    {
        from = Clamp(from);
        to   = Clamp(to);
        if (from == to) return "";
        if (to < from) (from, to) = (to, from);

        lock (_gate)
        {
            if (from.Row == to.Row)
                return _lines[from.Row][from.Col..to.Col];

            var sb = new StringBuilder();
            sb.Append(_lines[from.Row][from.Col..]);
            for (var i = from.Row + 1; i < to.Row; i++)
            {
                sb.Append('\n');
                sb.Append(_lines[i]);
            }
            sb.Append('\n');
            sb.Append(_lines[to.Row][..to.Col]);
            return sb.ToString();
        }
    }

    public int LineLength(int row) { lock (_gate) return row >= 0 && row < _lines.Count ? _lines[row].Length : 0; }

    public string LeadingWhitespace(int row)
    {
        lock (_gate)
        {
            if (row < 0 || row >= _lines.Count) return "";
            var l = _lines[row];
            var i = 0;
            while (i < l.Length && (l[i] == ' ' || l[i] == '\t')) i++;
            return l[..i];
        }
    }

    public (TextPosition Start, TextPosition End) WordAt(TextPosition p)
    {
        p = Clamp(p);
        lock (_gate)
        {
            var l   = _lines[p.Row];
            if (l.Length == 0) return (p, p);
            var col = Math.Min(p.Col, l.Length - 1);
            static bool W(char c) => char.IsLetterOrDigit(c) || c == '_';
            var s = col; while (s > 0 && W(l[s - 1])) s--;
            var e = col; while (e < l.Length && W(l[e])) e++;
            return (new(p.Row, s), new(p.Row, e));
        }
    }

    public List<TextPosition> FindAll(string needle, bool caseSensitive = false)
    {
        var cmp  = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var hits = new List<TextPosition>();
        lock (_gate)
        {
            for (var r = 0; r < _lines.Count; r++)
            {
                var ln = _lines[r];
                var c  = 0;
                while (true)
                {
                    c = ln.IndexOf(needle, c, cmp);
                    if (c < 0) break;
                    hits.Add(new(r, c));
                    c += needle.Length;
                }
            }
        }
        return hits;
    }
}