namespace SabakaLang.AST;

public class SwitchCase
{
    public Expr? Value { get; } // null for default
    public List<Expr> Body { get; }

    public SwitchCase(Expr? value, List<Expr> body)
    {
        Value = value;
        Body = body;
    }
}

public class SwitchStatement : Expr
{
    public Expr Expression { get; }
    public List<SwitchCase> Cases { get; }

    public SwitchStatement(Expr expression, List<SwitchCase> cases)
    {
        Expression = expression;
        Cases = cases;
    }
}
