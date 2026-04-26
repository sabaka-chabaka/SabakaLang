using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Editing;
using SabakaLang.Compiler;
using SabakaLang.LanguageServer;
using SabakaLang.Runtime;
using SabakaLang.Studio.Completion;
using SabakaLang.Studio.Diagnostics;
using SabakaLang.Studio.Helpers;
using SabakaLang.Studio.Highlighting;

namespace SabakaLang.Studio.Views;

public partial class MainWindow : Window
{
    private readonly DocumentStore _store = new();

    private SabakaHighlightingColorizer? _colorizer;
    private DiagnosticsRenderer?         _diagRenderer;
    private DiagnosticsPanel?            _diagPanel;
    private SabakaCompletionProvider?    _completionProvider;
    private CompletionWindow?            _completionWindow;

    private readonly DispatcherTimer _debounce;
    private string _pendingSource = "";

    private TextEditor? _editor;
    private TextBlock?  _statusPosition;
    private TextBlock?  _statusErrors;
    private TextBlock?  _statusWarnings;
    private TextBlock?  _statusMessage;

    public MainWindow()
    {
        InitializeComponent();

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); RunAnalysis(_pendingSource); };
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _editor         = this.FindControl<TextEditor>("Editor")!;
        _statusPosition = this.FindControl<TextBlock>("StatusPosition");
        _statusErrors   = this.FindControl<TextBlock>("StatusErrors");
        _statusWarnings = this.FindControl<TextBlock>("StatusWarnings");
        _statusMessage  = this.FindControl<TextBlock>("StatusMessage");

        SetupHighlighting(_editor);
        SetupDiagnostics(_editor);
        SetupCompletion(_editor);
        SetupCaretTracking(_editor);

        this.FindControl<Button>("BtnRun")!.Click += (_, _) => _ = RunCodeAsync();

        _editor.TextArea.TextEntered += OnTextEntered;
        _editor.Document.TextChanged += (_, _) => ScheduleAnalysis(_editor.Text);

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F5) { _ = RunCodeAsync(); e.Handled = true; }
        };

        ScheduleAnalysis(_editor.Text);
    }

    private void SetupHighlighting(TextEditor editor)
    {
        _colorizer = new SabakaHighlightingColorizer(_store);
        editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        editor.Options.HighlightCurrentLine = true;
    }

    private void SetupDiagnostics(TextEditor editor)
    {
        _diagRenderer = new DiagnosticsRenderer(editor.TextArea.TextView);
        editor.TextArea.TextView.BackgroundRenderers.Add(_diagRenderer);

        _diagPanel = new DiagnosticsPanel(editor);
        this.FindControl<ContentControl>("DiagnosticsPanelHost")!.Content = _diagPanel.Root;
    }

    private void SetupCompletion(TextEditor editor)
    {
        _completionProvider = new SabakaCompletionProvider(_store);

        editor.TextArea.TextEntered += (_, e) =>
        {
            if (e.Text is not { Length: > 0 }) return;
            char ch = e.Text[0];
            ScheduleAnalysis(editor.Text);
            if (char.IsLetter(ch) || ch == '_' || ch == '.' || ch == ':')
                TryShowCompletion(editor);
        };

        editor.TextArea.TextEntering += (_, e) =>
        {
            if (_completionWindow is null) return;
            if (e.Text is not { Length: > 0 }) return;
            char ch = e.Text[0];
            if (!char.IsLetterOrDigit(ch) && ch != '_')
                _completionWindow.CompletionList.RequestInsertion(e);
        };
    }

    private void TryShowCompletion(TextEditor editor)
    {
        if (_completionWindow is not null) return;
        List<SabakaCompletionData> items;
        try { items = _completionProvider!.GetCompletions(editor.Text, editor.CaretOffset).ToList(); }
        catch { return; }
        if (items.Count == 0) return;

        _completionWindow = new CompletionWindow(editor.TextArea);
        foreach (var item in items)
            _completionWindow.CompletionList.CompletionData.Add(item);
        _completionWindow.Closed += (_, _) => _completionWindow = null;
        _completionWindow.Show();
    }

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (sender is not TextArea area) return;
        var doc    = area.Document;
        var offset = area.Caret.Offset;

        string? closing = e.Text switch
        {
            "("  => ")",
            "{"  => "}",
            "["  => "]",
            "\"" => "\"",
            "'"  => "'",
            _    => null
        };

        if (closing is null) return;
        doc.Insert(offset, closing);
        area.Caret.Offset = offset;
    }

    private async Task RunCodeAsync()
    {
        SetStatus("Running...");
        try
        {
            var lex    = new Lexer(_editor!.Text).Tokenize();
            var parse  = new Parser(lex).Parse();
            var bind   = new Binder().Bind(parse.Statements);
            var comp   = new Compiler.Compiler();
            var result = comp.Compile(parse.Statements, bind);

            ConsoleHelper.Show();
            var vm = new VirtualMachine();
            vm.Execute(result.Code.ToList());

            await Task.Delay(10000);
            ConsoleHelper.Hide();
            SetStatus("Ready");
        }
        catch (Exception ex)
        {
            SetStatus($"Runtime error: {ex.Message}");
        }
    }

    private void SetupCaretTracking(TextEditor editor)
    {
        editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (_statusPosition is null) return;
            var pos = editor.TextArea.Caret.Position;
            _statusPosition.Text = $"Ln {pos.Line}, Col {pos.Column}";
        };
    }

    private void ScheduleAnalysis(string source)
    {
        _pendingSource = source;
        _debounce.Stop();
        _debounce.Start();
    }

    private void RunAnalysis(string source)
    {
        DocumentAnalysis? analysis = null;
        try
        {
            _colorizer?.UpdateSource(source);
            analysis = _store.Get("studio://active-document");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SabakaLang] Analysis error: {ex}");
        }

        if (_editor is null) return;

        var diags    = analysis?.Diagnostics ?? [];
        var errors   = diags.Count(d => d.Severity == DiagnosticSeverity.Error);
        var warnings = diags.Count(d => d.Severity == DiagnosticSeverity.Warning);

        _diagRenderer?.Update(diags);
        _diagPanel?.Update(diags);

        if (_statusErrors   is not null) _statusErrors.Text   = $"⛔ {errors}";
        if (_statusWarnings is not null) _statusWarnings.Text = $"⚠ {warnings}";
        SetStatus(errors == 0 && warnings == 0 ? "Ready" : $"{errors} error(s), {warnings} warning(s)");

        _editor.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Background);
    }

    private void SetStatus(string msg)
    {
        if (_statusMessage is not null) _statusMessage.Text = msg;
    }
}