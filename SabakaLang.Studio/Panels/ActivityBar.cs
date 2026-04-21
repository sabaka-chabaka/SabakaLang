using Microsoft.Maui.Controls;
using SabakaLang.Studio.Editor.Highlighting;

namespace SabakaLang.Studio.Panels;

public enum SidePanel { Explorer, Git, Search, Settings }

public sealed class ActivityBar : ContentView
{
    public event Action<SidePanel>? PanelSelected;
    private SidePanel _active = SidePanel.Explorer;

    private readonly List<(SidePanel panel, Label icon)> _buttons = [];

    public ActivityBar()
    {
        WidthRequest    = 48;
        BackgroundColor = StudioTheme.Background;

        var stack = new VerticalStackLayout { Spacing = 4, Padding = new Thickness(0, 8) };

        Add(stack, "📁", SidePanel.Explorer, "Explorer");
        Add(stack, "⎇",  SidePanel.Git,      "Source Control");
        Add(stack, "🔍", SidePanel.Search,   "Search");
        Add(stack, "⚙",  SidePanel.Settings, "Settings");

        Content = stack;
        UpdateColors();
    }

    private void Add(VerticalStackLayout stack, string icon, SidePanel panel, string tooltip)
    {
        var lbl = new Label
        {
            Text = icon, FontSize = 20,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions   = LayoutOptions.Center,
            Padding   = new Thickness(0, 10),
            WidthRequest = 48,
            HeightRequest= 48
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            _active = panel;
            UpdateColors();
            PanelSelected?.Invoke(panel);
        };
        lbl.GestureRecognizers.Add(tap);
        _buttons.Add((panel, lbl));
        stack.Children.Add(lbl);
    }

    private void UpdateColors()
    {
        foreach (var (p, lbl) in _buttons)
            lbl.Opacity = p == _active ? 1.0 : 0.45;
    }
}

public sealed class SidePanelHost : ContentView
{
    private readonly Explorer.ProjectExplorer _explorer;
    private readonly Git.GitPanel             _git;
    private readonly Search.FindReplacePanel? _search;

    public SidePanelHost(
        Explorer.ProjectExplorer explorer,
        Git.GitPanel git)
    {
        _explorer = explorer;
        _git      = git;
        WidthRequest    = 240;
        BackgroundColor = StudioTheme.ExplorerBg;

        var grid = new Grid
        {
            Children = { explorer, git }
        };
        git.IsVisible = false;

        Content = grid;
    }

    public void ShowPanel(SidePanel panel)
    {
        _explorer.IsVisible = panel == SidePanel.Explorer;
        _git.IsVisible      = panel == SidePanel.Git;
    }
}