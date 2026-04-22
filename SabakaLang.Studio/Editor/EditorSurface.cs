using Microsoft.Maui.Layouts;
using SabakaLang.LanguageServer;
using SabakaLang.Studio.Editor.Core;
using SabakaLang.Studio.Editor.Gutter;
using SabakaLang.Studio.Editor.Highlighting;
using SabakaLang.Studio.Editor.Input;
using SabakaLang.Studio.Editor.Intellisense;
using SabakaLang.Studio.Editor.Render;

namespace SabakaLang.Studio.Editor;

public sealed class EditorSurface : AbsoluteLayout, IDisposable
{
    private readonly EditorModel         _model      = new();
    private readonly SyntaxTokenizer     _tokenizer  = new();
    private readonly CompletionState     _completion = new();
    private readonly SignatureState      _signature  = new();
    private readonly HoverState          _hover      = new();
    private readonly InputHandler        _input;
    private readonly IntelliSenseEngine  _lsEngine;
    private readonly DocumentStore       _store;

    private readonly EditorCanvas      _canvas;
    private readonly Entry             _keyCapture;
    private readonly CompletionPopupView _completionView;
    private readonly SignatureView     _signatureView;
    private readonly HoverView         _hoverView;

    private string  _uri         = "untitled://untitled.sabaka";
    private float   _scrollRow   = 0f;   // fractional row offset
    private bool    _caretBlink  = true;
    private bool    _isDirty     = false;

    private List<Diagnostic>    _diagnostics = [];
    private List<TextPosition>  _searchHits  = [];
    private int                 _searchCurrent = -1;

    private readonly IDispatcherTimer _blinkTimer;
    private readonly IDispatcherTimer _analysisTimer;
    private readonly IDispatcherTimer _hoverTimer;

    private CancellationTokenSource? _analysisCts;

    public event Action<bool>?  DirtyChanged;
    public event Action<int,int>? CaretMoved;
    public event Action<List<Diagnostic>>? DiagnosticsChanged;

    private bool _disposed;

    private int _invalidatePending;

    public EditorSurface(DocumentStore store)
    {
        _store    = store;
        _lsEngine = new IntelliSenseEngine(store);
        _input    = new InputHandler(_model, _completion);

        BackgroundColor = Theme.Bg;

        _canvas = new EditorCanvas(_model, _tokenizer, _completion, _signature,
                                   _hover, _diagnostics, _searchHits, ref _searchCurrent,
                                   ref _scrollRow, ref _caretBlink);

        AbsoluteLayout.SetLayoutBounds(_canvas, new Rect(0, 0, 1, 1));
        AbsoluteLayout.SetLayoutFlags(_canvas, AbsoluteLayoutFlags.SizeProportional);
        Children.Add(_canvas);

        _keyCapture = new Entry
        {
            Text            = "",
            IsVisible       = true,
            BackgroundColor = Colors.Transparent,
            TextColor       = Colors.Transparent,
            FontSize        = 1,
            WidthRequest    = 1,
            HeightRequest   = 1,
            Opacity         = 0.01,
            ZIndex          = 500
        };
        
        AbsoluteLayout.SetLayoutBounds(_keyCapture, new Rect(0, 0, 1, 1));
        AbsoluteLayout.SetLayoutFlags(_keyCapture, AbsoluteLayoutFlags.SizeProportional);
        Children.Add(_keyCapture);

        _completionView = new CompletionPopupView();
        _completionView.Bind(_completion);
        Children.Add(_completionView);

        _signatureView = new SignatureView();
        _signatureView.Bind(_signature);
        Children.Add(_signatureView);

        _hoverView = new HoverView();
        _hoverView.Bind(_hover);
        Children.Add(_hoverView);

        _input.ScrollToCaret    += EnsureCaretVisible;
        _input.TriggerCompletion+= DoCompletion;
        _input.TriggerSignature += DoSignature;
        _input.FindRequested    += OnFindRequested;
        _input.SaveRequested    += OnSaveRequested;
        _input.PaletteRequested += OnPaletteRequested;
        _input.ScrollDelta      += OnScrollDelta;

        _model.Changed += OnModelChanged;

        _completion.Changed += Invalidate;
        _signature.Changed  += Invalidate;
        _hover.Changed      += Invalidate;

        WirePointerGestures();

        WireKeyboard();

        var disp = Application.Current!.Dispatcher;

        _blinkTimer = disp.CreateTimer();
        _blinkTimer.Interval    = TimeSpan.FromMilliseconds(530);
        _blinkTimer.Tick        += OnBlinkTick;
        _blinkTimer.Start();

        _analysisTimer = disp.CreateTimer();
        _analysisTimer.Interval    = TimeSpan.FromMilliseconds(300);
        _analysisTimer.IsRepeating = false;
        _analysisTimer.Tick        += OnAnalysisTick;

        _hoverTimer = disp.CreateTimer();
        _hoverTimer.Interval    = TimeSpan.FromMilliseconds(600);
        _hoverTimer.IsRepeating = false;
        _hoverTimer.Tick        += (_, _) => { };

        var focusTap = new TapGestureRecognizer();
        focusTap.Tapped += OnFocusTapped;
        GestureRecognizers.Add(focusTap);

        _tokenizer.TokensReady += Invalidate;
    }

    private void OnFindRequested()    => FindRequested?.Invoke();
    private void OnSaveRequested()    => SaveRequested?.Invoke();
    private void OnPaletteRequested() => PaletteRequested?.Invoke();
    private void OnScrollDelta(float lines) => Scroll(lines * 3);

    private void OnModelChanged()
    {
        PositionOverlays();
        Invalidate();
        if (!_isDirty) { _isDirty = true; DirtyChanged?.Invoke(true); }
        CaretMoved?.Invoke(_model.Caret.Row, _model.Caret.Col);
        _analysisTimer.Stop();
        _analysisTimer.Start();
    }

    private void OnBlinkTick(object? sender, EventArgs e) { _caretBlink = !_caretBlink; _canvas.SyncState(_caretBlink); Invalidate(); }
    private void OnAnalysisTick(object? sender, EventArgs e) => RunAnalysis();
    private void OnFocusTapped(object? sender, TappedEventArgs e) { _keyCapture.Focus(); }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _analysisCts?.Cancel();
        _analysisCts?.Dispose();

        _blinkTimer.Stop();
        _analysisTimer.Stop();
        _hoverTimer.Stop();

        _input.ScrollToCaret    -= EnsureCaretVisible;
        _input.TriggerCompletion-= DoCompletion;
        _input.TriggerSignature -= DoSignature;
        _input.FindRequested    -= OnFindRequested;
        _input.SaveRequested    -= OnSaveRequested;
        _input.PaletteRequested -= OnPaletteRequested;
        _input.ScrollDelta      -= OnScrollDelta;

        _model.Changed -= OnModelChanged;

        _completion.Changed -= Invalidate;
        _signature.Changed  -= Invalidate;
        _hover.Changed      -= Invalidate;
        
        _tokenizer.TokensReady -= Invalidate;

        GestureRecognizers.Clear();

        _completionView.Dispose();
        _signatureView.Dispose();
        _hoverView.Dispose();
        
        _canvas.GestureRecognizers.Clear();
    }

    public event Action? FindRequested;
    public event Action? SaveRequested;
    public event Action? PaletteRequested;

    public string Uri
    {
        get => _uri;
        set { _uri = value; RunAnalysis(); }
    }

    public string Text
    {
        get => _model.Text;
        set { _model.Text = value; _isDirty = false; RunAnalysis(); Invalidate(); }
    }

    public bool IsDirty => _isDirty;
    public void MarkClean() { _isDirty = false; DirtyChanged?.Invoke(false); }

    public void FocusEditor() => _keyCapture.Focus();

    public void SetSearchResults(List<TextPosition> hits, int current)
    {
        _searchHits    = hits;
        _searchCurrent = current;
        _canvas.UpdateSearch(hits, current);
        Invalidate();
    }

    public void ScrollToRow(int row)
    {
        _scrollRow = Math.Max(0, row - 5f);
        _input.SetScroll(_scrollRow);
        Invalidate();
    }

    private void WireKeyboard()
    {
    }

    public bool HandleKeyDown(string key, bool ctrl, bool shift, bool alt)
    {
        var handled = _input.OnKeyDown(key, ctrl, shift, alt);
        if (handled)
        {
            _caretBlink = true;
            _canvas.SyncState(true);
            _blinkTimer.Stop();
            _blinkTimer.Start();
        }
        return handled;
    }

    private float _lastTapTime;
    private float _lastTapX, _lastTapY;

    private void WirePointerGestures()
    {
        var ptr = new PointerGestureRecognizer();

        ptr.PointerPressed += (_, e) =>
        {
            var pt = e.GetPosition(_canvas);
            if (pt is null) return;
            var (px, py) = ((float)pt.Value.X, (float)pt.Value.Y);

            var now = (float)(DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds;
            var isDouble = (now - _lastTapTime) < 400
                        && Math.Abs(px - _lastTapX) < 10
                        && Math.Abs(py - _lastTapY) < 10;
            _lastTapTime = now; _lastTapX = px; _lastTapY = py;

            _hover.Hide();
            _completion.Hide();
            _signature.Hide();

            if (isDouble) _input.OnDoubleTap(px, py);
            else           _input.OnPointerPressed(px, py, shift: false);

            _keyCapture.Focus();
            _caretBlink = true; _canvas.SyncState(true); _blinkTimer.Stop(); _blinkTimer.Start();
        };

        ptr.PointerMoved += (_, e) =>
        {
            var pt = e.GetPosition(_canvas);
            if (pt is null) return;
            _input.OnPointerMoved((float)pt.Value.X, (float)pt.Value.Y);
        };

        ptr.PointerReleased += (_, _) => _input.OnPointerReleased();

        _canvas.GestureRecognizers.Add(ptr);

        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (_, e) =>
        {
            if (e.StatusType != GestureStatus.Running) return;
            var lines = -(float)e.TotalY / Theme.LineHeight;
            Scroll(lines);
        };
        _canvas.GestureRecognizers.Add(pan);
    }
    
    private void RunAnalysis()
    {
        var oldCts = _analysisCts;
        _analysisCts = new CancellationTokenSource();
        var ct = _analysisCts.Token;

        oldCts?.Cancel();
        oldCts?.Dispose();

        var src = _model.Text;
        var uri = _uri;
        var cts = _analysisCts;

        _ = Task.Run(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                _tokenizer.Analyze(uri, src, _store);

                ct.ThrowIfCancellationRequested();
                var analysis = _store.Get(uri);
                if (analysis is null) return;

                var diags = analysis.Diagnostics.ToList();
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_disposed || ct.IsCancellationRequested) return;
                    _diagnostics = diags;
                    _canvas.UpdateDiagnostics(diags);
                    DiagnosticsChanged?.Invoke(diags);
                    Invalidate();
                });
            }
            catch (OperationCanceledException) { }
            catch { /* ignored */ }
            finally
            {
                if (ReferenceEquals(_analysisCts, cts))
                {
                }
            }
        }, ct);
    }
    
    private void DoCompletion()
    {
        var items = _lsEngine.GetCompletions(_uri, _model.Caret);
        if (items.Count == 0) { _completion.Hide(); return; }

        var (x, y) = CaretPixelPos();
        _completion.Show(items, x, y + Theme.LineHeight + 2);
        PositionOverlays();
    }

    private void DoSignature()
    {
        var result = _lsEngine.GetSignature(_uri, _model.Caret);
        if (result is null) { _signature.Hide(); return; }
        var (label, parms, active) = result.Value;
        _signature.Show(label, parms, active);
        PositionOverlays();
    }

    private void Scroll(float lineDelta)
    {
        var maxScroll = Math.Max(0, _model.Buffer.LineCount - (int)(Height / Theme.LineHeight) + 2);
        _scrollRow = Math.Clamp(_scrollRow + lineDelta, 0, maxScroll);
        _input.SetScroll(_scrollRow);
        _canvas.SetScroll(_scrollRow);
        Invalidate();
    }

    private void EnsureCaretVisible()
    {
        var row    = _model.Caret.Row;
        var visible = (int)(Height / Theme.LineHeight) - 2;
        if (visible < 1) return;

        if (row < (int)_scrollRow)
            _scrollRow = row;
        else if (row >= (int)_scrollRow + visible)
            _scrollRow = row - visible + 1;

        _scrollRow = Math.Max(0, _scrollRow);
        _input.SetScroll(_scrollRow);
        _canvas.SetScroll(_scrollRow);
        PositionOverlays();
    }

    private (float x, float y) CaretPixelPos()
    {
        var caret = _model.Caret;
        return (
            Theme.GutterW + caret.Col * Theme.CharWidth,
            (caret.Row - _scrollRow) * Theme.LineHeight
        );
    }

    private void PositionOverlays()
    {
        if (!IsVisible) return;
        var (cx, cy) = CaretPixelPos();

        var popY = cy + Theme.LineHeight + 4;
        AbsoluteLayout.SetLayoutBounds(_completionView, new Rect(cx, popY, 360, 220));

        var sigY = Math.Max(0, cy - 32);
        AbsoluteLayout.SetLayoutBounds(_signatureView, new Rect(cx, sigY, AbsoluteLayout.AutoSize, AbsoluteLayout.AutoSize));

        AbsoluteLayout.SetLayoutBounds(_hoverView, new Rect(cx, popY, AbsoluteLayout.AutoSize, AbsoluteLayout.AutoSize));
    }

    private void Invalidate()
    {
        if (System.Threading.Interlocked.CompareExchange(ref _invalidatePending, 1, 0) == 0)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                System.Threading.Interlocked.Exchange(ref _invalidatePending, 0);
                if (!_disposed) _canvas.Invalidate();
            });
        }
    }

    public void HandleChar(char ch)
    {
        if (_disposed) return;
        _input.OnChar(ch);
        _caretBlink = true;
        _blinkTimer.Stop();
        _blinkTimer.Start();
    }
}

internal sealed class EditorCanvas : GraphicsView, IDrawable
{
    private readonly EditorModel        _model;
    private readonly SyntaxTokenizer    _tokens;
    private readonly CompletionState    _completion;
    private readonly SignatureState     _signature;
    private readonly HoverState         _hover;
    private List<Diagnostic>            _diagnostics;
    private List<TextPosition>          _searchHits;
    private int                         _searchCurrent;
    private float                       _scrollRow;
    private bool                        _caretBlink;

    public EditorCanvas(
        EditorModel model, SyntaxTokenizer tokens,
        CompletionState completion, SignatureState sig, HoverState hover,
        List<Diagnostic> diag, List<TextPosition> hits,
        ref int searchCurrent, ref float scrollRow, ref bool caretBlink)
    {
        _model         = model;
        _tokens        = tokens;
        _completion    = completion;
        _signature     = sig;
        _hover         = hover;
        _diagnostics   = diag;
        _searchHits    = hits;
        _searchCurrent = searchCurrent;
        _scrollRow     = scrollRow;
        _caretBlink    = caretBlink;
        Drawable       = this;
        BackgroundColor = Theme.Bg;
    }

    public void SetScroll(float row)        => _scrollRow     = row;
    public void UpdateDiagnostics(List<Diagnostic> d) => _diagnostics = d;
    public void UpdateSearch(List<TextPosition> h, int c) { _searchHits = h; _searchCurrent = c; }

    internal void SyncState(bool caretBlink) => _caretBlink = caretBlink;

    public void Draw(ICanvas canvas, RectF dirty)
    {
        var bracket = BracketMatcher.Find(_model.Buffer, _model.Caret);
        EditorRenderer.Draw(
            canvas, dirty, _model, _tokens, _diagnostics,
            bracket, _searchHits, _searchCurrent,
            _caretBlink, _scrollRow);
    }
}