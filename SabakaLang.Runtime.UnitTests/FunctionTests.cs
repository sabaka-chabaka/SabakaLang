using SabakaLang.Compiler;

namespace SabakaLang.Runtime.UnitTests;

public sealed class FunctionTests : Utilities
{
    [Fact]
    public void Function_VoidCallExecutesBody()
    {
        var src = """
            void greet() { print("hi"); }
            greet();
            """;
        Assert.Equal("hi", Output(src));
    }

    [Fact]
    public void Function_ReturnsValue()
    {
        var src = """
            int double(int x) { return x * 2; }
            print(double(21));
            """;
        Assert.Equal("42", Output(src));
    }

    [Fact]
    public void Function_MultipleParams()
    {
        var src = """
            int add(int a, int b) { return a + b; }
            print(add(3, 4));
            """;
        Assert.Equal("7", Output(src));
    }

    [Fact]
    public void Function_ReturnEarlyFromIf()
    {
        var src = """
            string sign(int n) {
                if (n > 0) { return "pos"; }
                if (n < 0) { return "neg"; }
                return "zero";
            }
            print(sign(-5));
            print(sign(0));
            print(sign(7));
            """;
        Assert.Equal(["neg", "zero", "pos"], Lines(src));
    }

    [Fact]
    public void Function_LocalVarDoesNotLeakToOuter()
    {
        RunError("""
            void foo() { int secret = 99; }
            foo();
            print(secret);
            """, "[3:7] Undefined symbol 'secret'.");
    }
    
    [Fact]
    public void Recursion_Factorial()
    {
        var src = """
            int fact(int n) {
                if (n <= 1) { return 1; }
                return n * fact(n - 1);
            }
            print(fact(6));
            """;
        Assert.Equal("720", Output(src));
    }

    [Fact]
    public void Recursion_Fibonacci()
    {
        var src = """
            int fib(int n) {
                if (n <= 1) { return n; }
                return fib(n - 1) + fib(n - 2);
            }
            print(fib(10));
            """;
        Assert.Equal("55", Output(src));
    }

    [Fact]
    public void Recursion_MutualRecursion()
    {
        var src = """
            bool isEven(int n) {
                if (n == 0) { return true; }
                return isOdd(n - 1);
            }
            bool isOdd(int n) {
                if (n == 0) { return false; }
                return isEven(n - 1);
            }
            print(isEven(4));
            print(isOdd(7));
            """;
        Assert.Equal(["true", "true"], Lines(src));
    }

    [Fact]
    public void Function_ArrayPassedByReference()
    {
        var src = """
            void fill(int[] arr) {
                arr[0] = 99;
            }
            int[] a = [0, 0, 0];
            fill(a);
            print(a[0]);
            """;
        Assert.Equal("99", Output(src));
    }
    
    [Fact]
    public void CallFunction_FromCSharp_ReturnsCorrectValue()
    {
        var src = """
            int square(int n) { return n * n; }
            """;

        var instructions = CompileToInstructions(src);
        var (vm, _) = BuildVm();
        vm.Execute(instructions);

        var result = vm.CallFunction("square", [Value.FromInt(7)]);
        Assert.Equal(SabakaType.Int, result.Type);
        Assert.Equal(49, result.Int);
    }

    [Fact]
    public void CallFunction_WithStringArg_Correct()
    {
        var src = """
            string greet(string name) { return "hello " + name; }
            """;

        var instructions = CompileToInstructions(src);
        var (vm, _) = BuildVm();
        vm.Execute(instructions);

        var result = vm.CallFunction("greet", [Value.FromString("world")]);
        Assert.Equal("hello world", result.String);
    }

    [Fact]
    public void CallFunction_Undefined_Throws()
    {
        var src = "int x = 1;";
        var instructions = CompileToInstructions(src);
        var (vm, _) = BuildVm();
        vm.Execute(instructions);

        Assert.Throws<RuntimeException>(() =>
            vm.CallFunction("nonExistent", []));
    }
    
    [Fact]
    public void ExternalFunction_CalledFromScript()
    {
        var called = false;
        var rt = new SabakaRuntime();
        rt.RegisterExternal("ping", 0, _ => { called = true; return Value.Null; });

        rt.RunAndCapture("ping();");
        Assert.True(called);
    }

    [Fact]
    public void ExternalFunction_ReceivesArgs()
    {
        Value[]? received = null;
        var rt = new SabakaRuntime();
        rt.RegisterExternal("capture", 2, args => { received = args; return Value.Null; });

        rt.RunAndCapture("capture(1, 2);");
        Assert.NotNull(received);
        Assert.Equal(2, received!.Length);
        Assert.Equal(1, received[0].Int);
        Assert.Equal(2, received[1].Int);
    }

    [Fact]
    public void ExternalFunction_ReturnsValueToScript()
    {
        var rt = new SabakaRuntime();
        rt.RegisterExternal("give42", 0, _ => Value.FromInt(42));

        var output = rt.RunAndCapture("print(give42());");
        Assert.Contains("42", output);
    }
}