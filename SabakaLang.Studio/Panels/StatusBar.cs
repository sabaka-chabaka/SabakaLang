using SabakaLang.Studio.Editor.Highlighting;
using SabakaLang.Studio.ViewModels;

namespace SabakaLang.Studio.Panels;

public sealed class StatusBar : ContentView
{
    public StatusBar(MainViewModel vm)
    {
        HeightRequest   = 24;
        BackgroundColor = StudioTheme.StatusBarBg;

        var branch = new Label
        {
            FontSize = 11, FontFamily = StudioTheme.FontFamily,
            TextColor = StudioTheme.StatusBarGit,
            VerticalOptions = LayoutOptions.Center,
            Padding = new Thickness(10, 0)
        };
        branch.SetBinding(Label.TextProperty, new Binding(nameof(MainViewModel.StatusBranch), source: vm));

        var errors = new HorizontalStackLayout
        {
            Spacing = 3, VerticalOptions = LayoutOptions.Center,
            Padding = new Thickness(8, 0)
        };
        var errorIcon  = new Label { Text = "✕", FontSize = 11, TextColor = StudioTheme.DiagError, VerticalOptions = LayoutOptions.Center };
        var errorCount = new Label { FontSize = 11, TextColor = StudioTheme.StatusBarText, VerticalOptions = LayoutOptions.Center };
        errorCount.SetBinding(Label.TextProperty, new Binding(nameof(MainViewModel.ErrorCount), source: vm));
        var warnIcon   = new Label { Text = "⚠", FontSize = 11, TextColor = StudioTheme.DiagWarning, VerticalOptions = LayoutOptions.Center, Padding = new Thickness(8, 0, 0, 0) };
        var warnCount  = new Label { FontSize = 11, TextColor = StudioTheme.StatusBarText, VerticalOptions = LayoutOptions.Center };
        warnCount.SetBinding(Label.TextProperty, new Binding(nameof(MainViewModel.WarnCount), source: vm));
        errors.Children.Add(errorIcon);
        errors.Children.Add(errorCount);
        errors.Children.Add(warnIcon);
        errors.Children.Add(warnCount);

        var position = new Label
        {
            FontSize = 11, FontFamily = StudioTheme.FontFamily,
            TextColor = StudioTheme.StatusBarText,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End,
            Padding = new Thickness(10, 0)
        };
        position.SetBinding(Label.TextProperty, new Binding(nameof(MainViewModel.StatusLine), source: vm));

        var lang = new Label
        {
            Text = "SabakaLang",
            FontSize = 11,
            TextColor = StudioTheme.StatusBarText,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End,
            Padding = new Thickness(10, 0)
        };

        var layout = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
            ],
            VerticalOptions = LayoutOptions.Fill,
            Children        = { branch, errors, lang, position }
        };
        Grid.SetColumn(errors,   1);
        Grid.SetColumn(lang,     3);
        Grid.SetColumn(position, 4);

        Content = layout;
    }
}