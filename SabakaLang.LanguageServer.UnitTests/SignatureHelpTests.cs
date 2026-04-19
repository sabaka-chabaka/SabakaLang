using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SabakaLang.LanguageServer;
using SabakaLang.LanguageServer.Handlers;
using LspPosition = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;

namespace SabakaLang.LanguageServer.UnitTests;

public class SignatureHelpTests
{
    private static (DocumentStore, SignatureHelpHandler) Setup(string uri, string source)
    {
        var store = new DocumentStore();
        store.Analyze(uri, source);
        return (store, new SignatureHelpHandler(store));
    }

    private static SignatureHelpParams Params(string uri, int line, int col) =>
        new()
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new LspPosition(line, col),
            Context = new SignatureHelpContext
            {
                TriggerKind = SignatureHelpTriggerKind.TriggerCharacter,
                TriggerCharacter = "("
            }
        };

    [Fact]
    public async Task SignatureHelp_SingleParamFunction_ShowsSignature()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri,
            "void greet(string name) {}\ngreet(");

        var result = await handler.Handle(Params(uri, 1, 6), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Signatures.Should().NotBeEmpty();
        result.Signatures.First().Label.Should().Contain("greet");
        result.Signatures.First().Label.Should().Contain("string name");
    }

    [Fact]
    public async Task SignatureHelp_MultipleParams_ShowsAllParams()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri,
            "int add(int a, int b) { return a + b; }\nadd(");

        var result = await handler.Handle(Params(uri, 1, 4), CancellationToken.None);

        result.Should().NotBeNull();
        var sig = result!.Signatures.First();
        sig.Parameters.Should().HaveCount(2);
        sig.Label.Should().Contain("int a");
        sig.Label.Should().Contain("int b");
    }

    [Fact]
    public async Task SignatureHelp_FirstArg_ActiveParameterIsZero()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri,
            "void test(int x, int y) {}\ntest(");

        var result = await handler.Handle(Params(uri, 1, 5), CancellationToken.None);

        result.Should().NotBeNull();
        result!.ActiveParameter.Should().Be(0);
    }

    [Fact]
    public async Task SignatureHelp_AfterComma_ActiveParameterIsOne()
    {
        var uri = "file:///test.sabaka";
        // cursor is after "test(1, " — second argument
        var (_, handler) = Setup(uri,
            "void test(int x, int y) {}\ntest(1, ");

        var result = await handler.Handle(Params(uri, 1, 8), CancellationToken.None);

        result.Should().NotBeNull();
        result!.ActiveParameter.Should().Be(1);
    }

    [Fact]
    public async Task SignatureHelp_Builtin_ShowsSignature()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "httpGet(");

        var result = await handler.Handle(Params(uri, 0, 8), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Signatures.Should().NotBeEmpty();
        result.Signatures.First().Label.Should().Contain("httpGet");
        result.Signatures.First().Label.Should().Contain("string url");
    }

    [Fact]
    public async Task SignatureHelp_NoOpenParen_ReturnsNull()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "int x = 5;");

        var result = await handler.Handle(Params(uri, 0, 5), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SignatureHelp_DocumentNotFound_ReturnsNull()
    {
        var store = new DocumentStore();
        var handler = new SignatureHelpHandler(store);

        var result = await handler.Handle(
            Params("file:///missing.sabaka", 0, 0),
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SignatureHelp_UnknownFunction_ReturnsNull()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "unknownFunc(");

        var result = await handler.Handle(Params(uri, 0, 12), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SignatureHelp_ReturnsTypeInSignature()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri,
            "string repeat(string s, int n) { return s; }\nrepeat(");

        var result = await handler.Handle(Params(uri, 1, 7), CancellationToken.None);

        result.Should().NotBeNull();
        var sig = result!.Signatures.First();
        sig.Label.Should().Contain("string");
        sig.Label.Should().Contain("repeat");
    }

    [Fact]
    public async Task SignatureHelp_NestedCall_CorrectArgIndex()
    {
        var uri = "file:///test.sabaka";
        // outer(inner(, — outer is at arg 0, inner's paren opens later
        var (_, handler) = Setup(uri,
            "void outer(int a, int b) {}\nvoid inner(int x) {}\nouter(inner(");

        var result = await handler.Handle(Params(uri, 2, 12), CancellationToken.None);

        result.Should().NotBeNull();
        // should be inner's signature, arg 0
        result!.Signatures.First().Label.Should().Contain("inner");
        result.ActiveParameter.Should().Be(0);
    }
}
