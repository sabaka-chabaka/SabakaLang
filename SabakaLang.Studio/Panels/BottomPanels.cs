using Microsoft.Maui.Controls;
using SabakaLang.Studio.Editor.Controls;
using SabakaLang.Studio.Editor.Highlighting;
using SabakaLang.Studio.Editor.Rendering;
using StudioTheme = SabakaLang.Studio.Editor.Highlighting.StudioTheme;

namespace SabakaLang.Studio.Panels;

public sealed class DiagnosticsPanel : ContentView
{
    private readonly ListView _list;
    private List<DiagnosticSpan> _diagnostics = [];

    public event Action<DiagnosticSpan>? DiagnosticSelected;

    public DiagnosticsPanel()
    {
        BackgroundColor = StudioTheme.Surface;

        var header = new HorizontalStackLayout
        {
            Padding  = new Thickness(12, 4),
            Spacing  = 12,
            Children =
            {
                TabLabel("PROBLEMS"),
            }
        };

        _list = new ListView
        {
            BackgroundColor = Colors.Transparent,
            SeparatorColor  = StudioTheme.Border,
            SeparatorVisibility = SeparatorVisibility.Default,
            ItemTemplate    = new DataTemplate(BuildCell)
        };
        _list.ItemSelected += (_, e) =>
        {
            if (e.SelectedItem is DiagnosticSpan d)
                DiagnosticSelected?.Invoke(d);
        };

        Content = new StackLayout
        {
            Spacing  = 0,
            Children = { header, _list }
        };
    }

    public void Update(List<DiagnosticSpan> diagnostics)
    {
        _diagnostics = diagnostics;
        _list.ItemsSource = _diagnostics;
    }

    private static Cell BuildCell()
    {
        var icon = new Label { FontSize = 13, WidthRequest = 18, VerticalOptions = LayoutOptions.Center };
        var msg  = new Label { FontFamily = StudioTheme.FontFamily, FontSize = 12,
                               TextColor  = StudioTheme.Plain, VerticalOptions = LayoutOptions.Center };
        var loc  = new Label { FontSize = 11, TextColor = Colors.Gray, VerticalOptions = LayoutOptions.Center };

        var row = new HorizontalStackLayout
        {
            Padding = new Thickness(8, 3),
            Spacing = 8,
            Children = { icon, msg, loc }
        };

        var cell = new ViewCell { View = row };
        cell.BindingContextChanged += (_, _) =>
        {
            if (cell.BindingContext is not DiagnosticSpan d) return;
            icon.Text      = d.IsError ? "✕" : "⚠";
            icon.TextColor = d.IsError ? StudioTheme.DiagError : StudioTheme.DiagWarning;
            msg.Text       = d.Message;
            loc.Text       = $"Ln {d.Line + 1}, Col {d.StartCol + 1}";
        };
        return cell;
    }

    private static Label TabLabel(string t) => new()
    {
        Text = t, FontSize = 11, TextColor = Colors.Gray,
        VerticalOptions = LayoutOptions.Center
    };
}

public sealed class OutputPanel : ContentView
{
    private readonly Microsoft.Maui.Controls.Editor _textEditor;
    private readonly Label  _titleLabel;

    public OutputPanel()
    {
        BackgroundColor = StudioTheme.Surface;

        _titleLabel = new Label
        {
            Text = "OUTPUT", FontSize = 11, TextColor = Colors.Gray,
            Padding = new Thickness(12, 4)
        };

        _textEditor = new Microsoft.Maui.Controls.Editor
        {
            IsReadOnly       = true,
            BackgroundColor  = StudioTheme.Background,
            TextColor        = StudioTheme.Plain,
            FontFamily       = StudioTheme.FontFamily,
            FontSize         = 12,
            AutoSize         = EditorAutoSizeOption.TextChanges
        };

        var clearBtn = new Button
        {
            Text = "Clear", FontSize = 11,
            BackgroundColor = Colors.Transparent,
            TextColor = Colors.Gray,
            BorderColor = Colors.DimGray,
            BorderWidth = 1, CornerRadius = 3,
            HeightRequest = 22, Padding = new Thickness(6, 0),
            HorizontalOptions = LayoutOptions.End,
            Margin = new Thickness(0, 0, 8, 0)
        };
        clearBtn.Clicked += (_, _) => Clear();

        var header = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            Children = { _titleLabel, clearBtn }
        };
        Grid.SetColumn(clearBtn, 1);

        Content = new StackLayout
        {
            Spacing  = 0,
            Children = { header, new ScrollView { Content = _textEditor } }
        };
    }

    public void AppendLine(string text)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _textEditor.Text += text + "\n";
        });
    }

    public void AppendError(string text)  => AppendLine($"[ERROR] {text}");
    public void AppendInfo(string text)   => AppendLine($"[INFO]  {text}");
    public void AppendSuccess(string text)=> AppendLine($"[OK]    {text}");

    public void Clear() => _textEditor.Text = "";

    public void SetTitle(string title) => _titleLabel.Text = title.ToUpperInvariant();
}

public sealed class TerminalPanel : ContentView
{
    private readonly Microsoft.Maui.Controls.Editor  _output;
    private readonly Entry   _input;
    private System.Diagnostics.Process? _shell;
    private readonly List<string> _history = [];
    private int _historyIndex = -1;

    public TerminalPanel()
    {
        BackgroundColor = StudioTheme.Background;

        _output = new Microsoft.Maui.Controls.Editor
        {
            IsReadOnly      = true,
            BackgroundColor = Colors.Transparent,
            TextColor       = StudioTheme.Plain,
            FontFamily      = StudioTheme.FontFamily,
            FontSize        = 12
        };

        _input = new Entry
        {
            Placeholder      = "$ ",
            PlaceholderColor = Colors.Gray,
            TextColor        = StudioTheme.Plain,
            BackgroundColor  = Colors.Transparent,
            FontFamily       = StudioTheme.FontFamily,
            FontSize         = 12
        };
        _input.Completed += OnInputSubmit;

        var header = new Label
        {
            Text = "TERMINAL", FontSize = 11, TextColor = Colors.Gray,
            Padding = new Thickness(12, 4)
        };

        Content = new StackLayout
        {
            Spacing  = 0,
            Children =
            {
                header,
                new ScrollView { Content = _output, VerticalOptions = LayoutOptions.FillAndExpand },
                new BoxView { HeightRequest = 1, BackgroundColor = StudioTheme.Border },
                _input
            }
        };

        StartShell();
    }

    private void StartShell()
    {
        var shell = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash";
        var psi = new System.Diagnostics.ProcessStartInfo(shell)
        {
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };
        try
        {
            _shell = System.Diagnostics.Process.Start(psi);
            _shell!.OutputDataReceived += (_, e) => Append(e.Data);
            _shell.ErrorDataReceived   += (_, e) => Append("[err] " + e.Data);
            _shell.BeginOutputReadLine();
            _shell.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            Append($"Cannot start shell: {ex.Message}");
        }
    }

    private void OnInputSubmit(object? s, EventArgs e)
    {
        var cmd = _input.Text ?? "";
        _input.Text = "";
        if (string.IsNullOrWhiteSpace(cmd)) return;
        _history.Add(cmd);
        _historyIndex = _history.Count;
        Append($"$ {cmd}");
        try { _shell?.StandardInput.WriteLine(cmd); }
        catch { Append("Shell not running. Restart panel."); }
    }

    private void Append(string? text)
    {
        if (text is null) return;
        MainThread.BeginInvokeOnMainThread(() => _output.Text += text + "\n");
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        // Could wire keyboard up/down for history here
    }
}

public sealed class BottomPanelHost : ContentView
{
    public readonly DiagnosticsPanel Diagnostics = new();
    public readonly OutputPanel      Output      = new();
    public readonly TerminalPanel    Terminal    = new();

    private int _activeTab = 0;

    public BottomPanelHost()
    {
        BackgroundColor = StudioTheme.Surface;

        var tabs = new HorizontalStackLayout
        {
            Spacing = 0,
            BackgroundColor = StudioTheme.Surface,
            Children =
            {
                MakeTab("PROBLEMS", 0),
                MakeTab("OUTPUT",   1),
                MakeTab("TERMINAL", 2)
            }
        };

        var panels = new Grid
        {
            Children = { Diagnostics, Output, Terminal }
        };

        SetActivePanel(panels, 0);

        Content = new StackLayout
        {
            Spacing  = 0,
            Children = { tabs, panels }
        };
    }

    private Label MakeTab(string title, int idx)
    {
        var lbl = new Label
        {
            Text    = title,
            FontSize= 11,
            TextColor = idx == 0 ? StudioTheme.Plain : Colors.Gray,
            Padding = new Thickness(12, 6)
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            _activeTab = idx;
            if (Content is StackLayout sl && sl.Children[1] is Grid g)
                SetActivePanel(g, idx);
        };
        lbl.GestureRecognizers.Add(tap);
        return lbl;
    }

    private static void SetActivePanel(Grid grid, int idx)
    {
        for (var i = 0; i < grid.Children.Count; i++)
            ((View)grid.Children[i]).IsVisible = i == idx;
    }
}