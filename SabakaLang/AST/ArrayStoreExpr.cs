namespace SabakaLang.AST;

public class ArrayStoreExpr : Expr
{
    public Expr Array { get; }
    public Expr Index { get; }
    public Expr Value { get; }

    public ArrayStoreExpr(Expr array, Expr index, Expr value)
    {
        Array = array;
        Index = index;
        Value = value;
    }
}
