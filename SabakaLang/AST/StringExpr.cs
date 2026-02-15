namespace SabakaLang.AST;

public class StringExpr : Expr
{
    public string Value { get; }

    public StringExpr(string value)
    {
        Value = value;
    }
}
