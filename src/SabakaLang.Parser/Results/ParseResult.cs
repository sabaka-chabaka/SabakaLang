using SabakaLang.AST.Interfaces;
using SabakaLang.Parser.Structures;

namespace SabakaLang.Parser.Results;

public sealed class ParseResult(IReadOnlyList<IStmt> statements, IReadOnlyList<ParseError> errors)
{
    public IReadOnlyList<IStmt> Statements { get; } = statements;
    public IReadOnlyList<ParseError> Errors { get; } = errors;
    public bool HasErrors => Errors.Count > 0;
}