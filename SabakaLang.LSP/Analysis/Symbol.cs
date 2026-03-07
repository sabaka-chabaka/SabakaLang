namespace SabakaLang.LSP.Analysis;

public class Symbol
{
    public string Name { get; }
    public SymbolKind Kind { get; }
    public string Type { get; }
    public int Start { get; }
    public int End { get; }
    public int ScopeStart { get; }
    public int ScopeEnd { get; }
    public string? ParentName { get; }   // class/struct name for members
    public string? SourceFile { get; }
    public string? Parameters { get; }
    public string? ModuleAlias { get; }  // non-null = belongs to "import X as alias"

    public Symbol(string name, SymbolKind kind, string type,
        int start, int end,
        int scopeStart = 0, int scopeEnd = int.MaxValue,
        string? parentName = null, string? sourceFile = null,
        string? parameters = null, string? moduleAlias = null)
    {
        Name = name;
        Kind = kind;
        Type = type;
        Start = start;
        End = end;
        ScopeStart = scopeStart;
        ScopeEnd = scopeEnd;
        ParentName = parentName;
        SourceFile = sourceFile;
        Parameters = parameters;
        ModuleAlias = moduleAlias;
    }
}