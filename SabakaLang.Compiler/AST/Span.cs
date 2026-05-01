using SabakaLang.Compiler.Lexing;

namespace SabakaLang.Compiler.AST;

public readonly record struct Span(Position Start, Position End);
