using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Windows.Controls;
using SabakaLang;

namespace SabakaIDE;

public class BuildRunService
{
    public void Run(string source, TextReader? input, TextWriter output)
    {
        try
        {
            SabakaRunner.Run(source, input, output);
        }
        catch (Exception ex)
        {
            output.WriteLine("\nError: " + ex.Message);
        }
    }
}

public class TextBoxWriter : TextWriter
{
    private readonly TextBox _textBox;
    public override Encoding Encoding => Encoding.UTF8;

    public TextBoxWriter(TextBox textBox)
    {
        _textBox = textBox;
    }

    public override void WriteLine(string? value)
    {
        _textBox.Dispatcher.BeginInvoke(() =>
        {
            _textBox.AppendText((value ?? "") + Environment.NewLine);
            _textBox.ScrollToEnd();
        });
    }

    public override void Write(char value)
    {
        _textBox.Dispatcher.BeginInvoke(() =>
        {
            _textBox.AppendText(value.ToString());
            _textBox.ScrollToEnd();
        });
    }

    public override void Write(string? value)
    {
        _textBox.Dispatcher.BeginInvoke(() =>
        {
            _textBox.AppendText(value ?? "");
            _textBox.ScrollToEnd();
        });
    }
}

public class ObservableTextReader : TextReader
{
    private readonly BlockingCollection<string> _lines = new();

    public void ProvideLine(string line)
    {
        _lines.Add(line);
    }

    public override string? ReadLine()
    {
        try
        {
            return _lines.Take();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public void Clear()
    {
        while (_lines.TryTake(out _)) { }
    }
}
