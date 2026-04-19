using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SabakaLang.LanguageServer;
using SabakaLang.LanguageServer.Handlers;
using LspPosition = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;
using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace SabakaLang.LanguageServer.UnitTests;

public class CodeActionTests
{
    private static (DocumentStore, CodeActionHandler) Setup(string uri, string source)
    {
        var store = new DocumentStore();
        store.Analyze(uri, source);
        return (store, new CodeActionHandler(store));
    }

    private static CodeActionParams Params(
        string uri,
        LspRange range,
        IEnumerable<LspDiagnostic> diagnostics) =>
        new()
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Range = range,
            Context = new CodeActionContext
            {
                Diagnostics = new Container<LspDiagnostic>(diagnostics)
            }
        };

    private static LspDiagnostic MakeDiag(string message, int line = 0) =>
        new()
        {
            Message = message,
            Range = new LspRange(new LspPosition(line, 0), new LspPosition(line, 5)),
            Severity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Warning,
            Source = "sabaka-ls"
        };

    [Fact]
    public async Task CodeAction_UndefinedSymbol_OffersDeclareVariable()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "foo = 5;");

        var diag = MakeDiag("Undefined symbol 'foo'");
        var result = await handler.Handle(
            Params(uri, diag.Range, [diag]),
            CancellationToken.None);

        result.Should().NotBeNull();
        var actions = result!.Select(x => x.CodeAction!).ToList();
        actions.Should().Contain(a => a.Title.Contains("foo") && a.Title.Contains("Declare"));
    }

    [Fact]
    public async Task CodeAction_UnknownType_OffersCreateClass()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "MyType x = new MyType();");

        var diag = MakeDiag("Unknown type 'MyType'");
        var result = await handler.Handle(
            Params(uri, diag.Range, [diag]),
            CancellationToken.None);

        result.Should().NotBeNull();
        var actions = result!.Select(x => x.CodeAction!).ToList();
        actions.Should().Contain(a => a.Title.Contains("MyType") && a.Title.Contains("class"));
    }

    [Fact]
    public async Task CodeAction_AlwaysOffersOrganizeImports()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "int x = 1;");

        var result = await handler.Handle(
            Params(uri,
                new LspRange(new LspPosition(0, 0), new LspPosition(0, 1)),
                []),
            CancellationToken.None);

        result.Should().NotBeNull();
        var actions = result!.Select(x => x.CodeAction!).ToList();
        actions.Should().Contain(a => a.Kind == CodeActionKind.SourceOrganizeImports);
    }

    [Fact]
    public async Task CodeAction_DocumentNotFound_ReturnsNull()
    {
        var store = new DocumentStore();
        var handler = new CodeActionHandler(store);

        var result = await handler.Handle(
            Params("file:///missing.sabaka",
                new LspRange(new LspPosition(0, 0), new LspPosition(0, 5)),
                []),
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CodeAction_DeclareVariable_InsertsCorrectEdit()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "counter = 1;");

        var diag = MakeDiag("Undefined symbol 'counter'");
        var result = await handler.Handle(
            Params(uri, diag.Range, [diag]),
            CancellationToken.None);

        var action = result!.Select(x => x.CodeAction!)
            .First(a => a.Title.Contains("counter"));

        action.Edit.Should().NotBeNull();
        var edits = action.Edit!.Changes!.Values.First().ToList();
        edits.Should().NotBeEmpty();
        edits.First().NewText.Should().Contain("counter");
    }

    [Fact]
    public async Task CodeAction_CreateClass_InsertsClassStub()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "Widget w;");

        var diag = MakeDiag("Unknown type 'Widget'");
        var result = await handler.Handle(
            Params(uri, diag.Range, [diag]),
            CancellationToken.None);

        var action = result!.Select(x => x.CodeAction!)
            .First(a => a.Title.Contains("Widget"));

        action.Edit.Should().NotBeNull();
        var edits = action.Edit!.Changes!.Values.First().ToList();
        edits.First().NewText.Should().Contain("class Widget");
    }

    [Fact]
    public async Task CodeAction_OrganizeImports_SortsAlphabetically()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri,
            "import \"zzz\";\nimport \"aaa\";\nimport \"mmm\";");

        var result = await handler.Handle(
            Params(uri,
                new LspRange(new LspPosition(0, 0), new LspPosition(2, 10)),
                []),
            CancellationToken.None);

        var organizeAction = result!.Select(x => x.CodeAction!)
            .First(a => a.Kind == CodeActionKind.SourceOrganizeImports);

        if (organizeAction.Edit?.Changes is not null &&
            organizeAction.Edit.Changes.Count > 0)
        {
            var text = organizeAction.Edit.Changes.Values.First().First().NewText;
            var aaaIdx = text.IndexOf("aaa", StringComparison.Ordinal);
            var mmmIdx = text.IndexOf("mmm", StringComparison.Ordinal);
            var zzzIdx = text.IndexOf("zzz", StringComparison.Ordinal);

            aaaIdx.Should().BeLessThan(mmmIdx);
            mmmIdx.Should().BeLessThan(zzzIdx);
        }
    }
}
