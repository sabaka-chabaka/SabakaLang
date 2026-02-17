using SabakaLang.Lexer;

namespace SabakaLang.AST;

public class FunctionDeclaration : Expr
{
    public TokenType ReturnType { get; }
    public string Name { get; }
    public List<Parameter> Parameters { get; }
    public List<Expr> Body { get; }
    public bool IsOverride { get; }
    public AccessModifier AccessModifier { get; }

    public FunctionDeclaration(
        TokenType returnType,
        string name,
        List<Parameter> parameters,
        List<Expr> body,
        bool isOverride = false,
        AccessModifier accessModifier = AccessModifier.Public)
    {
        ReturnType = returnType;
        Name = name;
        Parameters = parameters;
        Body = body;
        IsOverride = isOverride;
        AccessModifier = accessModifier;
    }
}
