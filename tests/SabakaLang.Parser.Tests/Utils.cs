using SabakaLang.AST;
using SabakaLang.AST.Interfaces;
using SabakaLang.Parser.Results;

namespace SabakaLang.Parser.Tests;

public static class Utils
{
    public static ParseResult Parse(string source)
    {
        var lexer = new Lexer.Lexer().Tokenize(source);
        var parser = new Parser(lexer);
        return parser.Parse();
    }

    public static T ParseSingleStmt<T>(string source) where T : class, IStmt
    {
        var result = Parse(source);
        Assert.False(result.HasErrors, $"Parse errors: {string.Join(", ", result.Errors.Select(e => e.Message))}");
        Assert.Single(result.Statements);
        var stmt = Assert.IsAssignableFrom<T>(result.Statements[0]);
        return stmt;
    }

    public static T ParseSingleExpr<T>(string source) where T : class, IExpr
    {
        var exprStmt = ParseSingleStmt<ExprStmt>(source.EndsWith(';') ? source : source + ";");
        return Assert.IsAssignableFrom<T>(exprStmt.Expr);
    }
}
