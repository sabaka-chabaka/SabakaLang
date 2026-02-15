namespace SabakaLang.AST;

public class IntExpr : Expr
{
    public int Value { get; }

    public IntExpr(int value)
    {
        Value = value;
    }
}