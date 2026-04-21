using SabakaLang.Compiler;
using SabakaLang.LanguageServer;

namespace SabakaLang.Studio.Services;

public sealed class LspBridge : IDisposable
{
    private readonly DocumentStore _store;
    private readonly IDispatcherTimer _debounce;
    private string? _pendingUri;
    private string? _pendingSource;

    public event Action<string, IReadOnlyList<SabakaLang.LanguageServer.Diagnostic>>? DiagnosticsPublished;

    public LspBridge(DocumentStore store)
    {
        _store    = store;
        _debounce = Application.Current!.Dispatcher.CreateTimer();
        _debounce.Interval    = TimeSpan.FromMilliseconds(250);
        _debounce.IsRepeating = false;
        _debounce.Tick        += (_, _) => Flush();
    }

    public void NotifyChange(string uri, string source)
    {
        _pendingUri    = uri;
        _pendingSource = source;
        _debounce.Stop();
        _debounce.Start();
    }

    private void Flush()
    {
        if (_pendingUri is null || _pendingSource is null) return;
        var analysis = _store.Analyze(_pendingUri, _pendingSource);
        DiagnosticsPublished?.Invoke(_pendingUri, analysis.Diagnostics);
        _pendingUri = null; _pendingSource = null;
    }

    public DocumentAnalysis? GetAnalysis(string uri) => _store.Get(uri);

    public void Dispose() => _debounce.Stop();
}