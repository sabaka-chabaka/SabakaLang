namespace SabakaLang.AST;

public class NewExpr : Expr
{
    public string ClassName { get; }
    public List<Expr> Arguments { get; }

    public NewExpr(string className, List<Expr> arguments)
    {
        ClassName = className;
        Arguments = arguments;
    }
}
