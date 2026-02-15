namespace SabakaLang.AST;

public class NewExpr : Expr
{
    public string ClassName { get; }

    public NewExpr(string className)
    {
        ClassName = className;
    }
}
