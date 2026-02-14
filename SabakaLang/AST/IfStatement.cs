namespace SabakaLang.AST;

public class IfStatement : Expr
{
    public Expr Condition { get; }
    public List<Expr> ThenBlock { get; }
    public List<Expr>? ElseBlock { get; }

    public IfStatement(
        Expr condition,
        List<Expr> thenBlock,
        List<Expr>? elseBlock)
    {
        Condition = condition;
        ThenBlock = thenBlock;
        ElseBlock = elseBlock;
    }
}