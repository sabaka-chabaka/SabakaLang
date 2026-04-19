using SabakaLang.Compiler;
using SabakaLang.LanguageServer;

namespace SabakaLang.LanguageServer.UnitTests;

public class DocumentStoreTests
{
    private static DocumentStore NewStore() => new();

    [Fact]
    public void Analyze_SimpleSource_ReturnsAnalysis()
    {
        var store = NewStore();
        var analysis = store.Analyze("file:///test.sabaka", "int x = 5;");

        analysis.Should().NotBeNull();
        analysis.Uri.Should().Be("file:///test.sabaka");
        analysis.Source.Should().Be("int x = 5;");
    }

    [Fact]
    public void Analyze_ValidSource_HasNoDiagnostics()
    {
        var store = NewStore();
        var analysis = store.Analyze("file:///test.sabaka", "int x = 1;\nint y = 2;");

        analysis.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Analyze_UndefinedSymbol_HasDiagnostic()
    {
        var store = NewStore();
        var analysis = store.Analyze("file:///test.sabaka", "x = 5;");

        analysis.Diagnostics.Should().NotBeEmpty();
        analysis.Diagnostics.Should().Contain(d => d.Message.Contains("x"));
    }

    [Fact]
    public void Analyze_LexerError_HasDiagnostic()
    {
        var store = NewStore();
        var analysis = store.Analyze("file:///test.sabaka", "@@@");

        analysis.Diagnostics.Should().NotBeEmpty();
        analysis.Diagnostics.Should().Contain(d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Get_ExistingDocument_ReturnsIt()
    {
        var store = NewStore();
        store.Analyze("file:///a.sabaka", "int x = 1;");

        var result = store.Get("file:///a.sabaka");

        result.Should().NotBeNull();
        result!.Source.Should().Be("int x = 1;");
    }

    [Fact]
    public void Get_NonExistentDocument_ReturnsNull()
    {
        var store = NewStore();

        var result = store.Get("file:///nonexistent.sabaka");

        result.Should().BeNull();
    }

    [Fact]
    public void Remove_ExistingDocument_RemovesIt()
    {
        var store = NewStore();
        store.Analyze("file:///a.sabaka", "int x = 1;");
        store.Remove("file:///a.sabaka");

        store.Get("file:///a.sabaka").Should().BeNull();
    }

    [Fact]
    public void Analyze_OverwritesExisting()
    {
        var store = NewStore();
        store.Analyze("file:///a.sabaka", "int x = 1;");
        store.Analyze("file:///a.sabaka", "int y = 2;");

        var result = store.Get("file:///a.sabaka");
        result!.Source.Should().Be("int y = 2;");
    }

    [Fact]
    public void All_ReturnsAllDocuments()
    {
        var store = NewStore();
        store.Analyze("file:///a.sabaka", "int x = 1;");
        store.Analyze("file:///b.sabaka", "int y = 2;");

        store.All().Should().HaveCount(2);
    }

    [Fact]
    public void Analyze_FunctionDecl_PopulatesSymbols()
    {
        var store = NewStore();
        var analysis = store.Analyze("file:///test.sabaka", "int add(int a, int b) { return a + b; }");

        var syms = analysis.Bind.Symbols.Lookup("add").ToList();
        syms.Should().NotBeEmpty();
        syms[0].Kind.Should().Be(SymbolKind.Function);
    }

    [Fact]
    public void Analyze_ClassDecl_PopulatesSymbols()
    {
        var store = NewStore();
        var analysis = store.Analyze("file:///test.sabaka",
            "class Dog { int age; void bark() {} }");

        analysis.Bind.Symbols.Lookup("Dog").Should().NotBeEmpty();
        analysis.Bind.Symbols.MembersOf("Dog").Should().HaveCount(2); // age, bark
    }

    [Fact]
    public async Task Analyze_IsThreadSafe()
    {
        var store = NewStore();
        var tasks = Enumerable.Range(0, 20).Select(i =>
            Task.Run(() => store.Analyze($"file:///test{i}.sabaka", $"int x{i} = {i};")));

        Func<Task> act = () => Task.WhenAll(tasks);
    
        await act.Should().NotThrowAsync();
    }
}
