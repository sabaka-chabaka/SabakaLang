namespace SabakaLang.AST;


public class BoolExpr : Expr
{
    public bool Value { get; }

    public BoolExpr(bool value)
    {
        Value = value;
    }
}