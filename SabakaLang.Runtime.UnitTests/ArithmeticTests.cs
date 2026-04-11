namespace SabakaLang.Runtime.UnitTests;

public class ArithmeticTests : Utilities
{
    [Fact]
    public void IntAdd_ReturnsSum()
        => Assert.Equal("7", Output("print(3 + 4);"));
 
    [Fact]
    public void IntSub_ReturnsDiff()
        => Assert.Equal("1", Output("print(5 - 4);"));
 
    [Fact]
    public void IntMul_ReturnsProduct()
        => Assert.Equal("12", Output("print(3 * 4);"));
 
    [Fact]
    public void IntDiv_ReturnsQuotient()
        => Assert.Equal("3", Output("print(9 / 3);"));
 
    [Fact]
    public void IntMod_ReturnsRemainder()
        => Assert.Equal("1", Output("print(10 % 3);"));
 
    [Fact]
    public void IntAdd_NegativeNumbers()
        => Assert.Equal("-3", Output("print(-5 + 2);"));
 
    [Fact]
    public void IntMul_ByZero_IsZero()
        => Assert.Equal("0", Output("print(42 * 0);"));
 
    [Fact]
    public void IntDiv_IntegerTruncates()
        => Assert.Equal("3", Output("print(7 / 2);"));
    
    [Fact]
    public void FloatAdd_ReturnsSum()
        => Assert.Equal("1.5", Output("print(0.5 + 1.0);"));
 
    [Fact]
    public void FloatMul_ReturnsProduct()
        => Assert.Equal("6", Output("print(2.0 * 3.0);"));
 
    [Fact]
    public void MixedIntFloat_ProducesFloat()
        => Assert.Equal("2.5", Output("print(5 / 2.0);"));
    
    [Fact]
    public void Negate_Int_NegatesValue()
        => Assert.Equal("-42", Output("int x = 42; print(-x);"));
 
    [Fact]
    public void Negate_Float_NegatesValue()
        => Assert.Equal("-3.14", Output("float x = 3.14; print(-x);"));

    [Fact]
    public void Mod_ZeroDivisor_Throws()
        => RunError("int x = 5 % 0;", "zero");
 
    [Fact]
    public void Mod_NegativeNumerator()
        => Assert.Equal("-1", Output("print(-7 % 3);"));
    
    [Fact]
    public void IntDiv_ByZero_Throws()
        => RunError("int x = 1 / 0;", "zero");
    
    [Fact]
    public void OperatorPrecedence_MulBeforeAdd()
        => Assert.Equal("7", Output("print(1 + 2 * 3);"));
 
    [Fact]
    public void OperatorPrecedence_Parens_Override()
        => Assert.Equal("9", Output("print((1 + 2) * 3);"));
    
    [Fact]
    public void StringConcat_TwoStrings()
        => Assert.Equal("hello world", Output("print(\"hello\" + \" \" + \"world\");"));
 
    [Fact]
    public void StringConcat_IntToString()
        => Assert.Equal("x=42", Output("print(\"x=\" + 42);"));
 
    [Fact]
    public void StringConcat_FloatToString()
        => Assert.Equal("pi=3.14", Output("print(\"pi=\" + 3.14);"));
}