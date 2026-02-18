namespace SabakaLang.Exceptions;

public class SemanticException : SabakaLangException
{
    public SemanticException(string message, int position) : base(message, position)
    {
    }
}
