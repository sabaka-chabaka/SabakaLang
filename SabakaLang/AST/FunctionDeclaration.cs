using SabakaLang.Lexer;

namespace SabakaLang.AST;

public class FunctionDeclaration : Expr
{
    public TokenType ReturnType { get; }
    public string Name { get; }
    public List<Parameter> Parameters { get; }
    public List<Expr> Body { get; }

    public FunctionDeclaration(
        TokenType returnType,
        string name,
        List<Parameter> parameters,
        List<Expr> body)
    {
        ReturnType = returnType;
        Name = name;
        Parameters = parameters;
        Body = body;
    }
}
