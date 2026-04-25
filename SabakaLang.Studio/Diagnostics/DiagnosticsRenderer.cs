using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using SabakaLang.LanguageServer;

namespace SabakaLang.Studio.Diagnostics;

public sealed class DiagnosticsRenderer : IBackgroundRenderer
{
    private IReadOnlyList<Diagnostic> _diagnostics = [];
    private readonly TextView _textView;

    private static readonly IBrush BrushError   = new SolidColorBrush(Color.Parse("#F44747")); // red
    private static readonly IBrush BrushWarning = new SolidColorBrush(Color.Parse("#CCA700")); // yellow
    private static readonly IBrush BrushInfo    = new SolidColorBrush(Color.Parse("#75BEFF")); // blue
    private static readonly IBrush BrushHint    = new SolidColorBrush(Color.Parse("#6A9955")); // green

    public KnownLayer Layer => KnownLayer.Selection;

    public DiagnosticsRenderer(TextView textView)
    {
        _textView = textView;
    }

    public void Update(IReadOnlyList<Diagnostic> diagnostics)
    {
        _diagnostics = diagnostics;
        _textView.InvalidateLayer(Layer);
    }

    public void Draw(TextView textView, DrawingContext ctx)
    {
        if (_diagnostics.Count == 0) return;

        var doc = textView.Document;
        if (doc is null) return;

        try
        {
        foreach (var diag in _diagnostics)
        {
            int startOffset = ToOffset(doc, diag.Start.Line, diag.Start.Column);
            if (startOffset < 0) continue;

            int endOffset;
            if (diag.End.Offset > diag.Start.Offset)
                endOffset = ToOffset(doc, diag.End.Line, diag.End.Column) + 1;
            else
                endOffset = FindWordEnd(doc, startOffset);

            endOffset = Math.Min(endOffset, doc.TextLength);
            if (endOffset <= startOffset) endOffset = Math.Min(startOffset + 1, doc.TextLength);

            var brush = diag.Severity switch
            {
                DiagnosticSeverity.Error       => BrushError,
                DiagnosticSeverity.Warning     => BrushWarning,
                DiagnosticSeverity.Information => BrushInfo,
                _                              => BrushHint
            };

            DrawSquiggles(textView, ctx, startOffset, endOffset, brush);
        }
        } // try
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DiagnosticsRenderer] {ex.Message}");
        }
    }

    private static void DrawSquiggles(
        TextView textView, DrawingContext ctx,
        int startOffset, int endOffset, IBrush brush)
    {
        var pen = new Pen(brush, 1.5);

        foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(
                     textView, new SimpleSegment(startOffset, endOffset - startOffset)))
        {
            double y    = rect.Bottom - 1;
            double x    = rect.X;
            double xEnd = rect.Right;
            
            const double period    = 4.0;
            const double amplitude = 1.5;

            var geo    = new StreamGeometry();
            using var gc = geo.Open();

            bool first = true;
            for (double xi = x; xi <= xEnd; xi += 0.5)
            {
                double yi = y + Math.Sin((xi - x) / period * Math.PI * 2) * amplitude;
                if (first) { gc.BeginFigure(new Point(xi, yi), false); first = false; }
                else         gc.LineTo(new Point(xi, yi));
            }
            gc.EndFigure(false);

            ctx.DrawGeometry(null, pen, geo);
        }
    }
    
    private static int ToOffset(TextDocument doc, int line, int col)
    {
        if (line < 1 || line > doc.LineCount) return -1;
        var docLine = doc.GetLineByNumber(line);
        int colOffset = Math.Max(0, col - 1);
        return Math.Min(docLine.Offset + colOffset, docLine.EndOffset);
    }

    private static int FindWordEnd(TextDocument doc, int offset)
    {
        int end = offset;
        while (end < doc.TextLength)
        {
            char c = doc.GetCharAt(end);
            if (!char.IsLetterOrDigit(c) && c != '_') break;
            end++;
        }
        return end > offset ? end : offset + 1;
    }
}