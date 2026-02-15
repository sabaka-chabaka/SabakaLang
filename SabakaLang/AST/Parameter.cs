using SabakaLang.Lexer;

namespace SabakaLang.AST;

public class Parameter
{
    public TokenType Type { get; }
    public string Name { get; }

    public Parameter(TokenType type, string name)
    {
        Type = type;
        Name = name;
    }
}
