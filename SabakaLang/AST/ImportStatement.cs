namespace SabakaLang.AST;

public class ImportStatement : Expr
{
    public string FilePath { get; }
    public ImportStatement(string filePath)
    {
        FilePath = filePath;
    }
}