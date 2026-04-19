using SabakaLang.LanguageServer;

namespace SabakaLang.LanguageServer.UnitTests;

public class DiagnosticsTests
{
    private static DocumentAnalysis Analyze(string source) =>
        new DocumentStore().Analyze("file:///test.sabaka", source);

    [Fact]
    public void NoErrors_EmptyDiagnostics()
    {
        var analysis = Analyze("int x = 5;");
        analysis.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void UndefinedVariable_ProducesError()
    {
        var analysis = Analyze("x = 5;");
        analysis.Diagnostics.Should().Contain(d =>
            d.Message.Contains("x") && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void UnterminatedString_ProducesError()
    {
        var analysis = Analyze("string s = \"hello;");
        analysis.Diagnostics.Should().Contain(d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void UnknownBaseClass_ProducesWarning()
    {
        var analysis = Analyze("class Dog : Animal {}");
        analysis.Diagnostics.Should().Contain(d =>
            d.Message.Contains("Animal"));
    }

    [Fact]
    public void ReturnOutsideFunction_ProducesWarning()
    {
        var analysis = Analyze("return 5;");
        analysis.Diagnostics.Should().Contain(d =>
            d.Message.Contains("return"));
    }

    [Fact]
    public void MultipleErrors_AllReported()
    {
        var analysis = Analyze("a = 1;\nb = 2;\nc = 3;");
        analysis.Diagnostics.Should().HaveCountGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void ValidClass_NoDiagnostics()
    {
        var analysis = Analyze(
            "class Dog { int age; void bark() { print(\"woof\"); } }");
        analysis.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void ValidEnum_NoDiagnostics()
    {
        var analysis = Analyze("enum Color { Red, Green, Blue }");
        analysis.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Diagnostic_HasCorrectPosition()
    {
        var analysis = Analyze("undefined_var = 5;");
        var diag = analysis.Diagnostics.First();
        diag.Start.Line.Should().Be(1);     // 1-based from compiler
        diag.Start.Column.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ValidFunction_NoDiagnostics()
    {
        var analysis = Analyze("int add(int a, int b) { return a + b; }");
        analysis.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void InvalidSuperUsage_ProducesWarning()
    {
        var analysis = Analyze("void foo() { super; }");
        analysis.Diagnostics.Should().Contain(d => d.Message.Contains("super"));
    }

    [Fact]
    public void ValidInterface_NoDiagnostics()
    {
        var analysis = Analyze("interface IFly { void fly(); }");
        analysis.Diagnostics.Should().BeEmpty();
    }
}
