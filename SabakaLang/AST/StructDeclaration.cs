namespace SabakaLang.AST;

public class StructDeclaration : Expr
{
    public string Name { get; }
    public List<string> Fields { get; }

    public StructDeclaration(string name, List<string> fields)
    {
        Name = name;
        Fields = fields;
    }
}
