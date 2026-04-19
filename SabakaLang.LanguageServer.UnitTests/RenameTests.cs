using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SabakaLang.LanguageServer;
using SabakaLang.LanguageServer.Handlers;
using LspPosition = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;

namespace SabakaLang.LanguageServer.UnitTests;

public class RenameTests
{
    private static (DocumentStore, RenameHandler) Setup(string uri, string source)
    {
        var store = new DocumentStore();
        store.Analyze(uri, source);
        return (store, new RenameHandler(store));
    }

    private static RenameParams MakeRename(string uri, int line, int col, string newName) =>
        new()
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new LspPosition(line, col),
            NewName = newName
        };

    private static PrepareRenameParams MakePrepare(string uri, int line, int col) =>
        new()
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new LspPosition(line, col)
        };

    [Fact]
    public async Task Rename_Variable_ReplacesAllOccurrences()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "int foo = 1;\nfoo = foo + 1;");

        var result = await handler.Handle(MakeRename(uri, 0, 4, "bar"), CancellationToken.None);

        result.Should().NotBeNull();
        var edits = result!.Changes![DocumentUri.Parse(uri)].ToList();
        edits.Should().NotBeEmpty();
        edits.Should().AllSatisfy(e => e.NewText.Should().Be("bar"));
        // foo appears at least 3 times: declaration and 2 uses
        edits.Count.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Rename_Function_ReplacesAllCallSites()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri,
            "void oldName() {}\noldName();\noldName();");

        var result = await handler.Handle(MakeRename(uri, 0, 6, "newName"), CancellationToken.None);

        result.Should().NotBeNull();
        var edits = result!.Changes![DocumentUri.Parse(uri)].ToList();
        edits.Should().AllSatisfy(e => e.NewText.Should().Be("newName"));
        edits.Count.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Rename_DocumentNotFound_ReturnsNull()
    {
        var store = new DocumentStore();
        var handler = new RenameHandler(store);

        var result = await handler.Handle(
            MakeRename("file:///missing.sabaka", 0, 0, "x"),
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Rename_OnOperator_ReturnsNull()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "int x = 1 + 2;");

        var result = await handler.Handle(MakeRename(uri, 0, 10, "plus"), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task PrepareRename_OnIdentifier_ReturnsRange()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "int myVar = 5;");

        var result = await handler.Handle(MakePrepare(uri, 0, 4), CancellationToken.None);

        result.Should().NotBeNull();
        result!.PlaceholderRange.Should().NotBeNull();
        result.PlaceholderRange!.Placeholder.Should().Be("myVar");
    }

    [Fact]
    public async Task PrepareRename_OnBuiltin_ReturnsNull()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "print(\"hello\");");

        var result = await handler.Handle(MakePrepare(uri, 0, 1), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task PrepareRename_DocumentNotFound_ReturnsNull()
    {
        var store = new DocumentStore();
        var handler = new RenameHandler(store);

        var result = await handler.Handle(
            MakePrepare("file:///missing.sabaka", 0, 0),
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Rename_ClassRenamesAllReferences()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri,
            "class OldName {}\nOldName a = new OldName();");

        var result = await handler.Handle(MakeRename(uri, 0, 6, "NewName"), CancellationToken.None);

        result.Should().NotBeNull();
        var edits = result!.Changes![DocumentUri.Parse(uri)].ToList();
        edits.Should().AllSatisfy(e => e.NewText.Should().Be("NewName"));
        edits.Count.Should().BeGreaterThanOrEqualTo(3);
    }
}
