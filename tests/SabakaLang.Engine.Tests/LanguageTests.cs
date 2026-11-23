namespace SabakaLang.Engine.Tests;

/// <summary>Regression tests for bugs of the old compiler/VM and for core language behaviour.</summary>
public class LanguageTests
{
    [Fact]
    public void Functions_DoNotSeeCallersLocals()
    {
        // old VM resolved names through the whole call stack (dynamic scoping)
        var errors = Sabaka.CompileErrors("void f() { print(x); } void g() { int x = 1; f(); } g();");
        Assert.Contains("Undefined symbol 'x'.", errors[0]);
    }

    [Fact]
    public void Recursion_UsesOwnLocals() =>
        Assert.Equal("120", Sabaka.Run("int fact(int n) { if (n <= 1) { return 1; } int r = n * fact(n - 1); return r; } print(fact(5));"));

    [Fact]
    public void Print_MultipleArguments_PrintsAll() =>
        Assert.Equal("1 two 3", Sabaka.Run("print(1, \"two\", 3);"));

    [Fact]
    public void Statements_DoNotLeakStackValues() =>
        Assert.Equal("100000", Sabaka.Run("int n = 0; for (int i = 0; i < 100000; i++) { n + 1; n++; } print(n);"));

    [Fact]
    public void IntToFloat_ConvertsOnAssignment() =>
        Assert.Equal("0.5", Sabaka.Run("float x = 1; print(x / 2);"));

    [Fact]
    public void IntToFloat_ConvertsArgumentsAndReturns() =>
        Assert.Equal("1.5\n2.5", Sabaka.Run("float half(float v) { return v / 2; } float three() { return 3; } print(half(3)); print(three() - 0.5);"));

    [Fact]
    public void CharIndexing_ReturnsChar_AndConvertsToString() =>
        Assert.Equal("b true", Sabaka.Run("string s = \"abc\"; string ch = s[1]; print(ch, s[0] == 'a');"));

    [Fact]
    public void CharEscapes_Work() =>
        Assert.Equal("10", Sabaka.Run("print(ord('\\n'));"));

    [Fact]
    public void BlockComments_AreSkipped() =>
        Assert.Equal("2", Sabaka.Run("/* a\n comment */ print(1 + 1);"));

    [Fact]
    public void FieldNamedLength_IsNotTreatedAsArrayLength() =>
        Assert.Equal("42", Sabaka.Run("class Box { int length; } Box b = new Box(); b.length = 42; print(b.length);"));

    [Fact]
    public void UserFunction_ShadowsBuiltin() =>
        Assert.Equal("mine", Sabaka.Run("string max(int a, int b) { return \"mine\"; } print(max(1, 2));"));

    [Fact]
    public void Enums_FromDifferentTypes_AreNotEqual() =>
        Assert.Equal("false\ntrue", Sabaka.Run("enum A { X, Y } enum B { X, Y } print(A.X == A.Y); print(A.Y == A.Y);"));

    [Fact]
    public void Enum_PrintsMemberName_AndSwitchWorks() =>
        Assert.Equal("Green\ngreen", Sabaka.Run("enum C { Red, Green } C c = C.Green; print(c); switch (c) { case C.Red: print(\"red\"); case C.Green: print(\"green\"); }"));

    [Fact]
    public void Switch_Default_AndNoFallthrough() =>
        Assert.Equal("two", Sabaka.Run("int x = 2; switch (x) { case 1: print(\"one\"); case 2: print(\"two\"); default: print(\"other\"); }"));

    [Fact]
    public void BreakAndContinue()
    {
        Assert.Equal("0 1 3", Sabaka.Run("string s = \"\"; for (int i = 0; i < 10; i++) { if (i == 2) { continue; } if (i == 4) { break; } s += i + \" \"; } print(s.trim());"));
        Assert.Equal("3", Sabaka.Run("int n = 0; while (true) { n++; if (n == 3) { break; } } print(n);"));
    }

    [Fact]
    public void BreakOutsideLoop_IsError() =>
        Assert.Contains("break", Sabaka.CompileErrors("break;")[0]);

    [Fact]
    public void CompoundAssignment_EvaluatesTargetOnce() =>
        Assert.Equal("5\n1", Sabaka.Run("int calls = 0; int[] a = [1, 2, 3]; int idx() { calls++; return 1; } a[idx()] += 3; print(a[1]); print(calls);"));

    [Fact]
    public void CompoundAssignment_AllOperators() =>
        Assert.Equal("7 3 12 1.5 1", Sabaka.Run("int a = 5; a += 2; int b = 10; b -= 7; int c = 3; c *= 4; float d = 3.0; d /= 2; int e = 10; e %= 3; print(a, b, c, d, e);"));

    [Fact]
    public void PrefixIncrement() =>
        Assert.Equal("2 2", Sabaka.Run("int i = 1; int j = ++i; print(i, j);"));

    [Fact]
    public void Struct_HasValueSemantics() =>
        Assert.Equal("1 2", Sabaka.Run("struct P { int x; } P a = new P(); a.x = 1; P b = a; b.x = 2; print(a.x, b.x);"));

    [Fact]
    public void Struct_PassedByValue() =>
        Assert.Equal("1", Sabaka.Run("struct P { int x; } void bump(P p) { p.x = 99; } P a = new P(); a.x = 1; bump(a); print(a.x);"));

    [Fact]
    public void FieldDefaults_FollowType() =>
        Assert.Equal("0 |  false 0", Sabaka.Run("class C { int i; string s; bool b; float f; } C c = new C(); print(c.i, \"|\", c.s, c.b, c.f);"));

    [Fact]
    public void ClassConst_AndInitializers() =>
        Assert.Equal("10 7", Sabaka.Run("class C { const int MAX = 10; int v = 7; } C c = new C(); print(C.MAX, c.v);"));

    [Fact]
    public void Inheritance_Polymorphism_AndSuper() =>
        Assert.Equal("Rex makes a sound\nRex barks", Sabaka.Run(@"
            class Animal { string name; Animal(string n) { name = n; } string speak() { return name + "" makes a sound""; } }
            class Dog : Animal { Dog(string n) { super(n); } override string speak() { return name + "" barks""; } }
            Animal a = new Animal(""Rex""); print(a.speak());
            Animal d = new Dog(""Rex""); print(d.speak());"));

    [Fact]
    public void Interface_Implementation()
    {
        Assert.Equal("woof", Sabaka.Run("interface ISpeak { string speak(); } class Dog : ISpeak { string speak() { return \"woof\"; } } ISpeak s = new Dog(); print(s.speak());"));
        Assert.Contains("does not implement", Sabaka.CompileErrors("interface I { void m(); } class C : I { }")[0]);
    }

    [Fact]
    public void ToString_IsUsedByPrintAndInterpolation() =>
        Assert.Equal("P(1)\nvalue=P(1)", Sabaka.Run("class P { int x = 1; string toString() { return \"P(\" + x + \")\"; } } P p = new P(); print(p); print($\"value={p}\");"));

    [Fact]
    public void Generics_Class_AndInference()
    {
        Assert.Equal("5 hi", Sabaka.Run("class Box<T> { T v; Box(T x) { v = x; } T get() { return v; } } Box<int> a = new Box<int>(5); Box<string> b = new Box<string>(\"hi\"); print(a.get(), b.get());"));
        Assert.Equal("3", Sabaka.Run("T id<T>(T x) { return x; } print(id(3));"));
        Assert.Contains("Cannot convert", Sabaka.CompileErrors("class Box<T> { T v; } Box<int> b = new Box<int>(); b.v = \"x\";")[0]);
    }

    [Fact]
    public void NestedGenericTypes_Parse() =>
        Assert.Empty(Sabaka.CompileErrors("class Box<T> { T v; } Box<Box<int>> b = new Box<Box<int>>(); Box<int[]> c = new Box<int[]>();"));

    [Fact]
    public void IsOperator_WithTernary_IsNotNullableType() =>
        Assert.Equal("yes", Sabaka.Run("int x = 1; string r = x is int ? \"yes\" : \"no\"; print(r);"));

    [Fact]
    public void Arrays_NewArray_Jagged_AndGrowth() =>
        Assert.Equal("0 3 7 4", Sabaka.Run("int[] a = new int[3]; int[][] j = [[1], [2, 3]]; int[] g = []; g[0] = 7; print(a[0], a.length, g[0], j[1][1] + 1);").Replace("0 3 7 4", "0 3 7 4"));

    [Fact]
    public void Strings_Foreach_AndInterpolation() =>
        Assert.Equal("a-b-c- 3", Sabaka.Run("string r = \"\"; foreach (string c in \"abc\") { r += c + \"-\"; } print(r, \"abc\".length);"));

    [Fact]
    public void NullableAndCoalesce() =>
        Assert.Equal("fallback", Sabaka.Run("string? s = null; print(s ?? \"fallback\");"));

    [Fact]
    public void Globals_AreSharedWithFunctions() =>
        Assert.Equal("3", Sabaka.Run("int counter = 0; void inc() { counter++; } inc(); inc(); inc(); print(counter);"));

    [Fact]
    public void Globals_DeclaredAfterFunction_AreVisible() =>
        Assert.Equal("5", Sabaka.Run("int get() { return base; } int base = 5; print(get());"));

    [Fact]
    public void Constants_AreFoldedAndProtected()
    {
        Assert.Equal("14", Sabaka.Run("const int A = 2 * 3; const int B = A + 8; print(B);"));
        Assert.Contains("constant", Sabaka.CompileErrors("const int A = 1; A = 2;")[0]);
    }

    [Fact]
    public void Constants_AreScoped() =>
        Assert.Equal("1\n2", Sabaka.Run("void a() { const int K = 1; print(K); } void b() { const int K = 2; print(K); } a(); b();"));

    [Fact]
    public void Access_PrivateFieldIsProtected() =>
        Assert.Contains("private", Sabaka.CompileErrors("class C { private int x; } C c = new C(); c.x = 1;")[0]);

    [Fact]
    public void Return_AllPathsRequired()
    {
        Assert.Contains("all code paths", Sabaka.CompileErrors("int f(int x) { if (x > 0) { return 1; } }")[0]);
        Assert.Empty(Sabaka.CompileErrors("int f(int x) { if (x > 0) { return 1; } else { return 2; } }"));
        Assert.Empty(Sabaka.CompileErrors("int f() { while (true) { return 1; } }"));
    }

    [Fact]
    public void TypeErrors_AreReported()
    {
        Assert.Contains("Cannot convert 'string' to 'int'", Sabaka.CompileErrors("int x = \"a\";")[0]);
        Assert.Contains("Cannot convert 'float' to 'int'", Sabaka.CompileErrors("int x = 1.5;")[0]);
        Assert.Contains("expects 2 argument(s), got 1", Sabaka.CompileErrors("int add(int a, int b) { return a + b; } add(1);")[0]);
        Assert.Contains("already declared", Sabaka.CompileErrors("int x = 1; int x = 2;")[0]);
        Assert.Contains("Undefined symbol 'z'.", Sabaka.CompileErrors("print(z);")[0]);
    }

    [Fact]
    public void ErrorPosition_IsLineAndColumn() =>
        Assert.Equal("[1:7] Undefined symbol 'z'.", Sabaka.CompileErrors("print(z);")[0]);

    [Fact]
    public void Warning_NullToNonNullable()
    {
        var w = Sabaka.Warnings("int x = null;");
        Assert.Contains("non-nullable", w[0]);
        Assert.Contains("x", w[0]);
    }

    [Fact]
    public void HugeIntLiteral_IsDiagnostic_NotCrash() =>
        Assert.Contains("out of range", Sabaka.CompileErrors("int x = 99999999999;")[0]);

    [Fact]
    public void Runtime_DivisionByZero_AndBounds()
    {
        Assert.Contains("zero", Sabaka.RuntimeError("int z = 0; print(1 / z);"));
        Assert.Contains("out of bounds", Sabaka.RuntimeError("int[] a = [1]; print(a[5]);"));
        Assert.Contains("out of range", Sabaka.RuntimeError("string s = \"ab\"; print(s[9]);"));
    }

    [Fact]
    public void Runtime_StackOverflow_IsCatchableError() =>
        Assert.Contains("Stack overflow", Sabaka.RuntimeError("int f(int n) { return f(n + 1); } print(f(0));"));

    [Fact]
    public void Runtime_ErrorHasLineAndStack()
    {
        var result = Sabaka.NewEngine().Run("void boom() {\n  int z = 0;\n  print(1 / z);\n}\nboom();");
        Assert.Equal(3, result.RuntimeError!.Line);
        Assert.Contains(result.RuntimeError.ScriptStack, s => s.Contains("boom"));
    }

    [Fact]
    public void StringBuiltinMethods() =>
        Assert.Equal("ABC a,b,c true 2", Sabaka.Run("string s = \"abc\"; print(s.upper(), s.split(\"\").join(\",\"), s.contains(\"b\"), s.indexOf(\"c\"));"));

    [Fact]
    public void ArrayBuiltinMethods() =>
        Assert.Equal("3 3 1 [1, 2, 3]", Sabaka.Run("int[] a = [1, 2]; a.push(3); print(a.length, a.last(), a.first(), a);"));

    [Fact]
    public void Imports_SourceModules_AndAlias()
    {
        var modules = new Dictionary<string, string>
        {
            ["util.sabaka"] = "int twice(int x) { return x * 2; } const int K = 21; class Counter { int n; void inc() { n++; } } private int hidden = 1;"
        };
        Assert.Equal("42 3", Sabaka.Run("import \"util\" as u; Counter c = new Counter(); c.inc(); c.inc(); c.inc(); print(u.twice(u.K), c.n);".Replace("Counter c = new Counter()", "u.Counter c = new u.Counter()"), modules: modules));
        Assert.Equal("10", Sabaka.Run("import \"util\" from twice; print(twice(5));", modules: modules));
        Assert.Contains("hidden", Sabaka.CompileErrors("import \"util\" as u; print(u.hidden);", modules: modules)[0]);
    }

    [Fact]
    public void Imports_Unresolvable_IsError() =>
        Assert.Contains("Cannot resolve import", Sabaka.CompileErrors("import \"nope\";")[0]);

    [Fact]
    public void Imports_Cycle_IsError()
    {
        var modules = new Dictionary<string, string> { ["a.sabaka"] = "import \"b\";", ["b.sabaka"] = "import \"a\";" };
        Assert.Contains(Sabaka.CompileErrors("import \"a\";", modules: modules), e => e.Contains("Circular"));
    }
}
