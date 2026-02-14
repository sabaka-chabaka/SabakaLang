namespace SabakaLang.AST;

public class AssignmentExpr : Expr
{
    public string Name { get; }
    public Expr Value { get; }

    public AssignmentExpr(string name, Expr value)
    {
        Name = name;
        Value = value;
    }
}