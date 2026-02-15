namespace SabakaLang.AST;

public class CallExpr : Expr
{
    public string Name { get; }
    public Expr? Argument { get; }

    public CallExpr(string name, Expr argument)
    {
        Name = name;
        Argument = argument;
    }
}