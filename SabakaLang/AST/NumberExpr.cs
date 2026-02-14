namespace SabakaLang.AST;

public class NumberExpr : Expr
{
    public double Value { get; }

    public NumberExpr(double value)
    {
        Value = value;
    }
}