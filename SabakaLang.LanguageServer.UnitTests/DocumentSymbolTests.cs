using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SabakaLang.LanguageServer;
using SabakaLang.LanguageServer.Handlers;

namespace SabakaLang.LanguageServer.UnitTests;

public class DocumentSymbolTests
{
    private static (DocumentStore, DocumentSymbolHandler) Setup(string uri, string source)
    {
        var store = new DocumentStore();
        store.Analyze(uri, source);
        return (store, new DocumentSymbolHandler(store));
    }

    private static DocumentSymbolParams Params(string uri) =>
        new() { TextDocument = new TextDocumentIdentifier { Uri = uri } };

    [Fact]
    public async Task DocumentSymbols_Class_AppearsInOutline()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "class Dog { int age; void bark() {} }");

        var result = await handler.Handle(Params(uri), CancellationToken.None);

        result.Should().NotBeNull();
        var syms = result!.Select(s => s.DocumentSymbol!).ToList();
        syms.Should().Contain(s => s.Name == "Dog" && s.Kind == SymbolKind.Class);
    }

    [Fact]
    public async Task DocumentSymbols_Class_HasFieldAndMethodChildren()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "class Cat { string name; void meow() {} }");

        var result = await handler.Handle(Params(uri), CancellationToken.None);

        var classSymbol = result!.Select(s => s.DocumentSymbol!).First(s => s.Name == "Cat");
        classSymbol.Children.Should().Contain(c => c.Name == "name");
        classSymbol.Children.Should().Contain(c => c.Name == "meow");
    }

    [Fact]
    public async Task DocumentSymbols_Enum_AppearsWithMembers()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "enum Direction { North, South, East, West }");

        var result = await handler.Handle(Params(uri), CancellationToken.None);

        var enumSym = result!.Select(s => s.DocumentSymbol!).First(s => s.Name == "Direction");
        enumSym.Kind.Should().Be(SymbolKind.Enum);
        enumSym.Children.Should().HaveCount(4);
        enumSym.Children.Should().Contain(c => c.Name == "North");
    }

    [Fact]
    public async Task DocumentSymbols_Interface_AppearsInOutline()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "interface IFly { void fly(); }");

        var result = await handler.Handle(Params(uri), CancellationToken.None);

        var sym = result!.Select(s => s.DocumentSymbol!).First(s => s.Name == "IFly");
        sym.Kind.Should().Be(SymbolKind.Interface);
        sym.Children.Should().Contain(c => c.Name == "fly");
    }

    [Fact]
    public async Task DocumentSymbols_Struct_AppearsInOutline()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "struct Point { int x; int y; }");

        var result = await handler.Handle(Params(uri), CancellationToken.None);

        var sym = result!.Select(s => s.DocumentSymbol!).First(s => s.Name == "Point");
        sym.Kind.Should().Be(SymbolKind.Struct);
        sym.Children.Should().HaveCount(2);
    }

    [Fact]
    public async Task DocumentSymbols_TopLevelFunction_AppearsInOutline()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "int compute(int x, int y) { return x + y; }");

        var result = await handler.Handle(Params(uri), CancellationToken.None);

        var sym = result!.Select(s => s.DocumentSymbol!).FirstOrDefault(s => s.Name == "compute");
        sym.Should().NotBeNull();
        sym!.Kind.Should().Be(SymbolKind.Method); // FuncDecl maps to Method
    }

    [Fact]
    public async Task DocumentSymbols_TopLevelVariable_AppearsInOutline()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri, "int globalCount = 0;");

        var result = await handler.Handle(Params(uri), CancellationToken.None);

        var sym = result!.Select(s => s.DocumentSymbol!).FirstOrDefault(s => s.Name == "globalCount");
        sym.Should().NotBeNull();
        sym!.Kind.Should().Be(SymbolKind.Variable);
    }

    [Fact]
    public async Task DocumentSymbols_DocumentNotFound_ReturnsNull()
    {
        var store = new DocumentStore();
        var handler = new DocumentSymbolHandler(store);

        var result = await handler.Handle(
            new DocumentSymbolParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = "file:///missing.sabaka" }
            },
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task DocumentSymbols_MultipleClasses_AllAppear()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri,
            "class A {}\nclass B {}\nclass C {}");

        var result = await handler.Handle(Params(uri), CancellationToken.None);

        var names = result!.Select(s => s.DocumentSymbol!.Name).ToList();
        names.Should().Contain("A");
        names.Should().Contain("B");
        names.Should().Contain("C");
    }

    [Fact]
    public async Task DocumentSymbols_MethodDetail_ContainsReturnType()
    {
        var uri = "file:///test.sabaka";
        var (_, handler) = Setup(uri,
            "class Calc { int add(int a, int b) { return a + b; } }");

        var result = await handler.Handle(Params(uri), CancellationToken.None);

        var calcSym = result!.Select(s => s.DocumentSymbol!).First(s => s.Name == "Calc");
        var addMethod = calcSym.Children!.First(c => c.Name == "add");
        addMethod.Detail.Should().Contain("int");
    }
}
