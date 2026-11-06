using SabakaLang.Lexer.Structures;

namespace SabakaLang.Parser.Structures;

public record ParseError(string Message, Position Position);