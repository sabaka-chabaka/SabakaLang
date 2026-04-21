using Microsoft.Maui.Controls;
using SabakaLang.Studio.Editor.Controls;
using SabakaLang.Studio.Editor.Core;
using SabakaLang.Studio.Editor.Highlighting;

namespace SabakaLang.Studio.Search;

public sealed class FindReplacePanel : ContentView
{
    private readonly Entry   _findEntry;
    private readonly Entry   _replaceEntry;
    private readonly Label   _matchCount;
    private readonly CheckBox _caseSensitive;
    private readonly CheckBox _wholeWord;

    private EditorView? _editor;
    private List<CaretPosition> _hits = [];
    private int _currentHit = -1;

    public FindReplacePanel()
    {
        IsVisible       = false;
        BackgroundColor = StudioTheme.Surface;

        _findEntry = new Entry
        {
            Placeholder      = "Find…",
            PlaceholderColor = Colors.Gray,
            TextColor        = StudioTheme.Plain,
            BackgroundColor  = StudioTheme.Background,
            FontFamily       = StudioTheme.FontFamily,
            FontSize         = 13,
            HeightRequest    = 28
        };
        _findEntry.TextChanged += (_, _) => Search();
        _findEntry.Completed   += (_, _) => MoveNext();

        _replaceEntry = new Entry
        {
            Placeholder      = "Replace…",
            PlaceholderColor = Colors.Gray,
            TextColor        = StudioTheme.Plain,
            BackgroundColor  = StudioTheme.Background,
            FontFamily       = StudioTheme.FontFamily,
            FontSize         = 13,
            HeightRequest    = 28
        };

        _matchCount = new Label
        {
            TextColor       = Colors.Gray,
            FontSize        = 11,
            VerticalOptions = LayoutOptions.Center,
            WidthRequest    = 70
        };

        _caseSensitive = new CheckBox { Color = Colors.Gray };
        _wholeWord     = new CheckBox { Color = Colors.Gray };
        _caseSensitive.CheckedChanged += (_, _) => Search();

        var prevBtn  = IconBtn("↑", MoveNext);
        var nextBtn  = IconBtn("↓", MoveNext);
        var closeBtn = IconBtn("×", Hide);
        var repBtn   = SmallBtn("Replace", DoReplace);
        var repAllBtn= SmallBtn("All",     DoReplaceAll);

        var findRow = new HorizontalStackLayout
        {
            Padding  = new Thickness(6, 4),
            Spacing  = 4,
            Children =
            {
                _findEntry,
                _matchCount,
                prevBtn,
                nextBtn,
                new Label { Text = "Aa", TextColor = Colors.Gray, FontSize = 12, VerticalOptions = LayoutOptions.Center },
                _caseSensitive,
                new Label { Text = "W", TextColor = Colors.Gray, FontSize = 12, VerticalOptions = LayoutOptions.Center },
                _wholeWord,
                closeBtn
            }
        };

        var replaceRow = new HorizontalStackLayout
        {
            Padding  = new Thickness(6, 0, 6, 4),
            Spacing  = 4,
            Children = { _replaceEntry, repBtn, repAllBtn }
        };

        Content = new StackLayout
        {
            Spacing  = 0,
            Children = { findRow, replaceRow }
        };
    }

    public void Open(EditorView editor)
    {
        _editor   = editor;
        IsVisible = true;
        _findEntry.Focus();
        var sel = editor.Document;
    }

    public new void Hide()
    {
        IsVisible = false;
        _editor?.SetSearchResults([], -1);
        _hits.Clear();
    }

    private void Search()
    {
        if (_editor is null || string.IsNullOrEmpty(_findEntry.Text))
        {
            _hits.Clear();
            _editor?.SetSearchResults([], -1);
            _matchCount.Text = "";
            return;
        }

        _hits = _editor.Document
            .FindAll(_findEntry.Text, _caseSensitive.IsChecked)
            .ToList();

        _currentHit = _hits.Count > 0 ? 0 : -1;
        _matchCount.Text = _hits.Count > 0 ? $"{_currentHit + 1}/{_hits.Count}" : "No matches";

        _editor.SetSearchResults(_hits, _currentHit);
        if (_currentHit >= 0) _editor.ScrollToLine(_hits[_currentHit].Line);
    }

    private void MoveNext()
    {
        if (_hits.Count == 0) return;
        _currentHit = (_currentHit + 1) % _hits.Count;
        _matchCount.Text = $"{_currentHit + 1}/{_hits.Count}";
        _editor?.SetSearchResults(_hits, _currentHit);
        _editor?.ScrollToLine(_hits[_currentHit].Line);
    }

    private void MovePrev()
    {
        if (_hits.Count == 0) return;
        _currentHit = (_currentHit - 1 + _hits.Count) % _hits.Count;
        _matchCount.Text = $"{_currentHit + 1}/{_hits.Count}";
        _editor?.SetSearchResults(_hits, _currentHit);
        _editor?.ScrollToLine(_hits[_currentHit].Line);
    }

    private void DoReplace()
    {
        if (_editor is null || _currentHit < 0 || _currentHit >= _hits.Count) return;
        var pos = _hits[_currentHit];
        var needle = _findEntry.Text ?? "";
        var repl   = _replaceEntry.Text ?? "";
        _editor.Document.Delete(pos, new CaretPosition(pos.Line, pos.Column + needle.Length));
        _editor.Document.Insert(pos, repl);
        Search();
    }

    private void DoReplaceAll()
    {
        if (_editor is null) return;
        var count = _editor.Document.ReplaceAll(
            _findEntry.Text ?? "",
            _replaceEntry.Text ?? "",
            _caseSensitive.IsChecked);
        _matchCount.Text = $"Replaced {count}";
        _hits.Clear();
        _editor.SetSearchResults([], -1);
    }
    
    private static Label IconBtn(string icon, Action action)
    {
        var lbl = new Label
        {
            Text    = icon,
            FontSize= 15,
            TextColor = Colors.Gray,
            VerticalOptions = LayoutOptions.Center,
            Padding = new Thickness(6, 0)
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => action();
        lbl.GestureRecognizers.Add(tap);
        return lbl;
    }

    private static Button SmallBtn(string text, Action action) => new()
    {
        Text = text, FontSize = 11,
        BackgroundColor = Colors.Transparent,
        TextColor       = Colors.Gray,
        BorderColor     = Colors.DimGray,
        BorderWidth     = 1, CornerRadius = 3,
        HeightRequest   = 24, Padding = new Thickness(6, 0),
        Command = new Command(action)
    };
}

public sealed class CommandPalette : ContentView
{
    private readonly Entry   _input;
    private readonly ListView _results;
    private List<PaletteCommand> _allCommands = [];
    private List<PaletteCommand> _filtered    = [];

    public event Action<PaletteCommand>? CommandSelected;

    public CommandPalette()
    {
        IsVisible       = false;
        BackgroundColor = StudioTheme.PopupBackground;
        HorizontalOptions = LayoutOptions.Center;
        VerticalOptions   = LayoutOptions.Start;
        WidthRequest      = 560;
        Margin            = new Thickness(0, 60, 0, 0);

        _input = new Entry
        {
            Placeholder      = "Search commands, files, symbols…",
            PlaceholderColor = Colors.Gray,
            TextColor        = StudioTheme.Plain,
            BackgroundColor  = StudioTheme.Background,
            FontFamily       = StudioTheme.FontFamily,
            FontSize         = 14,
            HeightRequest    = 40
        };
        _input.TextChanged += (_, e) => Filter(e.NewTextValue);

        _results = new ListView
        {
            BackgroundColor   = Colors.Transparent,
            SeparatorColor    = StudioTheme.Border,
            HeightRequest     = 320,
            ItemTemplate      = new DataTemplate(BuildCell)
        };
        _results.ItemSelected += (_, e) =>
        {
            if (e.SelectedItem is PaletteCommand cmd)
            {
                Hide();
                CommandSelected?.Invoke(cmd);
                cmd.Execute?.Invoke();
            }
        };

        Content = new Frame
        {
            Padding         = 0,
            CornerRadius    = 6,
            BackgroundColor = StudioTheme.PopupBackground,
            BorderColor     = StudioTheme.PopupBorder,
            HasShadow       = true,
            Content         = new StackLayout
            {
                Spacing  = 0,
                Children = { _input, _results }
            }
        };
    }

    public void Register(IEnumerable<PaletteCommand> commands)
        => _allCommands.AddRange(commands);

    public void Open()
    {
        IsVisible = true;
        _input.Text = "";
        Filter("");
        _input.Focus();
    }

    public new void Hide() => IsVisible = false;

    private void Filter(string query)
    {
        _filtered = string.IsNullOrWhiteSpace(query)
            ? _allCommands.Take(20).ToList()
            : _allCommands
                .Where(c => c.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                         || (c.Category?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
                .Take(20).ToList();

        _results.ItemsSource = _filtered;
    }

    private static Cell BuildCell()
    {
        var icon     = new Label { FontSize = 13, WidthRequest = 20, TextColor = Colors.Gray, VerticalOptions = LayoutOptions.Center };
        var title    = new Label { FontFamily = StudioTheme.FontFamily, FontSize = 13,
                                   TextColor = StudioTheme.PopupText, VerticalOptions = LayoutOptions.Center };
        var category = new Label { FontSize = 11, TextColor = Colors.Gray, VerticalOptions = LayoutOptions.Center };
        var shortcut = new Label { FontSize = 11, TextColor = Colors.DimGray, HorizontalOptions = LayoutOptions.End,
                                   VerticalOptions = LayoutOptions.Center };

        var row = new Grid
        {
            Padding = new Thickness(12, 4),
            ColumnDefinitions =
            [
                new ColumnDefinition(new GridLength(24)),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(90))
            ],
            Children = { icon, title, category, shortcut }
        };
        Grid.SetColumn(title,    1);
        Grid.SetColumn(category, 2);
        Grid.SetColumn(shortcut, 3);

        var cell = new ViewCell { View = row };
        cell.BindingContextChanged += (_, _) =>
        {
            if (cell.BindingContext is not PaletteCommand c) return;
            icon.Text     = c.Icon ?? "•";
            title.Text    = c.Title;
            category.Text = c.Category is not null ? $" — {c.Category}" : "";
            shortcut.Text = c.Shortcut ?? "";
        };

        return cell;
    }
}

public sealed class PaletteCommand
{
    public string    Title    { get; init; } = "";
    public string?   Category { get; init; }
    public string?   Icon     { get; init; }
    public string?   Shortcut { get; init; }
    public Action?   Execute  { get; init; }
}