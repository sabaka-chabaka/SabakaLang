using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using SabakaLang.LanguageServer;

namespace SabakaLang.Studio.Diagnostics;

public sealed class DiagnosticsPanel
{
    public Control Root { get; }

    private readonly TextEditor _editor;
    private readonly StackPanel _listPanel;
    private readonly TextBlock  _header;
    private readonly Border     _listBorder;
    private bool                _collapsed;

    private static readonly IBrush BgPanel    = new SolidColorBrush(Color.Parse("#1A1A1A"));
    private static readonly IBrush BgHeader   = new SolidColorBrush(Color.Parse("#252526"));
    private static readonly IBrush BgRow      = new SolidColorBrush(Color.Parse("#1E1E1E"));
    private static readonly IBrush BgRowHover = new SolidColorBrush(Color.Parse("#2A2D2E"));
    private static readonly IBrush FgDefault  = new SolidColorBrush(Color.Parse("#D4D4D4"));
    private static readonly IBrush FgMuted    = new SolidColorBrush(Color.Parse("#858585"));

    private static readonly IBrush ColorError   = new SolidColorBrush(Color.Parse("#F44747"));
    private static readonly IBrush ColorWarning = new SolidColorBrush(Color.Parse("#CCA700"));
    private static readonly IBrush ColorInfo    = new SolidColorBrush(Color.Parse("#75BEFF"));
    private static readonly IBrush ColorHint    = new SolidColorBrush(Color.Parse("#6A9955"));

    private static readonly FontFamily MonoFont =
        new("JbFont,monospace");


    public DiagnosticsPanel(TextEditor editor)
    {
        _editor = editor;

        _header = new TextBlock
        {
            Text = "PROBLEMS  (0)",
            Foreground = FgDefault,
            FontSize = 11,
            FontFamily = MonoFont,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0)
        };

        var collapseBtn = new TextBlock
        {
            Text = "▼",
            Foreground = FgMuted,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };

        var headerBar = new Border
        {
            Background = BgHeader,
            Height = 26,
            Child = new DockPanel
            {
                Children =
                {
                    new DockPanel { Children = { collapseBtn } }.Also(d => DockPanel.SetDock(d, Dock.Right)),
                    _header
                }
            },
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };

        headerBar.PointerPressed += (_, _) => ToggleCollapse(collapseBtn);

        _listPanel = new StackPanel { Spacing = 0 };

        _listBorder = new Border
        {
            Background = BgPanel,
            MaxHeight = 160,
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility   = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = _listPanel
            }
        };

        Root = new StackPanel
        {
            Spacing = 0,
            Children = { headerBar, _listBorder }
        };
    }

    public void Update(IReadOnlyList<Diagnostic> diagnostics)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _listPanel.Children.Clear();

            int errors   = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
            int warnings = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);

            var parts = new List<string>();
            if (errors   > 0) parts.Add($"⛔ {errors}");
            if (warnings > 0) parts.Add($"⚠ {warnings}");
            int rest = diagnostics.Count - errors - warnings;
            if (rest > 0) parts.Add($"ℹ {rest}");

            _header.Text = parts.Count > 0
                ? $"PROBLEMS  {string.Join("  ", parts)}"
                : "PROBLEMS  ✓";

            if (diagnostics.Count == 0)
            {
                _listPanel.Children.Add(new TextBlock
                {
                    Text = "  No problems detected.",
                    Foreground = FgMuted,
                    FontSize = 12,
                    FontFamily = MonoFont,
                    Margin = new Thickness(8, 4)
                });
                return;
            }

            foreach (var diag in diagnostics.OrderBy(d => d.Start.Line).ThenBy(d => d.Start.Column))
            {
                var row = BuildRow(diag);
                _listPanel.Children.Add(row);
            }
        });
    }

    private Border BuildRow(Diagnostic diag)
    {
        var (icon, brush) = diag.Severity switch
        {
            DiagnosticSeverity.Error       => ("⛔", ColorError),
            DiagnosticSeverity.Warning     => ("⚠", ColorWarning),
            DiagnosticSeverity.Information => ("ℹ", ColorInfo),
            _                              => ("💡", ColorHint)
        };

        var iconBlock = new TextBlock
        {
            Text = icon,
            Foreground = brush,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 6, 0),
            MinWidth = 18
        };

        var msgBlock = new TextBlock
        {
            Text = diag.Message,
            Foreground = FgDefault,
            FontSize = 12,
            FontFamily = MonoFont,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var locBlock = new TextBlock
        {
            Text = $"  [{diag.Start.Line}:{diag.Start.Column}]",
            Foreground = FgMuted,
            FontSize = 11,
            FontFamily = MonoFont,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };

        var row = new Border
        {
            Background = BgRow,
            Padding = new Thickness(0, 3),
            BorderBrush = new SolidColorBrush(Color.Parse("#2D2D2D")),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Child = new DockPanel
            {
                Children =
                {
                    new DockPanel { Children = { locBlock } }.Also(d => DockPanel.SetDock(d, Dock.Right)),
                    iconBlock,
                    msgBlock
                }
            }
        };

        row.PointerEntered += (_, _) => row.Background = BgRowHover;
        row.PointerExited  += (_, _) => row.Background = BgRow;

        var captured = diag;
        row.PointerPressed += (_, _) => NavigateTo(captured);

        return row;
    }

    private void NavigateTo(Diagnostic diag)
    {
        var doc = _editor.Document;
        if (doc is null) return;

        int line = Math.Clamp(diag.Start.Line, 1, doc.LineCount);
        var docLine = doc.GetLineByNumber(line);
        int col     = Math.Max(0, diag.Start.Column - 1);
        int offset  = Math.Min(docLine.Offset + col, docLine.EndOffset);

        _editor.Focus();
        _editor.CaretOffset = offset;
        _editor.ScrollTo(line, diag.Start.Column);
    }

    private void ToggleCollapse(TextBlock arrow)
    {
        _collapsed = !_collapsed;
        _listBorder.IsVisible = !_collapsed;
        arrow.Text = _collapsed ? "▶" : "▼";
    }
}

internal static class ControlExtensions
{
    public static T Also<T>(this T self, Action<T> action) { action(self); return self; }
}