namespace SabakaLang.AST;

public class VariableExpr : Expr
{
    public string Name { get; }

    public VariableExpr(string name)
    {
        Name = name;
    }
}