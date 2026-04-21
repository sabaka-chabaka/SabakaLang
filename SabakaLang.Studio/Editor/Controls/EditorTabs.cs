using Microsoft.Maui.Controls.Shapes;
using SabakaLang.Studio.Editor.Highlighting;

namespace SabakaLang.Studio.Editor.Controls;

public sealed class EditorTab : BindableObject
{
    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(EditorTab), "untitled");
    public static readonly BindableProperty FilePathProperty =
        BindableProperty.Create(nameof(FilePath), typeof(string), typeof(EditorTab), "");
    public static readonly BindableProperty IsDirtyProperty =
        BindableProperty.Create(nameof(IsDirty), typeof(bool), typeof(EditorTab), false);
    public static readonly BindableProperty IsActiveProperty =
        BindableProperty.Create(nameof(IsActive), typeof(bool), typeof(EditorTab), false);

    public string Title    { get => (string)GetValue(TitleProperty);  set => SetValue(TitleProperty,  value); }
    public string FilePath { get => (string)GetValue(FilePathProperty); set => SetValue(FilePathProperty, value); }
    public bool   IsDirty  { get => (bool)GetValue(IsDirtyProperty);  set => SetValue(IsDirtyProperty,  value); }
    public bool   IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
}

public sealed class EditorTabBar : ContentView
{
    private readonly ScrollView          _scroll;
    private readonly HorizontalStackLayout _stack;
    private readonly List<EditorTab>     _tabs = [];
    private int                          _activeIndex = -1;

    public event Action<EditorTab>? TabSelected;
    public event Action<EditorTab>? TabClosed;
    public event Action?            NewTabRequested;

    public EditorTabBar()
    {
        BackgroundColor = StudioTheme.TabBackground;
        HeightRequest   = 36;

        _stack = new HorizontalStackLayout { Spacing = 0 };

        var newBtn = new Label
        {
            Text            = "+",
            FontSize        = 18,
            TextColor       = StudioTheme.TabText,
            VerticalOptions = LayoutOptions.Center,
            Padding         = new Thickness(12, 0),
            HeightRequest   = 36
        };
        var tapNew = new TapGestureRecognizer();
        tapNew.Tapped += (_, _) => NewTabRequested?.Invoke();
        newBtn.GestureRecognizers.Add(tapNew);
        _stack.Children.Add(newBtn);

        _scroll = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            Content     = _stack,
            HeightRequest = 36
        };

        Content = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            Children = { _scroll }
        };
        Grid.SetColumn(_scroll, 0);
    }

    public IReadOnlyList<EditorTab> Tabs => _tabs;
    public EditorTab? ActiveTab => _activeIndex >= 0 ? _tabs[_activeIndex] : null;

    public EditorTab AddTab(string filePath, string title)
    {
        var tab = new EditorTab { FilePath = filePath, Title = title };
        _tabs.Add(tab);
        _stack.Children.Insert(_stack.Children.Count - 1, BuildTabView(tab));
        SetActive(_tabs.Count - 1);
        return tab;
    }

    public void CloseTab(EditorTab tab)
    {
        var idx = _tabs.IndexOf(tab);
        if (idx < 0) return;

        var view = _stack.Children[idx];
        _stack.Children.Remove(view);
        _tabs.RemoveAt(idx);

        if (_tabs.Count == 0) { _activeIndex = -1; return; }

        var newActive = Math.Min(idx, _tabs.Count - 1);
        SetActive(newActive);
        TabSelected?.Invoke(_tabs[newActive]);
    }

    public void SetDirty(string filePath, bool dirty)
    {
        var tab = _tabs.FirstOrDefault(t => t.FilePath == filePath);
        if (tab is not null) tab.IsDirty = dirty;
    }

    public void ActivateByPath(string filePath)
    {
        var idx = _tabs.FindIndex(t => t.FilePath == filePath);
        if (idx >= 0) SetActive(idx);
    }

    private void SetActive(int idx)
    {
        if (_activeIndex >= 0 && _activeIndex < _tabs.Count)
            _tabs[_activeIndex].IsActive = false;
        _activeIndex = idx;
        if (idx >= 0 && idx < _tabs.Count)
            _tabs[idx].IsActive = true;
    }

    private View BuildTabView(EditorTab tab)
    {
        var title = new Label
        {
            FontFamily      = StudioTheme.FontFamily,
            FontSize        = 13,
            VerticalOptions = LayoutOptions.Center
        };
        title.SetBinding(Label.TextProperty, new Binding(nameof(EditorTab.Title), source: tab));
        title.SetBinding(Label.TextColorProperty, new Binding(nameof(EditorTab.IsActive), source: tab,
            converter: new BoolToColorConverter(
                StudioTheme.TabText,
                Colors.Gray)));

        var dirty = new Ellipse
        {
            WidthRequest = 7, HeightRequest = 7,
            Fill = new SolidColorBrush(StudioTheme.TabDirtyDot),
            VerticalOptions = LayoutOptions.Center
        };
        dirty.SetBinding(IsVisibleProperty, new Binding(nameof(EditorTab.IsDirty), source: tab));

        var close = new Label
        {
            Text            = "×",
            FontSize        = 15,
            TextColor       = Colors.Gray,
            VerticalOptions = LayoutOptions.Center,
            Padding         = new Thickness(4, 0, 0, 0)
        };
        var closeGesture = new TapGestureRecognizer();
        closeGesture.Tapped += (_, _) => { TabClosed?.Invoke(tab); CloseTab(tab); };
        close.GestureRecognizers.Add(closeGesture);

        var row = new HorizontalStackLayout
        {
            Spacing = 6,
            Padding = new Thickness(12, 0, 8, 0),
            HeightRequest = 36,
            VerticalOptions = LayoutOptions.Center,
            Children = { title, dirty, close }
        };

        var indicator = new BoxView
        {
            HeightRequest = 2,
            VerticalOptions = LayoutOptions.End,
            BackgroundColor = StudioTheme.GitModified
        };
        indicator.SetBinding(IsVisibleProperty, new Binding(nameof(EditorTab.IsActive), source: tab));

        var cell = new Grid
        {
            HeightRequest = 36,
            BackgroundColor = Colors.Transparent,
            Children = { row, indicator }
        };
        cell.SetBinding(BackgroundColorProperty, new Binding(nameof(EditorTab.IsActive), source: tab,
            converter: new BoolToColorConverter(
                StudioTheme.TabActive,
                StudioTheme.TabInactive)));

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            var idx = _tabs.IndexOf(tab);
            if (idx >= 0) SetActive(idx);
            TabSelected?.Invoke(tab);
        };
        cell.GestureRecognizers.Add(tap);

        return cell;
    }
}

public sealed class BoolToColorConverter : IValueConverter
{
    private readonly Color _true, _false;
    public BoolToColorConverter(Color t, Color f) { _true = t; _false = f; }
    public object? Convert(object? v, Type t, object? p, System.Globalization.CultureInfo c)
        => v is true ? _true : _false;
    public object? ConvertBack(object? v, Type t, object? p, System.Globalization.CultureInfo c)
        => throw new NotImplementedException();
}