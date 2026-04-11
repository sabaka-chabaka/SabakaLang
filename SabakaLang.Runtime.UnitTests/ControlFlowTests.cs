namespace SabakaLang.Runtime.UnitTests;

public sealed class VariableTests : Utilities
{
    [Fact]
    public void DeclareInt_DefaultZero()
        => Assert.Equal("0", Output("int x; print(x);"));

    [Fact]
    public void DeclareInt_WithInit()
        => Assert.Equal("99", Output("int x = 99; print(x);"));

    [Fact]
    public void DeclareString_DefaultEmpty()
        => Assert.Equal("", Output("string s; print(s);"));

    [Fact]
    public void DeclareBool_DefaultFalse()
        => Assert.Equal("false", Output("bool b; print(b);"));

    [Fact]
    public void Assign_UpdatesValue()
        => Assert.Equal("10", Output("int x = 5; x = 10; print(x);"));

    [Fact]
    public void Assign_ChainedExpression()
        => Assert.Equal("7", Output("int x = 3; int y = 4; x = y = 7; print(x);"));

    [Fact]
    public void UndeclaredVariable_Throws()
        => RunError("print(z);", "[1:7] Undefined symbol 'z'.");

    [Fact]
    public void DuplicateDeclaration_SameScope_Throws()
        => RunError("int x = 1; int x = 2;", "already declared");
}

public sealed class ScopeTests : Utilities
{
    [Fact]
    public void InnerScope_CanReadOuter()
        => Assert.Equal("5", Output("int x = 5; b(); void b() { print(x); }"));

    [Fact]
    public void InnerScope_ShadowsOuter_DoesNotAffectOuter()
    {
        var lines = Lines("""
            int x = 10;
            b();
            void b() {
                int x = 20;
                print(x);
            }
            print(x);
            """);
        Assert.Equal(["20", "10"], lines);
    }

    [Fact]
    public void AssignInInnerScope_AffectsOuter()
    {
        var lines = Lines("""
            int x = 1;
            void b() {
                x = 42;
            }
            b();
            print(x);
            """);
        Assert.Equal(["42"], lines);
    }
}

public sealed class ControlFlowTests : Utilities
{
    [Fact]
    public void If_TrueBranch_Executes()
        => Assert.Equal("yes", Output("if (true) { print(\"yes\"); }"));

    [Fact]
    public void If_FalseBranch_Skipped()
        => Assert.Equal("", Output("if (false) { print(\"no\"); }"));

    [Fact]
    public void IfElse_TrueTakesIf()
        => Assert.Equal("if", Output("if (true) { print(\"if\"); } else { print(\"else\"); }"));

    [Fact]
    public void IfElse_FalseTakesElse()
        => Assert.Equal("else", Output("if (false) { print(\"if\"); } else { print(\"else\"); }"));

    [Fact]
    public void IfElseIf_Chain()
    {
        var src = """
            int x = 2;
            if (x == 1) { print("one"); }
            else if (x == 2) { print("two"); }
            else { print("other"); }
            """;
        Assert.Equal("two", Output(src));
    }
    
    [Fact]
    public void While_CountsToFive()
    {
        var src = """
            int i = 0;
            while (i < 5) { i = i + 1; }
            print(i);
            """;
        Assert.Equal("5", Output(src));
    }

    [Fact]
    public void While_NeverEnters_WhenFalse()
        => Assert.Equal("", Output("while (false) { print(\"x\"); }"));

    [Fact]
    public void While_AccumulatesSum()
    {
        var src = """
            int s = 0;
            int i = 1;
            while (i <= 10) {
                s = s + i;
                i = i + 1;
            }
            print(s);
            """;
        Assert.Equal("55", Output(src));
    }
    
    [Fact]
    public void For_CountsToTen()
    {
        var src = """
            int s = 0;
            for (int i = 1; i <= 10; i = i + 1) {
                s = s + i;
            }
            print(s);
            """;
        Assert.Equal("55", Output(src));
    }

    [Fact]
    public void For_IteratorVarNotVisibleAfterLoop()
        => RunError("""
            for (int i = 0; i < 3; i = i + 1) {}
            print(i);
            """, "2:7] Undefined symbol 'i'.");
    
    [Fact]
    public void Foreach_IteratesAllElements()
    {
        var src = """
            int[] nums = [10, 20, 30];
            int s = 0;
            foreach (int n in nums) {
                s = s + n;
            }
            print(s);
            """;
        Assert.Equal("60", Output(src));
    }

    [Fact]
    public void Foreach_PrintsEachElement()
    {
        var lines = Lines("""
            string[] words = ["a", "b", "c"];
            foreach (string w in words) {
                print(w);
            }
            """);
        Assert.Equal(["a", "b", "c"], lines);
    }
    
    [Fact]
    public void Switch_MatchesCorrectCase()
    {
        var src = """
            int x = 2;
            switch (x) {
                case 1: print("one");
                case 2: print("two");
                case 3: print("three");
            }
            """;
        Assert.Equal("two", Output(src));
    }

    [Fact]
    public void Switch_DefaultFallback()
    {
        var src = """
            int x = 99;
            switch (x) {
                case 1: print("one");
                default: print("other");
            }
            """;
        Assert.Equal("other", Output(src));
    }

    [Fact]
    public void Switch_NoMatch_NoOutput()
    {
        var src = """
            int x = 5;
            switch (x) {
                case 1: print("one");
                case 2: print("two");
            }
            """;
        Assert.Equal("", Output(src));
    }
}