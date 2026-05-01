using SabakaLang.Compiler.Lexing;

namespace SabakaLang.Compiler.Compiling;

public readonly record struct CompileError(string Message, Position Position)
{
    public override string ToString() => $"[{Position.Line}:{Position.Column}] {Message}";
}
