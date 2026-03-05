using SabakaLang.Lexer;

namespace SabakaLang.AST;

public class FunctionDeclaration : Expr
{
    public TokenType ReturnType { get; }
    public string Name { get; }
    public List<string> TypeParams { get; }   // e.g. ["T"] for generic methods
    public List<Parameter> Parameters { get; }
    public List<Expr> Body { get; }
    public bool IsOverride { get; }
    public AccessModifier AccessModifier { get; }

    public bool IsGeneric => TypeParams.Count > 0;

    public FunctionDeclaration(
        TokenType returnType,
        string name,
        List<Parameter> parameters,
        List<Expr> body,
        bool isOverride = false,
        AccessModifier accessModifier = AccessModifier.Public,
        List<string>? typeParams = null)
    {
        ReturnType = returnType;
        Name = name;
        TypeParams = typeParams ?? new List<string>();
        Parameters = parameters;
        Body = body;
        IsOverride = isOverride;
        AccessModifier = accessModifier;
    }
}

