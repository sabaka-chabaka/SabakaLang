namespace SabakaLang.LanguageServer.Services;

public class SemanticTokensLegend
{
    public static readonly string[] TokenTypes =
    [
        "namespace",    // 0
        "type",         // 1
        "class",        // 2
        "enum",         // 3
        "interface",    // 4
        "struct",       // 5
        "typeParameter",// 6
        "parameter",    // 7
        "variable",     // 8
        "property",     // 9
        "enumMember",   // 10
        "function",     // 11
        "method",       // 12
        "keyword",      // 13
        "comment",      // 14
        "string",       // 15
        "number",       // 16
        "operator"     // 17
    ];
    
    public static readonly string[] TokenModifiers =
    [
        "declaration",    // 0x01
        "definition",     // 0x02
        "readonly",       // 0x04
        "static",         // 0x08
        "deprecated",     // 0x10
        "modification",   // 0x20
        "defaultLibrary", // 0x40
    ];
    
    public const int Namespace     = 0;
    public const int Type          = 1;
    public const int Class         = 2;
    public const int Enum          = 3;
    public const int Interface     = 4;
    public const int Struct        = 5;
    public const int TypeParameter = 6;
    public const int Parameter     = 7;
    public const int Variable      = 8;
    public const int Property      = 9;
    public const int EnumMember    = 10;
    public const int Function      = 11;
    public const int Method        = 12;
    public const int Keyword       = 13;
    public const int Comment       = 14;
    public const int StringType    = 15;
    public const int Number        = 16;
    public const int Operator      = 17;

    public const int ModDeclaration  = 1 << 0;
    public const int ModDefaultLib   = 1 << 6;
}
