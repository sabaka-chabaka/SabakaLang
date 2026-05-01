namespace SabakaLang.Compiler.Lexing;

public readonly record struct Position(int Line, int Column, int Offset);
