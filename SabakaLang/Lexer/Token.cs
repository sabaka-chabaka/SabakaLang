namespace SabakaLang.Lexer;

public class Token
{
    public TokenType Type { get; }
    public string Value { get; }
    public int TokenStart { get; }
    public int TokenEnd { get; }

    public Token(TokenType type, string value = "", int tokenStart = 0, int tokenEnd = 0)
    {
        Type = type;
        Value = value;
        TokenStart = tokenStart;
        TokenEnd = tokenEnd;
    }

    public override string ToString()
    {
        return Type == TokenType.Number ? $"Number({Value})" : Type.ToString();
    }
}