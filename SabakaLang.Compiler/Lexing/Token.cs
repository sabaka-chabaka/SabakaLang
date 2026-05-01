namespace SabakaLang.Compiler.Lexing;

public readonly record struct Token(TokenType Type, string Value, Position Start, Position End)
{
    public override string ToString() => Type is TokenType.IntLiteral or TokenType.FloatLiteral ? $"{Type}({Value}) at {Start.Line}:{Start.Column}" : $"{Type} at {Start.Line}:{Start.Column}";
}
