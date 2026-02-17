namespace SabakaLang.Exceptions;

public class CompilerException : SabakaLangException
{
    public CompilerException(string message, int position) : base(message, position)
    {
    }
}