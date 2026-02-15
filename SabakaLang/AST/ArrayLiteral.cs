namespace SabakaLang.AST;

public class ArrayExpr : Expr
{
    public List<Expr> Elements { get; }

    public ArrayExpr(List<Expr> elements)
    {
        Elements = elements;
    }
}
