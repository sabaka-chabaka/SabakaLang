namespace SabakaLang.AST;

public class WhileExpr : Expr
{
    public Expr Condition { get; }
    public List<Expr> Body { get; }

    public WhileExpr(Expr condition, List<Expr> body)
    {
        Condition = condition;
        Body = body;
    }
}