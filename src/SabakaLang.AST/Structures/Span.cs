using SabakaLang.Lexer.Structures;

namespace SabakaLang.AST.Structures;

public readonly record struct Span(Position Start, Position End);