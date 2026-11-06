using SabakaLang.Lexer.Results;

namespace SabakaLang.Lexer.Tests;

public class Utils
{
    public static LexerResult Tokenize(string source)
    {
        var lexer = new Lexer();
        return lexer.Tokenize(source);
    }
}