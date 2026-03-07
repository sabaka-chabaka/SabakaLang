namespace SabakaLang.AST;

public class ImportStatement : Expr
{
    public string FilePath { get; }
    public List<string> ImportNames { get; } // Empty means import all
    public string? Alias { get; }            // null = global (no 'as'), otherwise namespaced

    public ImportStatement(string filePath, List<string>? importNames = null, string? alias = null)
    {
        FilePath = filePath;
        ImportNames = importNames ?? new List<string>();
        Alias = alias;
    }
}