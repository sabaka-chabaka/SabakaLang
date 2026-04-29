namespace SabakaLang.Runtime.UnitTests;

public class CharTests : Utilities
{
    [Fact]
    public void CharLiteral_Works()
        => Assert.Equal("a", Output("print('a');"));

    [Fact]
    public void CharVariable_Works()
        => Assert.Equal("b", Output("char c = 'b'; print(c);"));

    [Fact]
    public void CharEquality_IsTrue()
        => Assert.Equal("true", Output("print('x' == 'x');"));

    [Fact]
    public void CharEquality_IsFalse()
        => Assert.Equal("false", Output("print('x' == 'y');"));

    [Fact]
    public void CharNotEqual_IsTrue()
        => Assert.Equal("true", Output("print('x' != 'y');"));

    [Fact]
    public void CharDefaultValue_IsNullChar()
        => Assert.Equal("\0", Output("char c; print(c);"));

    [Fact]
    public void CharIsChar_IsTrue()
        => Assert.Equal("true", Output("print('a' is char);"));

    [Fact]
    public void CharIsInt_IsFalse()
        => Assert.Equal("false", Output("print('a' is int);"));

    [Fact]
    public void Ord_ReturnsInt()
        => Assert.Equal("97", Output("print(ord('a'));"));

    [Fact]
    public void Chr_ReturnsChar()
        => Assert.Equal("b", Output("print(chr(98));"));

    [Fact]
    public void StringConcat_WithChar()
        => Assert.Equal("a: b", Output("print(\"a: \" + 'b');"));
}
