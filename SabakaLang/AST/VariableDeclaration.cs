using SabakaLang.Lexer;

namespace SabakaLang.AST;

public class VariableDeclaration : Expr
{
    public TokenType TypeToken { get; }   // IntKeyword / FloatKeyword / BoolKeyword
    public string Name { get; }
    public Expr Initializer { get; }

    public VariableDeclaration(TokenType typeToken, string name, Expr initializer)
    {
        TypeToken = typeToken;
        Name = name;
        Initializer = initializer;
    }
}
