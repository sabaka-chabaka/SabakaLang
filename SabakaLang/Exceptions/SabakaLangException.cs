namespace SabakaLang.Exceptions;

public abstract class SabakaLangException : Exception
{
    public int Position { get; }

    protected SabakaLangException(string message, int position) : base(message)
    {
        Position = position;
    }

    public override string Message => $"{base.Message} at position {Position}";
}