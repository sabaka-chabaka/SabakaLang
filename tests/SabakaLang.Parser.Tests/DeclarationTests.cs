using SabakaLang.AST;
using SabakaLang.Lexer.Enums;

namespace SabakaLang.Parser.Tests;

public class DeclarationTests
{
    [Theory]
    [InlineData("int x;", "int", "x", false, false)]
    [InlineData("float y;", "float", "y", false, false)]
    [InlineData("string s;", "string", "s", false, false)]
    [InlineData("bool b;", "bool", "b", false, false)]
    [InlineData("char c;", "char", "c", false, false)]
    [InlineData("int[] arr;", "int", "arr", true, false)]
    [InlineData("string? opt;", "string", "opt", false, true)]
    [InlineData("MyType custom;", "MyType", "custom", false, false)]
    public void Parse_VarDecl_Types(string source, string expectedType, string expectedName, bool isArray, bool isNullable)
    {
        var decl = Utils.ParseSingleStmt<VarDecl>(source);
        Assert.Equal(expectedType, decl.Type.Name);
        Assert.Equal(expectedName, decl.Name);
        Assert.Equal(isArray, decl.Type.IsArray);
        Assert.Equal(isNullable, decl.Type.IsNullable);
        Assert.Null(decl.Init);
        Assert.Equal(AccessMod.Public, decl.Access);
    }

    [Fact]
    public void Parse_VarDecl_WithGenericType()
    {
        var decl = Utils.ParseSingleStmt<VarDecl>("List<int> list;");
        Assert.Equal("List", decl.Type.Name);
        Assert.Equal(new[] { "int" }, decl.Type.TypeArgs);
        Assert.Equal("list", decl.Name);
    }

    [Fact]
    public void Parse_VarDecl_WithInit()
    {
        var decl = Utils.ParseSingleStmt<VarDecl>("int count = 42;");
        Assert.Equal("int", decl.Type.Name);
        Assert.Equal("count", decl.Name);
        Assert.NotNull(decl.Init);
        Assert.Equal(42, Assert.IsType<IntLit>(decl.Init).Value);
    }

    [Theory]
    [InlineData("public int a;", AccessMod.Public)]
    [InlineData("private int b;", AccessMod.Private)]
    [InlineData("protected int c;", AccessMod.Protected)]
    public void Parse_VarDecl_AccessModifiers(string source, AccessMod expectedAccess)
    {
        var decl = Utils.ParseSingleStmt<VarDecl>(source);
        Assert.Equal(expectedAccess, decl.Access);
    }

    [Fact]
    public void Parse_ConstDecl_Simple()
    {
        var decl = Utils.ParseSingleStmt<ConstDecl>("const int MAX = 100;");
        Assert.Equal("int", decl.Type.Name);
        Assert.Equal("MAX", decl.Name);
        Assert.Equal(100, Assert.IsType<IntLit>(decl.Value).Value);
        Assert.Equal(AccessMod.Public, decl.Access);

        var varDecl = decl.ToVarDecl();
        Assert.True(varDecl.IsConst);
        Assert.Equal("MAX", varDecl.Name);
    }

    [Fact]
    public void Parse_ConstDecl_WithAccessModifier()
    {
        var decl = Utils.ParseSingleStmt<ConstDecl>("private const string API_URL = \"https://api.test\";");
        Assert.Equal("string", decl.Type.Name);
        Assert.Equal("API_URL", decl.Name);
        Assert.Equal("https://api.test", Assert.IsType<StringLit>(decl.Value).Value);
        Assert.Equal(AccessMod.Private, decl.Access);
    }

    [Fact]
    public void Parse_FuncDecl_Standard()
    {
        var decl = Utils.ParseSingleStmt<FuncDecl>("int add(int a, int b) { return a + b; }");
        Assert.Equal("int", decl.ReturnType.Name);
        Assert.Equal("add", decl.Name);
        Assert.False(decl.IsOverride);
        Assert.Equal(AccessMod.Public, decl.Access);
        Assert.Empty(decl.TypeParams);
        Assert.Equal(2, decl.Params.Count);
        Assert.Equal("int", decl.Params[0].Type.Name);
        Assert.Equal("a", decl.Params[0].Name);
        Assert.Equal("int", decl.Params[1].Type.Name);
        Assert.Equal("b", decl.Params[1].Name);
        Assert.Single(decl.Body);
        var ret = Assert.IsType<ReturnStmt>(decl.Body[0]);
        Assert.IsType<BinaryExpr>(ret.Value);
    }

    [Fact]
    public void Parse_FuncDecl_VoidKeyword()
    {
        var decl = Utils.ParseSingleStmt<FuncDecl>("void printMessage(string msg) { }");
        Assert.Equal("void", decl.ReturnType.Name);
        Assert.Equal("printMessage", decl.Name);
        Assert.Single(decl.Params);
        Assert.Empty(decl.Body);
    }

    [Fact]
    public void Parse_FuncDecl_FuncKeyword_ReturnsVoid()
    {
        var decl = Utils.ParseSingleStmt<FuncDecl>("func run() { }");
        Assert.Equal("void", decl.ReturnType.Name);
        Assert.Equal("run", decl.Name);
        Assert.Empty(decl.Params);
        Assert.Empty(decl.Body);
    }

    [Fact]
    public void Parse_FuncDecl_WithGenerics()
    {
        var decl = Utils.ParseSingleStmt<FuncDecl>("T identity<T>(T val) { return val; }");
        Assert.Equal("T", decl.ReturnType.Name);
        Assert.Equal("identity", decl.Name);
        Assert.Single(decl.TypeParams);
        Assert.Equal("T", decl.TypeParams[0].Name);
        Assert.Single(decl.Params);
        Assert.Equal("T", decl.Params[0].Type.Name);
        Assert.Equal("val", decl.Params[0].Name);
    }

    [Fact]
    public void Parse_FuncDecl_WithAccessAndOverride()
    {
        var decl = Utils.ParseSingleStmt<FuncDecl>("protected override void render() { }");
        Assert.Equal(AccessMod.Protected, decl.Access);
        Assert.True(decl.IsOverride);
        Assert.Equal("render", decl.Name);
    }
}
