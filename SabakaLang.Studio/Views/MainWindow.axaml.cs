using Avalonia.Controls;
using AvaloniaEdit;
using SabakaLang.LanguageServer;
using SabakaLang.Studio.Highlighting;

namespace SabakaLang.Studio.Views;

public partial class MainWindow : Window
{
    private readonly DocumentStore _store = new();
    private SabakaHighlightingColorizer? _colorizer;

    public MainWindow()
    {
        InitializeComponent();
        SetupHighlighting();
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
}