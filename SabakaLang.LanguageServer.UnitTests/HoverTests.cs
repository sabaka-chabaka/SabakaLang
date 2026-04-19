using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SabakaLang.LanguageServer;
using SabakaLang.LanguageServer.Handlers;
using LspPosition = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;

namespace SabakaLang.LanguageServer.UnitTests;

public class HoverTests
{
    private static (DocumentStore store, HoverHandler handler) Setup(string uri, string source)
    {
        var store = new DocumentStore();
        store.Analyze(uri, source);
        var handler = new HoverHandler(store);
        return (store, handler);
    }

    private static HoverParams MakeParams(string uri, int line, int col) =>
        new()
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new LspPosition(line, col)
        };

    [Fact]
    public async Task Hover_OnFunction_ReturnsInfo()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "int add(int a, int b) { return a + b; }\nadd(1, 2);");

        // hover on 'add' at second line (call site)
        var result = await handler.Handle(MakeParams(uri, 1, 1), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Contents.MarkupContent!.Value.Should().Contain("add");
        result.Contents.MarkupContent.Value.Should().Contain("int");
    }

    [Fact]
    public async Task Hover_OnVariable_ReturnsTypeInfo()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "int myVar = 42;");

        var result = await handler.Handle(MakeParams(uri, 0, 4), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Contents.MarkupContent!.Value.Should().Contain("myVar");
    }

    [Fact]
    public async Task Hover_OnBuiltin_ReturnsInfo()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "print(\"hello\");");

        var result = await handler.Handle(MakeParams(uri, 0, 1), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Contents.MarkupContent!.Value.Should().Contain("print");
        result.Contents.MarkupContent.Value.Should().Contain("BuiltIn");
    }

    [Fact]
    public async Task Hover_OnClass_ShowsClassInfo()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "class Animal { }\nAnimal a = new Animal();");

        var result = await handler.Handle(MakeParams(uri, 1, 0), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Contents.MarkupContent!.Value.Should().Contain("Animal");
    }

    [Fact]
    public async Task Hover_OnUnknownWord_ReturnsNull()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "int x = 5;");

        // hover on '=' which is not an identifier
        var result = await handler.Handle(MakeParams(uri, 0, 8), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Hover_DocumentNotFound_ReturnsNull()
    {
        var store = new DocumentStore();
        var handler = new HoverHandler(store);

        var result = await handler.Handle(
            MakeParams("file:///missing.sabaka", 0, 0),
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Hover_OnEnum_ShowsEnumInfo()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "enum Color { Red, Green, Blue }");

        var result = await handler.Handle(MakeParams(uri, 0, 6), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Contents.MarkupContent!.Value.Should().Contain("Color");
    }
}
