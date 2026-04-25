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

    private CompletionWindow? _completionWindow;

    private readonly DispatcherTimer _debounce;
    private string _pendingSource = "";

    public MainWindow()
    {
        InitializeComponent();

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); RunAnalysis(_pendingSource); };

        var editor = this.FindControl<TextEditor>("Editor")!;

        SetupHighlighting(editor);
        SetupDiagnostics(editor);
        SetupCompletion(editor);

        editor.Document.TextChanged += (_, _) =>
        {
            _colorizer?.UpdateSource(editor.Text);
        };
    }

    private void SetupHighlighting(TextEditor editor)
    {
        _colorizer = new SabakaHighlightingColorizer(_store);
        editor.TextArea.TextView.LineTransformers.Add(_colorizer);
    }

    private void SetupDiagnostics(TextEditor editor)
    {
        _diagRenderer = new DiagnosticsRenderer(editor.TextArea.TextView);
        editor.TextArea.TextView.BackgroundRenderers.Add(_diagRenderer);

        _diagPanel = new DiagnosticsPanel(editor);
        var host = this.FindControl<ContentControl>("DiagnosticsPanelHost")!;
        host.Content = _diagPanel.Root;
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
        try
        {
            items = _completionProvider!.GetCompletions(editor.Text, editor.CaretOffset).ToList();
        }
        catch
        {
            return;
        }

        if (items.Count == 0) return;

        _completionWindow = new CompletionWindow(editor.TextArea);
        var list = _completionWindow.CompletionList.CompletionData;
        foreach (var item in items)
            list.Add(item);

        _completionWindow.Closed += (_, _) => _completionWindow = null;
        _completionWindow.Show();
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

        var editor = this.FindControl<TextEditor>("Editor");
        if (editor is null) return;

        var diags = analysis?.Diagnostics ?? [];
        _diagRenderer?.Update(diags);
        _diagPanel?.Update(diags);

        editor.TextArea.TextView.InvalidateLayer(
            AvaloniaEdit.Rendering.KnownLayer.Background);
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        var editor = this.FindControl<TextEditor>("Editor")!;

        editor.Document.TextChanged += (_, _) => ScheduleAnalysis(editor.Text);

        ScheduleAnalysis(editor.Text);
    }
    
    
    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (sender is not TextArea area)
            return;

        var doc = area.Document;
        var offset = area.Caret.Offset;

        switch (e.Text)
        {
            case "(":
                doc.Insert(offset, ")");
                area.Caret.Offset = offset;
                break;

            case "{":
                doc.Insert(offset, "}");
                area.Caret.Offset = offset;
                break;

            case "[":
                doc.Insert(offset, "]");
                area.Caret.Offset = offset;
                break;

            case "\"":
                doc.Insert(offset, "\"");
                area.Caret.Offset = offset;
                break;

            case "'":
                doc.Insert(offset, "'");
                area.Caret.Offset = offset;
                break;
        }
    }

    public async void Run_OnClick(object? sender, RoutedEventArgs routedEventArgs)
    {
        var lex = new Lexer(Editor.Text).Tokenize();
        var parser = new Parser(lex).Parse();
        var bind = new Binder().Bind(parser.Statements);
        var comp = new Compiler.Compiler();
        var result = comp.Compile(parser.Statements, bind);

        ConsoleHelper.Show();

        var vm = new VirtualMachine();
        vm.Execute(result.Code.ToList());

        await Task.Delay(10000);

        ConsoleHelper.Hide();
    }
}