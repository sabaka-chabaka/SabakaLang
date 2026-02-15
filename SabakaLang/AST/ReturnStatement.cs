namespace SabakaLang.AST;

public class ReturnStatement : Expr
{
    public Expr? Value { get; }

    public ReturnStatement(Expr? value)
    {
        Value = value;
    }
}
