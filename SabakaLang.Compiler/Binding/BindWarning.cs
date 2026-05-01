using SabakaLang.Compiler.Lexing;

namespace SabakaLang.Compiler.Binding;

public record BindWarning(string Message, Position Position)
{
    public override string ToString() => $"Bind warning at {Position.Line}:{Position.Column}: {Message}";
}
