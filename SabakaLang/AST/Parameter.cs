using SabakaLang.Lexer;

namespace SabakaLang.AST;

public class Parameter
{
    public TokenType Type { get; }
    public string? CustomType { get; }
    public string Name { get; }

    public Parameter(TokenType type, string name, string? customType = null)
    {
        Type = type;
        Name = name;
        CustomType = customType;
    }
}
