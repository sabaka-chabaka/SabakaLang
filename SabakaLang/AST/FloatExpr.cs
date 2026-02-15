namespace SabakaLang.AST;

public class FloatExpr : Expr
{
    public double Value { get; }

    public FloatExpr(double value)
    {
        Value = value;
    }
}