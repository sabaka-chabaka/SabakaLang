namespace SabakaLang.AST;

public class VariableDeclaration : Expr
{
    public string Name { get; }
    public Expr Value { get; }

    public VariableDeclaration(string name, Expr value)
    {
        Name = name;
        Value = value;
    }
}