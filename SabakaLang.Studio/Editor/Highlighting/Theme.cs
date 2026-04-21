namespace SabakaLang.Studio.Editor.Highlighting;

public static class Theme
{
    public const float FontSize   = 14f;
    public const float LineHeight = 22f;
    public const float CharWidth  = 8.4f;
    public const float GutterW    = 48f;
    public const string Font      = "JMM";

    public static readonly Color Bg           = Color.FromArgb("#1E1F22");
    public static readonly Color LineHl       = Color.FromArgb("#26282E");
    public static readonly Color Selection    = Color.FromArgb("#214D87");
    public static readonly Color SearchHit    = Color.FromArgb("#5C4A1E");
    public static readonly Color SearchActive = Color.FromArgb("#A07830");
    public static readonly Color CaretColor   = Color.FromArgb("#ABABAB");
    public static readonly Color BracketHl    = Color.FromArgb("#3B5268");

    public static readonly Color GutterBg     = Color.FromArgb("#1A1B1E");
    public static readonly Color LineNum      = Color.FromArgb("#555860");
    public static readonly Color LineNumActive= Color.FromArgb("#A9B7C6");
    public static readonly Color GutterBorder = Color.FromArgb("#323438");

    public static readonly Color Plain        = Color.FromArgb("#A9B7C6");
    public static readonly Color Keyword      = Color.FromArgb("#CC7832");
    public static readonly Color TypeName     = Color.FromArgb("#FFC66D");
    public static readonly Color Function     = Color.FromArgb("#FFC66D");
    public static readonly Color ClassName    = Color.FromArgb("#6897BB");
    public static readonly Color StringColor  = Color.FromArgb("#6A8759");
    public static readonly Color Number       = Color.FromArgb("#6897BB");
    public static readonly Color Comment      = Color.FromArgb("#808080");
    public static readonly Color Operator     = Color.FromArgb("#A9B7C6");
    public static readonly Color Parameter    = Color.FromArgb("#94A3B8");
    public static readonly Color EnumMember   = Color.FromArgb("#9876AA");
    public static readonly Color Property     = Color.FromArgb("#9876AA");
    public static readonly Color Namespace    = Color.FromArgb("#A9B7C6");
    public static readonly Color Variable     = Color.FromArgb("#A9B7C6");

    public static readonly Color DiagError    = Color.FromArgb("#FF5555");
    public static readonly Color DiagWarn     = Color.FromArgb("#FFB86C");

    public static readonly Color PopupBg      = Color.FromArgb("#2B2D30");
    public static readonly Color PopupBorder  = Color.FromArgb("#4A4D52");
    public static readonly Color PopupSel     = Color.FromArgb("#113D6F");
    public static readonly Color PopupText    = Color.FromArgb("#A9B7C6");
    public static readonly Color PopupMuted   = Color.FromArgb("#686B72");

    public static readonly Color SigBg        = Color.FromArgb("#2B2D30");
    public static readonly Color SigText      = Color.FromArgb("#A9B7C6");
    public static readonly Color SigActive    = Color.FromArgb("#FFC66D");
}