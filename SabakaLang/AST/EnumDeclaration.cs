namespace SabakaLang.AST;

public class EnumDeclaration : Expr
{
    public string Name { get; }
    public List<string> Members { get; }

    public EnumDeclaration(string name, List<string> members)
    {
        Name = name;
        Members = members;
    }
}
