using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SabakaLang.LanguageServer;
using SabakaLang.LanguageServer.Handlers;
using LspPosition = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;

namespace SabakaLang.LanguageServer.UnitTests;

public class ReferencesTests
{
    private static (DocumentStore, ReferencesHandler) Setup(string uri, string source)
    {
        var store = new DocumentStore();
        store.Analyze(uri, source);
        return (store, new ReferencesHandler(store));
    }

    private static ReferenceParams Params(string uri, int line, int col, bool includeDecl = false) =>
        new()
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new LspPosition(line, col),
            Context = new ReferenceContext { IncludeDeclaration = includeDecl }
        };

    [Fact]
    public async Task References_FindsAllUsages()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri,
            "int x = 1;\nint y = x + 2;\nprint(x);");

        var result = await handler.Handle(Params(uri, 0, 4), CancellationToken.None);

        result.Should().NotBeNull();
        // x appears at least on lines 1 and 2 (y = x + 2 and print(x))
        result!.Count().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task References_IncludeDeclaration_IncludesDefSite()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri,
            "int counter = 0;\ncounter = counter + 1;");

        var result = await handler.Handle(Params(uri, 0, 4, includeDecl: true), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Count().Should().BeGreaterThanOrEqualTo(3); // decl + 2 uses on line 1
    }

    [Fact]
    public async Task References_ExcludeDeclaration_OmitsDefSite()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri,
            "int val = 0;\nval = 5;");

        var withDecl    = await handler.Handle(Params(uri, 0, 4, includeDecl: true),  CancellationToken.None);
        var withoutDecl = await handler.Handle(Params(uri, 0, 4, includeDecl: false), CancellationToken.None);

        withDecl!.Count().Should().BeGreaterThan(withoutDecl!.Count());
    }

    [Fact]
    public async Task References_DocumentNotFound_ReturnsNull()
    {
        var store = new DocumentStore();
        var handler = new ReferencesHandler(store);

        var result = await handler.Handle(
            Params("file:///missing.sabaka", 0, 0),
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task References_OnOperator_ReturnsEmpty()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "int x = 1 + 2;");

        // position of '+'
        var result = await handler.Handle(Params(uri, 0, 10), CancellationToken.None);

        // '+' is not an identifier — no refs found
        if (result is not null)
            result.Count().Should().Be(0);
    }

    [Fact]
    public async Task References_Function_FindsAllCallSites()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri,
            "void greet() { print(\"hi\"); }\ngreet();\ngreet();");

        var result = await handler.Handle(Params(uri, 0, 6), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Count().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task References_DeduplicatesResults()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "int x = 1;\nprint(x);");

        var result = await handler.Handle(Params(uri, 0, 4, includeDecl: true), CancellationToken.None);
        var locations = result!.ToList();

        // No two locations should share the same start position
        var positions = locations.Select(l => $"{l.Range.Start.Line}:{l.Range.Start.Character}");
        positions.Should().OnlyHaveUniqueItems();
    }
}
