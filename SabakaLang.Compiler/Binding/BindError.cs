using SabakaLang.Compiler.Lexing;

namespace SabakaLang.Compiler.Binding;

public record BindError(string Message, Position Position)
{
    public override string ToString() => $"Bind error at {Position.Line}:{Position.Column}: {Message}";
}
