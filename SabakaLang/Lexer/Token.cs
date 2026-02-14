namespace SabakaLang.Lexer;

public class Token
{
    public TokenType Type { get; }
    public string Value { get; }

    public Token(TokenType type, string value = "")
    {
        Type = type;
        Value = value;
    }

    public override string ToString()
    {
        return Type == TokenType.Number ? $"Number({Value})" : Type.ToString();
    }
}