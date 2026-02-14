namespace SabakaLang.Exceptions;

public class ParserException : SabakaLangException
{
    public ParserException(string message, int position) : base(message, position)
    {
    }
}