using Avalonia;
using Avalonia.Media;

namespace SabakaLang.Studio.Completion;

public static class CompletionIconProvider
{
    public static IImage GetIcon(CompletionIcon icon)
    {
        var geometry = icon switch
        {
            CompletionIcon.Class      => Geometry.Parse("M2,2 L14,2 L14,14 L2,14 Z"),
            CompletionIcon.Interface  => Geometry.Parse("M2,8 A6,6 0 1 1 14,8 A6,6 0 1 1 2,8"),
            CompletionIcon.Struct     => Geometry.Parse("M2,2 L14,2 L14,14 L2,14 Z M5,5 L11,5 L11,11 L5,11 Z"),
            CompletionIcon.Enum       => Geometry.Parse("M4,4 L12,4 L12,6 L4,6 Z M4,7 L12,7 L12,9 L4,9 Z M4,10 L12,10 L12,12 L4,12 Z"),
            CompletionIcon.EnumMember => Geometry.Parse("M6,6 L10,6 L10,10 L6,10 Z"),
            CompletionIcon.Function   => Geometry.Parse("M3,11 C5,3 11,3 13,11"),
            CompletionIcon.Method     => Geometry.Parse("M3,11 C5,3 11,3 13,11 M10,9 L13,11 L10,13"),
            CompletionIcon.Property   => Geometry.Parse("M3,3 L13,3 L13,13 L3,13 Z"),
            CompletionIcon.Variable   => Geometry.Parse("M4,12 L8,4 L12,12 Z"),
            CompletionIcon.Keyword    => Geometry.Parse("M3,3 L13,3 L13,13 L3,13 Z"),
            CompletionIcon.Namespace  => Geometry.Parse("M2,6 L6,2 L10,6 L14,2 L14,14 L2,14 Z"),
            CompletionIcon.TypeParam  => Geometry.Parse("M3,3 L13,3 L8,13 Z"),
            _ => Geometry.Parse("M2,2 L14,14 M14,2 L2,14")
        };

        var brush = icon switch
        {
            CompletionIcon.Class      => Brushes.CornflowerBlue,
            CompletionIcon.Interface  => Brushes.MediumPurple,
            CompletionIcon.Struct     => Brushes.SteelBlue,
            CompletionIcon.Enum       => Brushes.DarkCyan,
            CompletionIcon.EnumMember => Brushes.Teal,
            CompletionIcon.Function   => Brushes.Goldenrod,
            CompletionIcon.Method     => Brushes.Orange,
            CompletionIcon.Property   => Brushes.ForestGreen,
            CompletionIcon.Variable   => Brushes.LimeGreen,
            CompletionIcon.Keyword    => Brushes.Gray,
            CompletionIcon.Namespace  => Brushes.SlateBlue,
            CompletionIcon.TypeParam  => Brushes.DarkKhaki,
            _ => Brushes.White
        };

        var drawing = new GeometryDrawing
        {
            Geometry = geometry,
            Brush = brush,
            Pen = new Pen(Brushes.Black, 0.5)
        };

        return new DrawingImage(drawing);
    }
}