namespace SabakaLang.Runtime.UnitTests;

public class StringInterpolationTests : Utilities
{
    [Fact]
    public void InterpolatedString_BasicVar_ProducesCorrectOutput()
        => Assert.Equal("name is kitty", Output("""
            string name = "kitty";
            print($"name is {name}");
            """));
 
    [Fact]
    public void InterpolatedString_NoHoles_PrintsLiteralText()
        => Assert.Equal("just text", Output("""print($"just text");"""));
 
    [Fact]
    public void InterpolatedString_OnlyHole_PrintsVarValue()
        => Assert.Equal("hello", Output("""
            string s = "hello";
            print($"{s}");
            """));
 
    [Fact]
    public void InterpolatedString_IntVar_ConvertsToString()
        => Assert.Equal("n is 42", Output("""
            int n = 42;
            print($"n is {n}");
            """));
 
    [Fact]
    public void InterpolatedString_FloatVar_ConvertsToString()
        => Assert.Equal("pi is 3.14", Output("""
            float pi = 3.14;
            print($"pi is {pi}");
            """));
 
    [Fact]
    public void InterpolatedString_BoolVar_ConvertsToString()
        => Assert.Equal("flag is true", Output("""
            bool flag = true;
            print($"flag is {flag}");
            """));
 
    [Fact]
    public void InterpolatedString_ArithmeticExpression_Evaluated()
        => Assert.Equal("result: 5", Output("""
            int x = 3;
            print($"result: {x + 2}");
            """));
 
    [Fact]
    public void InterpolatedString_MultipleHoles_AllSubstituted()
        => Assert.Equal("a=1, b=2", Output("""
            int a = 1;
            int b = 2;
            print($"a={a}, b={b}");
            """));
 
    [Fact]
    public void InterpolatedString_HoleAtStart_ProducesCorrectOutput()
        => Assert.Equal("kitty says meow", Output("""
            string name = "kitty";
            print($"{name} says meow");
            """));
 
    [Fact]
    public void InterpolatedString_HoleAtEnd_ProducesCorrectOutput()
        => Assert.Equal("hello kitty", Output("""
            string name = "kitty";
            print($"hello {name}");
            """));
 
    [Fact]
    public void InterpolatedString_AdjacentHoles_ProducesCorrectOutput()
        => Assert.Equal("ab", Output("""
            string x = "a";
            string y = "b";
            print($"{x}{y}");
            """));
 
    [Fact]
    public void InterpolatedString_NestedCall_EvaluatesCorrectly()
        => Assert.Equal("length: 3", Output("""
            string[] arr = ["a", "b", "c"];
            print($"length: {arr.length}");
            """));
 
    [Fact]
    public void InterpolatedString_AssignedToVar_WorksCorrectly()
        => Assert.Equal("name is kitty", Output("""
            string name = "kitty";
            string msg = $"name is {name}";
            print(msg);
            """));
 
    [Fact]
    public void InterpolatedString_InFunctionBody_WorksCorrectly()
        => Assert.Equal("hello kitty", Output("""
            void greet(string name) {
                print($"hello {name}");
            }
            greet("kitty");
            """));
 
    [Fact]
    public void InterpolatedString_ComplexExpression_Evaluated()
        => Assert.Equal("doubled: 10", Output("""
            int x = 5;
            print($"doubled: {x * 2}");
            """));
}