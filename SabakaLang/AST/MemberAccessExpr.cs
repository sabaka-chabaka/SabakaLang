namespace SabakaLang.AST;

public class MemberAccessExpr : Expr
{
    public Expr Object { get; }
    public string Member { get; }

    public MemberAccessExpr(Expr obj, string member)
    {
        Object = obj;
        Member = member;
    }
}
