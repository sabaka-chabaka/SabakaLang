namespace SabakaLang.AST;

public class ArrayAccessExpr : Expr
{
    public Expr Array { get; }
    public Expr Index { get; }

    public ArrayAccessExpr(Expr array, Expr index)
    {
        Array = array;
        Index = index;
    }
}
