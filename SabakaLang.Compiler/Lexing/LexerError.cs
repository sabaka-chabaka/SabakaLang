namespace SabakaLang.Compiler.Lexing;

public readonly record struct LexerError(string Message, Position Position);
