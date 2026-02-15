namespace SabakaLang.AST;

public class CallExpr : Expr
{
    public string Name { get; }
    public List<Expr> Arguments { get; }

    public CallExpr(string name, List<Expr> arguments)
    {
        Name = name;
        Arguments = arguments;
    }
}
