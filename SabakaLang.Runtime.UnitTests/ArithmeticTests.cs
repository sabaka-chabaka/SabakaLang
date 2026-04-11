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

public sealed class LogicTests : Utilities
{
    [Theory]
    [InlineData("1 == 1",  "true")]
    [InlineData("1 == 2",  "false")]
    [InlineData("1 != 2",  "true")]
    [InlineData("1 != 1",  "false")]
    [InlineData("2 > 1",   "true")]
    [InlineData("1 > 2",   "false")]
    [InlineData("1 < 2",   "true")]
    [InlineData("2 < 1",   "false")]
    [InlineData("2 >= 2",  "true")]
    [InlineData("2 >= 3",  "false")]
    [InlineData("2 <= 2",  "true")]
    [InlineData("3 <= 2",  "false")]
    public void Comparison_IsCorrect(string expr, string expected)
        => Assert.Equal(expected, Output($"print({expr});"));
    
    [Theory]
    [InlineData("true && true",   "true")]
    [InlineData("true && false",  "false")]
    [InlineData("false && true",  "false")]
    [InlineData("false || true",  "true")]
    [InlineData("false || false", "false")]
    [InlineData("!true",          "false")]
    [InlineData("!false",         "true")]
    public void BoolOp_IsCorrect(string expr, string expected)
        => Assert.Equal(expected, Output($"print({expr});"));
    
    [Fact]
    public void And_ShortCircuit_DoesNotEvalRight_WhenLeftFalse()
    {
        RunOk("bool b = false && (1/0 == 0);");
    }
    
    [Fact]
    public void Or_ShortCircuit_DoesNotEvalRight_WhenLeftTrue()
    {
        RunOk("bool b = true || (1/0 == 0);");
    }
    
    [Fact]
    public void StringEquality_SameContent_IsTrue()
        => Assert.Equal("true", Output("print(\"abc\" == \"abc\");"));
 
    [Fact]
    public void StringEquality_DiffContent_IsFalse()
        => Assert.Equal("false", Output("print(\"abc\" == \"xyz\");"));
    
    [Fact]
    public void NullEquality_NullEqualsNull()
        => Assert.Equal("true", Output("print(null == null);"));
}