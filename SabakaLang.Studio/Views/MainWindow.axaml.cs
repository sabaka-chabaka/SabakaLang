using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Editing;
using SabakaLang.Compiler;
using SabakaLang.LanguageServer;
using SabakaLang.Runtime;
using SabakaLang.Studio.Completion;
using SabakaLang.Studio.Highlighting;

namespace SabakaLang.Studio.Views;

public partial class MainWindow : Window
{
    private readonly DocumentStore _store = new();
    private SabakaHighlightingColorizer? _colorizer;
    private SabakaCompletionProvider? _completionProvider;
    private CompletionWindow? _completionWindow;
    
    public MainWindow()
    {
        InitializeComponent();
        SetupHighlighting();
        SetupCompletion();
        Editor.TextArea.TextEntered += OnTextEntered;
    }

    private void SetupHighlighting()
    {
        var editor = this.FindControl<TextEditor>("Editor");
        if (editor is null) return;

        _colorizer = new SabakaHighlightingColorizer(_store);
        editor.TextArea.TextView.LineTransformers.Add(_colorizer);

        _colorizer.UpdateSource(editor.Text);

        editor.Document.TextChanged += (_, _) =>
        {
            _colorizer.UpdateSource(editor.Text);
        };
    }

    private void SetupCompletion()
    {
        var editor = this.FindControl<TextEditor>("Editor");
        if (editor is null) return;

        _completionProvider = new SabakaCompletionProvider(_store);

        editor.TextArea.TextEntered += (sender, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Text))
                return;

            var completions = _completionProvider.GetCompletions(
                editor.Text,
                editor.CaretOffset
            );

            if (completions.Count == 0)
                return;

            var caret = editor.TextArea.Caret.Offset;
            var text = editor.TextArea.Document.Text;

            int start = caret;

            while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_'))
                start--;

            _completionWindow = new CompletionWindow(editor.TextArea)
            {
                StartOffset = start,
                EndOffset = caret
            };
            var data = _completionWindow.CompletionList.CompletionData;

            foreach (var c in completions)
                data.Add(c);

            _completionWindow.Show();

            _completionWindow.Closed += (_, _) =>
            {
                _completionWindow = null;
            };
        };
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

    public void Run_OnClick(object? sender, RoutedEventArgs routedEventArgs)
    {
        var lex = new Lexer(Editor.Text).Tokenize();
        var parser = new Parser(lex).Parse();
        var bind = new Binder().Bind(parser.Statements);
        var comp = new Compiler.Compiler();
        var result = comp.Compile(parser.Statements, bind);
        
        var vm = new VirtualMachine();
        vm.Execute(result.Code.ToList());
    }
}