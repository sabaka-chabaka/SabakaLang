namespace SabakaLang.AST;

public class NewExpr : Expr
{
    public string ClassName { get; }
    public List<string> TypeArgs { get; }
    public List<Expr> Arguments { get; }
    
    public string MangledName => TypeArgs.Count > 0
        ? $"{ClassName}${string.Join("$", TypeArgs)}"
        : ClassName;

    public NewExpr(string className, List<Expr> arguments, List<string>? typeArgs = null)
    {
        ClassName = className;
        TypeArgs = typeArgs ?? new List<string>();
        Arguments = arguments;
    }
}
