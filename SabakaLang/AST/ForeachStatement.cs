namespace SabakaLang.AST;

public class ForeachStatement : Expr
{
    public string VarName { get; }
    public Expr Collection { get; }
    public List<Expr> Body { get; }

    public ForeachStatement(string varName, Expr collection, List<Expr> body)
    {
        VarName = varName;
        Collection = collection;
        Body = body;
    }
}
