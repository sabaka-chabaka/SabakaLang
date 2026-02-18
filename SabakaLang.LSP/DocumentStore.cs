using OmniSharp.Extensions.LanguageServer.Protocol;

namespace SabakaLang.LSP;

public class DocumentStore
{
    private readonly Dictionary<DocumentUri, string> _sources = new();

    public void UpdateDocument(DocumentUri uri, string text)
    {
        _sources[uri] = text;
    }

    public void RemoveDocument(DocumentUri uri)
    {
        _sources.Remove(uri);
    }

    public string? GetDocument(DocumentUri uri)
    {
        return _sources.TryGetValue(uri, out var source) ? source : null;
    }
}
