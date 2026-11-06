namespace SabakaLang.Lexer.Structures;

public readonly record struct LexerError(string Message, Position Position);