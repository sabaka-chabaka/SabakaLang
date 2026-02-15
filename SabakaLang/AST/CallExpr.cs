namespace SabakaLang.AST;

public class CallExpr : Expr
{
    public Expr? Target { get; }
    public string Name { get; }
    public List<Expr> Arguments { get; }

    public CallExpr(string name, List<Expr> arguments, Expr? target = null)
    {
        Name = name;
        Arguments = arguments;
        Target = target;
    }
}
