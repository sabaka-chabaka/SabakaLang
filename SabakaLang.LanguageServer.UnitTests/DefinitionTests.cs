using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SabakaLang.LanguageServer;
using SabakaLang.LanguageServer.Handlers;
using LspPosition = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;

namespace SabakaLang.LanguageServer.UnitTests;

public class DefinitionTests
{
    private static (DocumentStore, DefinitionHandler) Setup(string uri, string source)
    {
        var store = new DocumentStore();
        store.Analyze(uri, source);
        return (store, new DefinitionHandler(store));
    }

    private static DefinitionParams Params(string uri, int line, int col) =>
        new()
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new LspPosition(line, col)
        };

    [Fact]
    public async Task Definition_OnFunctionCall_ReturnsDeclarationLocation()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri,
            "int add(int a, int b) { return a + b; }\nadd(1, 2);");

        var result = await handler.Handle(Params(uri, 1, 1), CancellationToken.None);

        result.Should().NotBeNull();
        var locations = result!.Select(l => l.Location!).ToList();
        locations.Should().NotBeEmpty();
        // definition should be on line 0 (1-based line 1 in compiler, 0-based in LSP)
        locations[0].Range.Start.Line.Should().Be(0);
    }

    [Fact]
    public async Task Definition_OnClass_ReturnsClassDeclaration()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri,
            "class MyClass {}\nMyClass obj = new MyClass();");

        var result = await handler.Handle(Params(uri, 1, 0), CancellationToken.None);

        result.Should().NotBeNull();
        var locations = result!.Select(l => l.Location!).ToList();
        locations.Should().NotBeEmpty();
        locations[0].Range.Start.Line.Should().Be(0);
    }

    [Fact]
    public async Task Definition_OnBuiltin_ReturnsNull()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "print(\"hello\");");

        var result = await handler.Handle(Params(uri, 0, 1), CancellationToken.None);

        // builtins have zero spans, should be filtered
        if (result is not null)
        {
            var locations = result.Select(l => l.Location!).ToList();
            locations.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Definition_DocumentNotFound_ReturnsNull()
    {
        var store = new DocumentStore();
        var handler = new DefinitionHandler(store);

        var result = await handler.Handle(
            Params("file:///missing.sabaka", 0, 0),
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Definition_OnOperator_ReturnsNull()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "int x = 1 + 2;");

        // hover on '+'
        var result = await handler.Handle(Params(uri, 0, 10), CancellationToken.None);

        // '+' is not an identifier, should return null
        if (result is not null)
        {
            var locs = result.Select(l => l.Location!).ToList();
            locs.Should().BeEmpty();
        }
    }
}
