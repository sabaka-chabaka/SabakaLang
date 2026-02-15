namespace SabakaLang.AST;

public class ForStatement : Expr
{
    public Expr? Initializer { get; }
    public Expr? Condition { get; }
    public Expr? Increment { get; }
    public List<Expr> Body { get; }

    public ForStatement(
        Expr? initializer,
        Expr? condition,
        Expr? increment,
        List<Expr> body)
    {
        Initializer = initializer;
        Condition = condition;
        Increment = increment;
        Body = body;
    }
}
