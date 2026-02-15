using SabakaLang.Lexer;

namespace SabakaLang.AST;

public class VariableDeclaration : Expr
{
    public TokenType TypeToken { get; }   // IntKeyword / FloatKeyword / BoolKeyword / Identifier (for struct)
    public string? CustomType { get; }    // Name of the struct if TypeToken is Identifier
    public string Name { get; }
    public Expr? Initializer { get; }

    public VariableDeclaration(TokenType typeToken, string? customType, string name, Expr? initializer)
    {
        TypeToken = typeToken;
        CustomType = customType;
        Name = name;
        Initializer = initializer;
    }
}
