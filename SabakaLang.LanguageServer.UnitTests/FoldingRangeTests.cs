using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SabakaLang.LanguageServer;
using SabakaLang.LanguageServer.Handlers;

namespace SabakaLang.LanguageServer.UnitTests;

public class FoldingRangeTests
{
    private static (DocumentStore, FoldingRangeHandler) Setup(string uri, string source)
    {
        var store = new DocumentStore();
        store.Analyze(uri, source);
        return (store, new FoldingRangeHandler(store));
    }

    private static FoldingRangeRequestParam Params(string uri) =>
        new() { TextDocument = new TextDocumentIdentifier { Uri = uri } };

    [Fact]
    public async Task FoldingRange_Class_ProducesRange()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri,
            "class Dog {\n    int age;\n    void bark() {}\n}");

        var result = await handler.Handle(Params(uri), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Should().NotBeEmpty();
        result.Any(r => r.StartLine < r.EndLine).Should().BeTrue();
    }

    [Fact]
    public async Task FoldingRange_Function_ProducesRange()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri,
            "int add(int a, int b) {\n    return a + b;\n}");

        var result = await handler.Handle(Params(uri), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Any(r => r.StartLine == 0 && r.EndLine > 0).Should().BeTrue();
    }

    [Fact]
    public async Task FoldingRange_IfStatement_ProducesRange()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri,
            "int x = 1;\nif (x > 0) {\n    print(\"yes\");\n}");

        var result = await handler.Handle(Params(uri), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Should().NotBeEmpty();
    }

    [Fact]
    public async Task FoldingRange_WhileLoop_ProducesRange()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri,
            "int i = 0;\nwhile (i < 10) {\n    i = i + 1;\n}");

        var result = await handler.Handle(Params(uri), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Should().NotBeEmpty();
    }

    [Fact]
    public async Task FoldingRange_Enum_ProducesRange()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri,
            "enum Status {\n    Active,\n    Inactive,\n    Pending\n}");

        var result = await handler.Handle(Params(uri), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Should().NotBeEmpty();
    }

    [Fact]
    public async Task FoldingRange_MultipleImports_ProducesImportsFold()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri,
            "import \"a\";\nimport \"b\";\nimport \"c\";\nint x = 1;");

        var result = await handler.Handle(Params(uri), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Any(r => r.Kind == FoldingRangeKind.Imports).Should().BeTrue();
    }

    [Fact]
    public async Task FoldingRange_SingleImport_NoImportsFold()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "import \"a\";\nint x = 1;");

        var result = await handler.Handle(Params(uri), CancellationToken.None);

        // single import should not produce an imports fold
        if (result is not null)
            result.Any(r => r.Kind == FoldingRangeKind.Imports).Should().BeFalse();
    }

    [Fact]
    public async Task FoldingRange_DocumentNotFound_ReturnsNull()
    {
        var store = new DocumentStore();
        var handler = new FoldingRangeHandler(store);

        var result = await handler.Handle(
            new FoldingRangeRequestParam
            {
                TextDocument = new TextDocumentIdentifier { Uri = "file:///missing.sabaka" }
            },
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task FoldingRange_SingleLineFunctions_NoFolds()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "void noop() {}");

        var result = await handler.Handle(Params(uri), CancellationToken.None);

        // single-line constructs produce no folds (endLine == startLine)
        if (result is not null)
            result.Should().BeEmpty();
    }

    [Fact]
    public async Task FoldingRange_NestedBlocks_MultipleFolds()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri,
            "class Outer {\n" +
            "    void method() {\n" +
            "        if (true) {\n" +
            "            print(\"x\");\n" +
            "        }\n" +
            "    }\n" +
            "}");

        var result = await handler.Handle(Params(uri), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Count().Should().BeGreaterThanOrEqualTo(3); // class + method + if
    }

    [Fact]
    public async Task FoldingRange_ForStatement_ProducesRange()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri,
            "for (int i = 0; i < 10; i++) {\n    print(i);\n}");

        var result = await handler.Handle(Params(uri), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Should().NotBeEmpty();
    }
}
