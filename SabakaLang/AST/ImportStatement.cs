namespace SabakaLang.AST;

public class ImportStatement : Expr
{
    public string FilePath { get; }
    public List<string> ImportNames { get; } // Empty means import all, otherwise import specific names
    
    public ImportStatement(string filePath, List<string>? importNames = null)
    {
        FilePath = filePath;
        ImportNames = importNames ?? new List<string>();
    }
}