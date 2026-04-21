using Microsoft.Maui.Graphics;

namespace SabakaLang.Studio.Editor.Highlighting;

public static class StudioTheme
{
    public static readonly Color Background       = Color.FromArgb("#1E1F22");
    public static readonly Color Surface          = Color.FromArgb("#2B2D30");
    public static readonly Color SurfaceActive    = Color.FromArgb("#313438");
    public static readonly Color Border           = Color.FromArgb("#393B40");
    public static readonly Color ScrollBar        = Color.FromArgb("#4A4D52");
 
    public static readonly Color GutterBackground = Color.FromArgb("#1E1F22");
    public static readonly Color GutterForeground = Color.FromArgb("#606366");
    public static readonly Color GutterActiveLine  = Color.FromArgb("#A9B7C6");
    public static readonly Color LineHighlight     = Color.FromArgb("#26282E");
    public static readonly Color CurrentLineNumber = Color.FromArgb("#A9B7C6");
 
    public static readonly Color Selection        = Color.FromArgb("#214283");
    public static readonly Color SearchHighlight  = Color.FromArgb("#5C4A1E");
    public static readonly Color SearchCurrent    = Color.FromArgb("#A07830");
    public static readonly Color BracketMatch     = Color.FromArgb("#3B4252");
 
    public static readonly Color Caret            = Color.FromArgb("#ABABAB");
 
    public static readonly Color Plain            = Color.FromArgb("#A9B7C6");
    public static readonly Color Keyword          = Color.FromArgb("#CC7832");
    public static readonly Color TypeName         = Color.FromArgb("#FFC66D");
    public static readonly Color ClassName        = Color.FromArgb("#6897BB");
    public static readonly Color StringLit        = Color.FromArgb("#6A8759");
    public static readonly Color Number           = Color.FromArgb("#6897BB");
    public static readonly Color Comment          = Color.FromArgb("#808080");
    public static readonly Color Operator         = Color.FromArgb("#A9B7C6");
    public static readonly Color Punctuation      = Color.FromArgb("#A9B7C6");
    public static readonly Color Variable         = Color.FromArgb("#A9B7C6");
    public static readonly Color Function         = Color.FromArgb("#FFC66D");
    public static readonly Color Parameter        = Color.FromArgb("#94A3B8");
    public static readonly Color EnumMember       = Color.FromArgb("#9876AA");
    public static readonly Color Property         = Color.FromArgb("#9876AA");
    public static readonly Color Namespace        = Color.FromArgb("#A9B7C6");
 
    public static readonly Color DiagError        = Color.FromArgb("#FF5555");
    public static readonly Color DiagWarning      = Color.FromArgb("#FFB86C");
    public static readonly Color DiagInfo         = Color.FromArgb("#8BE9FD");
 
    public static readonly Color ExplorerBg       = Color.FromArgb("#1E1F22");
    public static readonly Color ExplorerSelected = Color.FromArgb("#2D5A8E");
    public static readonly Color ExplorerHover    = Color.FromArgb("#2A2D31");
    public static readonly Color ExplorerIcon     = Color.FromArgb("#6897BB");
    public static readonly Color ExplorerText     = Color.FromArgb("#BABABA");
 
    public static readonly Color TabBackground    = Color.FromArgb("#1E1F22");
    public static readonly Color TabActive        = Color.FromArgb("#2B2D30");
    public static readonly Color TabInactive      = Color.FromArgb("#1E1F22");
    public static readonly Color TabText          = Color.FromArgb("#A9B7C6");
    public static readonly Color TabBorder        = Color.FromArgb("#393B40");
    public static readonly Color TabDirtyDot      = Color.FromArgb("#E2C08D");
 
    public static readonly Color StatusBarBg      = Color.FromArgb("#3C3F41");
    public static readonly Color StatusBarText    = Color.FromArgb("#BABABA");
    public static readonly Color StatusBarGit     = Color.FromArgb("#6A8759");
    public static readonly Color StatusBarError   = Color.FromArgb("#FF5555");
 
    public static readonly Color PopupBackground  = Color.FromArgb("#313438");
    public static readonly Color PopupBorder      = Color.FromArgb("#515457");
    public static readonly Color PopupSelected    = Color.FromArgb("#113D6F");
    public static readonly Color PopupText        = Color.FromArgb("#A9B7C6");
    public static readonly Color PopupDetail      = Color.FromArgb("#808080");
 
    public static readonly Color GitAdded         = Color.FromArgb("#6A8759");
    public static readonly Color GitModified      = Color.FromArgb("#6897BB");
    public static readonly Color GitDeleted       = Color.FromArgb("#FF5555");
    public static readonly Color GitConflict      = Color.FromArgb("#E2C08D");
    public static readonly Color GitUntracked     = Color.FromArgb("#A9B7C6");
 
    public const string FontFamily  = "JetMonoRegular";
    public const float  FontSize    = 13f;
    public const float  LineHeight  = 20f;
    public const float  GutterWidth = 52f;
    public const float  CharWidth   = 7.8f;
 
    public static Color ColorFor(SyntaxKind kind) => kind switch
    {
        SyntaxKind.Keyword    => Keyword,
        SyntaxKind.TypeName   => TypeName,
        SyntaxKind.ClassName  => ClassName,
        SyntaxKind.StringLit  => StringLit,
        SyntaxKind.Number     => Number,
        SyntaxKind.Comment    => Comment,
        SyntaxKind.Operator   => Operator,
        SyntaxKind.Punctuation=> Punctuation,
        SyntaxKind.Function   => Function,
        SyntaxKind.Parameter  => Parameter,
        SyntaxKind.EnumMember => EnumMember,
        SyntaxKind.Property   => Property,
        SyntaxKind.Namespace  => Namespace,
        SyntaxKind.Variable   => Variable,
        _                     => Plain
    };
}