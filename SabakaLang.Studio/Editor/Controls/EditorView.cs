using SabakaLang.LanguageServer;
using SabakaLang.Studio.Editor.Core;
using SabakaLang.Studio.Editor.Highlighting;
using SabakaLang.Studio.Editor.Input;
using SabakaLang.Studio.Editor.Intellisense;
using SabakaLang.Studio.Editor.Rendering;
using StudioTheme = SabakaLang.Studio.Editor.Highlighting.StudioTheme;

namespace SabakaLang.Studio.Editor.Controls;

public sealed class EditorView : GraphicsView, IDrawable
{
    public readonly EditorDocument    Document;
    private readonly EditorSelection   _selection  = new();
    private readonly SyntaxHighlighter _highlighter;
    private readonly BracketMatcher    _bracketMatcher = new();
    private readonly EditorRenderer    _renderer;
    private readonly CompletionPopup   _completionPopup;
    private readonly CompletionProvider _completions;
    private readonly KeyboardHandler   _keyboard;
    private readonly MouseHandler      _mouse;

    private string  _fileUri   = "untitled://untitled.sabaka";
    private float   _scrollY;
    private float   _scrollX;
    private bool    _caretVisible = true;
    private List<DiagnosticSpan> _diagnostics = [];
    private List<CaretPosition>  _searchHits  = [];
    private int     _searchCurrent = -1;

    private readonly IDispatcherTimer _blinkTimer;

    private readonly IDispatcherTimer _analysisTimer;

    public event Action<string>?        FileDirty;
    public event Action<CaretPosition>? StatusUpdated;
    public event Action<List<DiagnosticSpan>>? DiagnosticsUpdated;

    public EditorView(DocumentStore store)
    {
        Drawable = this;
        BackgroundColor = StudioTheme.Background;

        Document      = new EditorDocument();
        _highlighter  = new SyntaxHighlighter(store);
        _renderer     = new EditorRenderer(_highlighter, _bracketMatcher);
        _completionPopup = new CompletionPopup();
        _completions  = new CompletionProvider(store);
        _keyboard     = new KeyboardHandler(Document, _selection, _completionPopup);
        _mouse        = new MouseHandler(Document, _selection);

        _keyboard.CaretMoved             += OnCaretMoved;
        _keyboard.CompletionRequested    += TriggerCompletion;
        _keyboard.SaveRequested          += () => RequestSave();
        _keyboard.FindRequested          += () => FindRequested?.Invoke();
        _keyboard.FindAllRequested       += () => FindAllRequested?.Invoke();
        _keyboard.GotoDefinitionRequested+= GotoDefinition;

        _mouse.CaretMoved    += pos => { _keyboard.Caret = pos; Invalidate(); };
        _mouse.ScrollRequested+= delta => { _scrollY = Math.Max(0, _scrollY + delta * 3); Invalidate(); };

        _completionPopup.ItemCommitted += CommitCompletion;

        Document.TextChanged += OnTextChanged;

        _blinkTimer = Application.Current!.Dispatcher.CreateTimer();
        _blinkTimer.Interval = TimeSpan.FromMilliseconds(530);
        _blinkTimer.Tick += (_, _) => { _caretVisible = !_caretVisible; Invalidate(); };
        _blinkTimer.Start();

        _analysisTimer = Application.Current!.Dispatcher.CreateTimer();
        _analysisTimer.Interval = TimeSpan.FromMilliseconds(300);
        _analysisTimer.IsRepeating = false;
        _analysisTimer.Tick += (_, _) => RunAnalysis();

        var drag = new DragGestureRecognizer();
        GestureRecognizers.Add(drag);
    }

    public string FileUri
    {
        get => _fileUri;
        set { _fileUri = value; RunAnalysis(); }
    }

    public string Text
    {
        get => Document.GetText();
        set { Document.SetText(value); RunAnalysis(); }
    }

    public bool IsDirty { get; private set; }

    public void MarkClean() { IsDirty = false; }

    public event Action? FindRequested;
    public event Action? FindAllRequested;
    public event Action? SaveRequested;

    private void RequestSave() => SaveRequested?.Invoke();

    public void SetSearchResults(List<CaretPosition> hits, int current)
    {
        _searchHits    = hits;
        _searchCurrent = current;
        Invalidate();
    }

    public void ScrollToLine(int line)
    {
        _scrollY = (float)Math.Max(0, line * StudioTheme.LineHeight - Height / 2);
        Invalidate();
    }

    public void Draw(ICanvas canvas, RectF dirty)
    {
        _renderer.Draw(
            canvas, dirty, Document, _selection, _keyboard.Caret,
            _scrollY, _scrollX, _diagnostics, _searchHits,
            _searchCurrent, _caretVisible);
    }

    public void OnKeyDown(string key, bool ctrl, bool shift, bool alt)
    {
        if (!_keyboard.HandleKey(key, ctrl, shift, alt))
            return;
        ResetBlink();
        Invalidate();
    }

    public void OnChar(char c)
    {
        _keyboard.HandleChar(c);
        ResetBlink();
        Invalidate();
    }

    public void OnPointerDown(float x, float y, bool shift, bool isDouble)
    {
        _keyboard.Caret = _mouse.OnPointerDown(x, y, shift, isDouble);
        _completionPopup.Hide();
        ResetBlink();
        Invalidate();
    }

    public void OnPointerMove(float x, float y)
    {
        var p = _mouse.OnPointerMove(x, y);
        if (p is not null) { _keyboard.Caret = p.Value; Invalidate(); }
    }

    public void OnPointerUp() => _mouse.OnPointerUp();

    public void OnScroll(float deltaY)
    {
        _scrollY = Math.Max(0, _scrollY + deltaY * StudioTheme.LineHeight * 3);
        Invalidate();
    }

    private void OnCaretMoved(CaretPosition pos)
    {
        _mouse.SetScroll(_scrollY, _scrollX);
        EnsureCaretVisible(pos);
        StatusUpdated?.Invoke(pos);
        Invalidate();
    }

    private void OnTextChanged()
    {
        IsDirty = true;
        FileDirty?.Invoke(_fileUri);
        _analysisTimer.Stop();
        _analysisTimer.Start();
        Invalidate();
    }

    private void RunAnalysis()
    {
        var source = Document.GetText();
        _highlighter.Invalidate(_fileUri, source);

        var store = GetStore();
        if (store is null) return;

        var analysis = store.Get(_fileUri);
        if (analysis is null) return;

        _diagnostics = analysis.Diagnostics.Select(d => new DiagnosticSpan(
            d.Start.Line - 1,
            d.Start.Column - 1,
            Math.Max(d.End.Column - 1, d.Start.Column),
            d.Severity == DiagnosticSeverity.Error,
            d.Message
        )).ToList();

        DiagnosticsUpdated?.Invoke(_diagnostics);
        Invalidate();
    }

    private void TriggerCompletion()
    {
        var caret = _keyboard.Caret;
        var items = _completions.GetCompletions(_fileUri, caret.Line, caret.Column);
        if (items.Count == 0) { _completionPopup.Hide(); return; }

        float x = StudioTheme.GutterWidth + caret.Column * StudioTheme.CharWidth;
        float y = (caret.Line + 1) * StudioTheme.LineHeight - _scrollY;
        _completionPopup.Show(items, x, y);
    }

    private void CommitCompletion(CompletionEntry entry)
    {
        var caret = _keyboard.Caret;
        var line  = Document.GetLine(caret.Line);
        var start = caret.Column;
        while (start > 0 && (char.IsLetterOrDigit(line[start - 1]) || line[start - 1] == '_'))
            start--;

        Document.Delete(new CaretPosition(caret.Line, start), caret);

        var insertText = entry.IsSnippet
            ? entry.Insert.Replace("$0", "")
            : entry.Insert;

        _keyboard.Caret = Document.Insert(new CaretPosition(caret.Line, start), insertText);
        _selection.Clear(_keyboard.Caret);
        _completionPopup.Hide();
        Invalidate();
    }

    private void GotoDefinition()
    {
        var caret    = _keyboard.Caret;
        var (s, _)   = Document.WordAt(caret);
        var line     = Document.GetLine(caret.Line);
        var wordStart = s.Column;
        var wordEnd   = caret.Column;
        while (wordEnd < line.Length && (char.IsLetterOrDigit(line[wordEnd]) || line[wordEnd] == '_'))
            wordEnd++;
        var word = line[wordStart..wordEnd];
        if (word.Length == 0) return;

        var hits = Document.FindAll(word);
        if (hits.Count > 0)
        {
            _keyboard.Caret = hits[0];
            _selection.Clear(_keyboard.Caret);
            ScrollToLine(hits[0].Line);
        }
    }

    private void EnsureCaretVisible(CaretPosition pos)
    {
        var y = pos.Line * StudioTheme.LineHeight;
        if (y < _scrollY)
            _scrollY = y;
        else if (y + StudioTheme.LineHeight > _scrollY + Height)
            _scrollY = y + StudioTheme.LineHeight - (float)Height;
        _scrollY = Math.Max(0, _scrollY);
    }

    private void ResetBlink() { _caretVisible = true; _blinkTimer.Stop(); _blinkTimer.Start(); }

    private DocumentStore? GetStore() => _completions.GetType()
        .GetField("_store", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
        ?.GetValue(_completions) as DocumentStore;
}