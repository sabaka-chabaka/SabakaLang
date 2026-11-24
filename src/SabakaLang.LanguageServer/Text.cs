namespace SabakaLang.LanguageServer;

public sealed class LineIndex
{
    private readonly List<int> _starts = [0];
    private readonly string _text;

    public LineIndex(string text)
    {
        _text = text;
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\n') _starts.Add(i + 1);
    }

    public int LineCount => _starts.Count;
    public int LineStart(int line) => _starts[Math.Clamp(line, 0, _starts.Count - 1)];

    public int LineEnd(int line)
    {
        line = Math.Clamp(line, 0, _starts.Count - 1);
        var end = line + 1 < _starts.Count ? _starts[line + 1] - 1 : _text.Length;
        if (end > _starts[line] && end <= _text.Length && end > 0 && _text[end - 1] == '\r') end--;
        return end;
    }

    public (int Line, int Character) ToPosition(int offset)
    {
        offset = Math.Clamp(offset, 0, _text.Length);
        var idx = _starts.BinarySearch(offset);
        if (idx < 0) idx = ~idx - 1;
        return (idx, offset - _starts[idx]);
    }

    public int ToOffset(int line, int character)
    {
        if (line < 0) return 0;
        if (line >= _starts.Count) return _text.Length;
        return Math.Min(_starts[line] + Math.Max(character, 0), LineEnd(line));
    }
}

public static class Paths
{
    public static string ToPath(string uri)
    {
        try { return new Uri(uri).LocalPath; } catch { return uri; }
    }

    public static string ToUri(string path)
    {
        try { return new Uri(Path.GetFullPath(path)).AbsoluteUri; } catch { return path; }
    }
}

public sealed class Doc
{
    public required string Uri { get; init; }
    public string Text { get; set; } = "";
    public Analysis? Analysis { get; set; }
}

public sealed class DocumentStore
{
    private readonly Dictionary<string, Doc> _docs = new();
    public IEnumerable<Doc> All => _docs.Values;
    public Doc? Get(string uri) => _docs.GetValueOrDefault(uri);

    public void Set(string uri, string text)
    {
        if (!_docs.TryGetValue(uri, out var d)) _docs[uri] = d = new Doc { Uri = uri };
        d.Text = text;
        foreach (var o in _docs.Values) o.Analysis = null;
    }

    public void Remove(string uri)
    {
        _docs.Remove(uri);
        foreach (var o in _docs.Values) o.Analysis = null;
    }

    public string? TryGetTextByPath(string path)
    {
        foreach (var d in _docs.Values)
            if (string.Equals(Paths.ToPath(d.Uri), path, StringComparison.OrdinalIgnoreCase)) return d.Text;
        return null;
    }
}
