namespace SabakaLang.AST;

public class MemberAssignmentExpr : Expr
{
    public Expr Object { get; }
    public string Member { get; }
    public Expr Value { get; }

    public MemberAssignmentExpr(Expr obj, string member, Expr value)
    {
        Object = obj;
        Member = member;
        Value = value;
    }
}
