using System.Drawing;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Win32;
using SabakaIDE.Services;
using SabakaLang.Lexer;

namespace SabakaIDE;

public partial class MainWindow : Window
{
    private Colorizer _colorizer;
    private BuildRunService _buildRunService;
    private ObservableTextReader _inputReader = new();
    private TextBoxWriter? _outputWriter;
    private string? _currentFilePath;
    private bool _isDirty = false;


    public MainWindow()
    {
        InitializeComponent();

        _colorizer = new Colorizer();
        _buildRunService = new();
        _outputWriter = new TextBoxWriter(OutputBox);

        Editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        Editor.TextChanged += Editor_TextChanged;
        Editor.TextArea.TextEntering += TextArea_TextEntering;
        Editor.TextArea.TextEntered += TextArea_TextEntered;
        Editor.TextChanged += (s, e) =>
        {
            _isDirty = true;
            UpdateTitle();
        };
    }

    private async void Editor_TextChanged(object sender, EventArgs e)
    {
        await Task.Delay(200);

        var lexer = new Lexer(Editor.Text);
        var tokens = lexer.Tokenize(true);

        _colorizer.SetTokens(tokens);
        Editor.TextArea.TextView.Redraw();
    }

    private void TextArea_TextEntering(object? sender, TextCompositionEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text))
            return;

        var caret = Editor.TextArea.Caret;
        var doc = Editor.Document;
        int offset = caret.Offset;

        char input = e.Text[0];

        if (input == ';' && Editor.SelectionLength == 0)
        {
            int depth = 0;
            bool inFor = false;
            int openParenPos = -1;
            for (int i = offset - 1; i >= 0; i--)
            {
                char c = doc.GetCharAt(i);
                if (c == ')') depth++;
                else if (c == '(')
                {
                    if (depth == 0)
                    {
                        openParenPos = i;
                        break;
                    }
                    else depth--;
                }
                else if (c == '{' || c == '}') break;
            }

            if (openParenPos != -1)
            {
                int j = openParenPos - 1;
                while (j >= 0 && char.IsWhiteSpace(doc.GetCharAt(j))) j--;
                if (j >= 2 && doc.GetText(j - 2, 3) == "for")
                {
                    if (j - 3 < 0 || !char.IsLetterOrDigit(doc.GetCharAt(j - 3)))
                        inFor = true;
                }

                if (!inFor)
                {
                    depth = 0;
                    for (int i = offset; i < doc.TextLength; i++)
                    {
                        char c = doc.GetCharAt(i);
                        if (c == '(') depth++;
                        else if (c == ')')
                        {
                            if (depth == 0)
                            {
                                doc.Insert(i + 1, ";");
                                Editor.CaretOffset = i + 2;
                                e.Handled = true;
                                return;
                            }
                            else depth--;
                        }
                        else if (c == '{' || c == '}') break;
                    }
                }
            }
        }

        string? closing = input switch
        {
            '(' => ")",
            '{' => "}",
            '[' => "]",
            '"' => "\"",
            '\'' => "'",
            _ => null
        };

        if (closing == null)
            return;

        if (Editor.SelectionLength > 0)
        {
            var selected = Editor.SelectedText;
            Editor.Document.Replace(
                Editor.SelectionStart,
                Editor.SelectionLength,
                input + selected + closing);

            e.Handled = true;
            return;
        }

        doc.Insert(offset, closing);

        Editor.CaretOffset = offset;
    }

    private void TextArea_TextEntered(object? sender, TextCompositionEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text))
            return;

        char input = e.Text[0];

        if (input == ')' || input == '}' || input == ']' || input == '"' || input == '\'')
        {
            var caret = Editor.TextArea.Caret;
            var doc = Editor.Document;

            if (caret.Offset < doc.TextLength &&
                doc.GetCharAt(caret.Offset) == input)
            {
                Editor.TextArea.Caret.Offset++;
                e.Handled = true;
            }
        }
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        OutputBox.Clear();
        InputBox.Clear();
        _inputReader.Clear();
        string source = Editor.Text;

        RunButton.IsEnabled = false;
        InputBox.Focus();
        try
        {
            await Task.Run(() => _buildRunService.Run(source, _inputReader, _outputWriter!));
        }
        finally
        {
            RunButton.IsEnabled = true;
        }
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            string text = InputBox.Text;
            InputBox.Clear();
            _outputWriter?.WriteLine(text);
            _inputReader.ProvideLine(text);
        }
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        SaveFile();
    }

    private void SaveFile()
    {
        if (_currentFilePath == null)
        {
            SaveFileAs();
            return;
        }

        File.WriteAllText(_currentFilePath, Editor.Text);
        _isDirty = false;
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        string fileName = _currentFilePath == null
            ? "Untitled"
            : System.IO.Path.GetFileName(_currentFilePath);

        Title = $"{fileName}{(_isDirty ? "*" : "")} - SabakaIDE";
    }

    private void SaveFileAs()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "SabakaLang files (*.sabaka)|*.sabaka|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            File.WriteAllText(dialog.FileName, Editor.Text);
            _currentFilePath = dialog.FileName;
            _isDirty = false;
            UpdateTitle();
        }
    }

    private void OpenButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenFile();
    }

    private void OpenFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Sabaka files (*.sabaka)|*.sabaka|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            Editor.Text = File.ReadAllText(dialog.FileName);
            _currentFilePath = dialog.FileName;
            _isDirty = false;
            UpdateTitle();
        }
    }


    private void SaveAsButton_OnClick(object sender, RoutedEventArgs e)
    {
        SaveFileAs();
    }

    private void Save_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        SaveFile();
    }

    private void Save_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = true;
    }
    
    private void Run_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        RunButton_Click(sender, e);
    }

    private void Run_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = true;
    }
}