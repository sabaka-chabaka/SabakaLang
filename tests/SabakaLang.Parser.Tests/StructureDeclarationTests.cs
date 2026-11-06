using SabakaLang.AST;

namespace SabakaLang.Parser.Tests;

public class StructureDeclarationTests
{
    [Fact]
    public void Parse_ClassDecl_Empty()
    {
        var decl = Utils.ParseSingleStmt<ClassDecl>("class Empty { }");
        Assert.Equal("Empty", decl.Name);
        Assert.Empty(decl.TypeParams);
        Assert.Null(decl.Base);
        Assert.Empty(decl.Interfaces);
        Assert.Empty(decl.Fields);
        Assert.Empty(decl.Methods);
    }

    [Fact]
    public void Parse_ClassDecl_WithGenericAndInheritance()
    {
        var source = "class MyList<T> : BaseList, IEnumerable, IDisposable { }";
        var decl = Utils.ParseSingleStmt<ClassDecl>(source);
        Assert.Equal("MyList", decl.Name);
        Assert.Single(decl.TypeParams);
        Assert.Equal("T", decl.TypeParams[0].Name);
        Assert.Equal("BaseList", decl.Base);
        Assert.Equal(new[] { "IEnumerable", "IDisposable" }, decl.Interfaces);
    }

    [Fact]
    public void Parse_ClassDecl_WithMembers()
    {
        var source = @"
            class Service {
                public int id;
                const string Tag = ""SVC"";

                public void start() { }
                private int getId() { return id; }
            }
        ";
        var decl = Utils.ParseSingleStmt<ClassDecl>(source);
        Assert.Equal("Service", decl.Name);
        Assert.Equal(2, decl.Fields.Count);
        Assert.Equal("id", decl.Fields[0].Name);
        Assert.False(decl.Fields[0].IsConst);
        Assert.Equal("Tag", decl.Fields[1].Name);
        Assert.True(decl.Fields[1].IsConst);
        Assert.Equal(2, decl.Methods.Count);
        Assert.Equal("start", decl.Methods[0].Name);
        Assert.Equal("getId", decl.Methods[1].Name);
    }

    [Fact]
    public void Parse_InterfaceDecl_Empty()
    {
        var decl = Utils.ParseSingleStmt<InterfaceDecl>("interface IRunner { }");
        Assert.Equal("IRunner", decl.Name);
        Assert.Empty(decl.TypeParams);
        Assert.Empty(decl.Parents);
        Assert.Empty(decl.Methods);
    }

    [Fact]
    public void Parse_InterfaceDecl_WithGenericsAndInheritance()
    {
        var source = "interface IRepository<T> : IReadRepo, IWriteRepo { }";
        var decl = Utils.ParseSingleStmt<InterfaceDecl>(source);
        Assert.Equal("IRepository", decl.Name);
        Assert.Single(decl.TypeParams);
        Assert.Equal("T", decl.TypeParams[0].Name);
        Assert.Equal(new[] { "IReadRepo", "IWriteRepo" }, decl.Parents);
    }

    [Fact]
    public void Parse_InterfaceDecl_WithMethods()
    {
        var source = @"
            interface ICalculator {
                int add(int a, int b);
                void reset();
            }
        ";
        var decl = Utils.ParseSingleStmt<InterfaceDecl>(source);
        Assert.Equal("ICalculator", decl.Name);
        Assert.Equal(2, decl.Methods.Count);
        Assert.Equal("int", decl.Methods[0].ReturnType.Name);
        Assert.Equal("add", decl.Methods[0].Name);
        Assert.Equal(2, decl.Methods[0].Params.Count);
        Assert.Equal("void", decl.Methods[1].ReturnType.Name);
        Assert.Equal("reset", decl.Methods[1].Name);
        Assert.Empty(decl.Methods[1].Params);
    }

    [Fact]
    public void Parse_StructDecl_Empty()
    {
        var decl = Utils.ParseSingleStmt<StructDecl>("struct Empty { }");
        Assert.Equal("Empty", decl.Name);
        Assert.Empty(decl.Fields);
    }

    [Fact]
    public void Parse_StructDecl_WithFields()
    {
        var source = @"
            struct Vector2 {
                float x;
                float y;
            }
        ";
        var decl = Utils.ParseSingleStmt<StructDecl>(source);
        Assert.Equal("Vector2", decl.Name);
        Assert.Equal(2, decl.Fields.Count);
        Assert.Equal("x", decl.Fields[0].Name);
        Assert.Equal("float", decl.Fields[0].Type.Name);
        Assert.Equal("y", decl.Fields[1].Name);
        Assert.Equal("float", decl.Fields[1].Type.Name);
    }

    [Fact]
    public void Parse_EnumDecl_Empty()
    {
        var decl = Utils.ParseSingleStmt<EnumDecl>("enum Status { }");
        Assert.Equal("Status", decl.Name);
        Assert.Empty(decl.Members);
    }

    [Fact]
    public void Parse_EnumDecl_WithMembers()
    {
        var decl = Utils.ParseSingleStmt<EnumDecl>("enum Color { Red, Green, Blue }");
        Assert.Equal("Color", decl.Name);
        Assert.Equal(new[] { "Red", "Green", "Blue" }, decl.Members);
    }
}
