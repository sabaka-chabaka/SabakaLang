using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SabakaLang.LanguageServer;
using SabakaLang.LanguageServer.Handlers;
using LspPosition = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;

namespace SabakaLang.LanguageServer.UnitTests;

public class CompletionTests
{
    private static (DocumentStore store, CompletionHandler handler) Setup(string uri, string source)
    {
        var store = new DocumentStore();
        store.Analyze(uri, source);
        var handler = new CompletionHandler(store);
        return (store, handler);
    }

    private static CompletionParams Params(string uri, int line, int col) =>
        new()
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new LspPosition(line, col),
            Context = new CompletionContext { TriggerKind = CompletionTriggerKind.Invoked }
        };

    [Fact]
    public async Task Completion_ContainsKeywords()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "");

        var result = await handler.Handle(Params(uri, 0, 0), CancellationToken.None);

        var labels = result.Items.Select(i => i.Label).ToList();
        labels.Should().Contain("if");
        labels.Should().Contain("while");
        labels.Should().Contain("class");
        labels.Should().Contain("int");
        labels.Should().Contain("return");
    }

    [Fact]
    public async Task Completion_ContainsBuiltins()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "");

        var result = await handler.Handle(Params(uri, 0, 0), CancellationToken.None);

        var labels = result.Items.Select(i => i.Label).ToList();
        labels.Should().Contain("print");
        labels.Should().Contain("input");
        labels.Should().Contain("httpGet");
    }

    [Fact]
    public async Task Completion_ContainsDeclaredFunction()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "int compute(int x) { return x * 2; }\n");

        var result = await handler.Handle(Params(uri, 1, 0), CancellationToken.None);

        var labels = result.Items.Select(i => i.Label).ToList();
        labels.Should().Contain("compute");
    }

    [Fact]
    public async Task Completion_FunctionHasSnippetInsertText()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "void greet(string name) {}\n");

        var result = await handler.Handle(Params(uri, 1, 0), CancellationToken.None);

        var greet = result.Items.FirstOrDefault(i => i.Label == "greet");
        greet.Should().NotBeNull();
        greet!.InsertTextFormat.Should().Be(InsertTextFormat.Snippet);
        greet.InsertText.Should().Contain("$0");
    }

    [Fact]
    public async Task Completion_MemberAccess_ShowsClassMembers()
    {
        var uri = "file:///test.sabaka";
        // source with 'dog.' to trigger member access
        var source = "class Dog { int age; void bark() {} }\nDog dog = new Dog();\ndog.";
        var (_, handler) = Setup(uri, source);

        // position after the dot on line 2
        var result = await handler.Handle(Params(uri, 2, 4), CancellationToken.None);

        var labels = result.Items.Select(i => i.Label).ToList();
        labels.Should().Contain("age");
        labels.Should().Contain("bark");
    }

    [Fact]
    public async Task Completion_StringMemberAccess_ShowsStringBuiltins()
    {
        var uri = "file:///test.sabaka";
        var source = "string s = \"hello\";\ns.";
        var (_, handler) = Setup(uri, source);

        var result = await handler.Handle(Params(uri, 1, 2), CancellationToken.None);

        var labels = result.Items.Select(i => i.Label).ToList();
        labels.Should().Contain("length");
        labels.Should().Contain("toUpper");
        labels.Should().Contain("contains");
    }

    [Fact]
    public async Task Completion_DocumentNotFound_ReturnsEmpty()
    {
        var store = new DocumentStore();
        var handler = new CompletionHandler(store);

        var result = await handler.Handle(
            Params("file:///missing.sabaka", 0, 0),
            CancellationToken.None);

        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Completion_ClassTypesIncluded()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri,
            "class Animal {}\nclass Dog : Animal {}\n");

        var result = await handler.Handle(Params(uri, 2, 0), CancellationToken.None);

        var labels = result.Items.Select(i => i.Label).ToList();
        labels.Should().Contain("Animal");
        labels.Should().Contain("Dog");
    }

    [Fact]
    public async Task Completion_EnumStaticAccess_ShowsMembers()
    {
        var uri = "file:///test.sabaka";
        var source = "enum Status { Active, Inactive }\nStatus::";
        var (_, handler) = Setup(uri, source);

        var result = await handler.Handle(Params(uri, 1, 8), CancellationToken.None);

        var labels = result.Items.Select(i => i.Label).ToList();
        labels.Should().Contain("Active");
        labels.Should().Contain("Inactive");
    }
}
