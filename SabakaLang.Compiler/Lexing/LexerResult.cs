namespace SabakaLang.Compiler.Lexing;

public sealed class LexerResult(IReadOnlyList<Token> tokens, IReadOnlyList<LexerError> errors)
{
    public IReadOnlyList<Token> Tokens { get; } = tokens;
    public IReadOnlyList<LexerError> Errors { get; } = errors;
    public bool HasErrors => Errors.Count > 0;
}
