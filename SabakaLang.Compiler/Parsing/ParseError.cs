using SabakaLang.Compiler.Lexing;

namespace SabakaLang.Compiler.Parsing;

public record ParseError(string Message, Position Position);
