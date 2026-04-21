using SabakaLang.Studio.Editor.Highlighting;
using SabakaLang.Studio.Editor.Intellisense;

namespace SabakaLang.Studio.Editor.Gutter;

public sealed class CompletionPopupView : Border, IDisposable
{
    private readonly VerticalStackLayout _itemStack;
    private readonly ScrollView          _scroll;
    private CompletionState?             _state;
    private bool                         _disposed;

    public CompletionPopupView()
    {
        IsVisible       = false;
        Padding         = 0;
        StrokeShape     = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 4 };
        BackgroundColor = Theme.PopupBg;
        Stroke          = Theme.PopupBorder;
        WidthRequest    = 360;
        HeightRequest   = 220;
        ZIndex          = 1000;
        InputTransparent = false;

        _itemStack = new VerticalStackLayout { Spacing = 0 };
        _scroll    = new ScrollView
        {
            Content       = _itemStack,
            HeightRequest = 220
        };
        Content = _scroll;
    }

    public void Bind(CompletionState state)
    {
        _state         = state;
        state.Changed += Rebuild;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_state is not null) _state.Changed -= Rebuild;
    }

    private void Rebuild()
    {
        if (_state is null) return;
        IsVisible = _state.IsVisible;
        if (!_state.IsVisible) return;

        _itemStack.Children.Clear();
        var items = _state.Items;
        for (var i = 0; i < items.Count; i++)
        {
            var item    = items[i];
            var isSelected = i == _state.SelectedIndex;
            _itemStack.Children.Add(BuildRow(item, isSelected, i));
        }

        if (_state.SelectedIndex < items.Count)
        {
            var target = _itemStack.Children.ElementAtOrDefault(_state.SelectedIndex);
            if (target is View v)
                _ = _scroll.ScrollToAsync(v, ScrollToPosition.MakeVisible, false);
        }
    }

    private View BuildRow(CompletionItem item, bool selected, int index)
    {
        var icon = new Label
        {
            Text            = item.KindIcon,
            FontSize        = 11,
            WidthRequest    = 20,
            TextColor       = Theme.PopupMuted,
            VerticalOptions = LayoutOptions.Center
        };
        var label = new Label
        {
            Text            = item.Label,
            FontFamily      = Theme.Font,
            FontSize        = Theme.FontSize - 1,
            TextColor       = Theme.PopupText,
            VerticalOptions = LayoutOptions.Center
        };
        var detail = new Label
        {
            Text            = item.Detail,
            FontSize        = 11,
            TextColor       = Theme.PopupMuted,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End
        };

        var row = new Grid
        {
            Padding         = new Thickness(8, 3),
            BackgroundColor = selected ? Theme.PopupSel : Colors.Transparent,
            ColumnDefinitions =
            [
                new ColumnDefinition(new GridLength(22)),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            ],
            Children = { icon, label, detail }
        };
        Grid.SetColumn(label,  1);
        Grid.SetColumn(detail, 2);

        var tap = new TapGestureRecognizer();
        var capturedIndex = index;
        tap.Tapped += (_, _) =>
        {
            if (_state is null) return;
            while (_state.SelectedIndex < capturedIndex) _state.MoveDown();
            while (_state.SelectedIndex > capturedIndex) _state.MoveUp();
        };
        row.GestureRecognizers.Add(tap);
        return row;
    }
}

public sealed class SignatureView : Border, IDisposable
{
    private readonly Label  _label;
    private SignatureState? _state;
    private bool            _disposed;

    public SignatureView()
    {
        IsVisible       = false;
        Padding         = new Thickness(10, 5);
        StrokeShape     = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 4 };
        BackgroundColor = Theme.SigBg;
        Stroke          = Theme.PopupBorder;
        ZIndex          = 900;
        InputTransparent = true;

        _label = new Label
        {
            FontFamily = Theme.Font,
            FontSize   = Theme.FontSize - 1,
            TextColor  = Theme.SigText
        };
        Content = _label;
    }

    public void Bind(SignatureState state)
    {
        _state         = state;
        state.Changed += Rebuild;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_state is not null) _state.Changed -= Rebuild;
    }

    private void Rebuild()
    {
        if (_state is null) return;
        IsVisible = _state.IsVisible;
        if (!_state.IsVisible) return;

        var parts = _state.Params;
        var sb    = new System.Text.StringBuilder();
        sb.Append(_state.Label.Split('(')[0]);
        sb.Append('(');
        for (var i = 0; i < parts.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(i == _state.ActiveParam ? $"[{parts[i]}]" : parts[i]);
        }
        sb.Append(')');

        _label.Text = sb.ToString();
        var fs = new FormattedString();
        var raw = _state.Label.Split('(')[0] + "(";
        fs.Spans.Add(new Span { Text = raw, TextColor = Theme.SigText, FontFamily = Theme.Font, FontSize = Theme.FontSize - 1 });
        for (var i = 0; i < parts.Count; i++)
        {
            if (i > 0) fs.Spans.Add(new Span { Text = ", ", TextColor = Theme.SigText, FontFamily = Theme.Font, FontSize = Theme.FontSize - 1 });
            fs.Spans.Add(new Span
            {
                Text      = parts[i],
                TextColor = i == _state.ActiveParam ? Theme.SigActive : Theme.SigText,
                FontAttributes = i == _state.ActiveParam ? FontAttributes.Bold : FontAttributes.None,
                FontFamily = Theme.Font,
                FontSize   = Theme.FontSize - 1
            });
        }
        fs.Spans.Add(new Span { Text = ")", TextColor = Theme.SigText, FontFamily = Theme.Font, FontSize = Theme.FontSize - 1 });
        _label.FormattedText = fs;
    }
}

public sealed class HoverView : Border, IDisposable
{
    private readonly Label  _label;
    private HoverState?     _state;
    private bool            _disposed;

    public HoverView()
    {
        IsVisible        = false;
        Padding          = new Thickness(10, 6);
        StrokeShape      = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 4 };
        BackgroundColor  = Theme.SigBg;
        Stroke           = Theme.PopupBorder;
        ZIndex           = 800;
        InputTransparent = true;
        MaximumWidthRequest = 420;

        _label = new Label
        {
            FontFamily = Theme.Font,
            FontSize   = Theme.FontSize - 1,
            TextColor  = Theme.SigText,
            LineBreakMode = LineBreakMode.WordWrap
        };
        Content = _label;
    }

    public void Bind(HoverState state)
    {
        _state         = state;
        state.Changed += Rebuild;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_state is not null) _state.Changed -= Rebuild;
    }

    private void Rebuild()
    {
        if (_state is null) return;
        IsVisible = _state.IsVisible;
        if (!_state.IsVisible) return;
        _label.Text = _state.Markdown
            .Replace("```sabaka", "").Replace("```", "")
            .Trim();
    }
}