namespace SabakaLang.Exceptions;

public class LexerException : SabakaLangException
{
    public LexerException(string message, int position) : base(message, position)
    {
    }
}