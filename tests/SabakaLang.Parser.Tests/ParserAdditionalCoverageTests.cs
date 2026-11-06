using SabakaLang.AST;
using SabakaLang.Lexer.Enums;

namespace SabakaLang.Parser.Tests;

public class ParserAdditionalCoverageTests
{
    [Fact]
    public void Parse_NestedDeclarationsInsideBlock()
    {
        var source = @"
            void foo() {
                class Inner { }
                interface IInner { }
                struct InnerStruct { int x; }
                enum InnerEnum { A, B }
                import ""inner/mod"";
            }
        ";
        var func = Utils.ParseSingleStmt<FuncDecl>(source);
        Assert.Equal(5, func.Body.Count);
        Assert.IsType<ClassDecl>(func.Body[0]);
        Assert.IsType<InterfaceDecl>(func.Body[1]);
        Assert.IsType<StructDecl>(func.Body[2]);
        Assert.IsType<EnumDecl>(func.Body[3]);
        Assert.IsType<ImportStmt>(func.Body[4]);
    }

    [Fact]
    public void Parse_CustomTypeArray_VarDecl()
    {
        var stmt = Utils.ParseSingleStmt<VarDecl>("CustomType[] items;");
        Assert.Equal("CustomType", stmt.Type.Name);
        Assert.True(stmt.Type.IsArray);
    }

    [Fact]
    public void Parse_CustomTypeGenericArrayNullable_VarDecl()
    {
        var stmt = Utils.ParseSingleStmt<VarDecl>("List<string>[]? lists;");
        Assert.Equal("List", stmt.Type.Name);
        Assert.True(stmt.Type.IsArray);
        Assert.True(stmt.Type.IsNullable);
    }

    [Fact]
    public void Parse_InterpolatedString_UnclosedBrace_ReportsError()
    {
        var result = Utils.Parse("string s = $\"hello { world\";");
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void Parse_InterpolatedString_SubParserError_ReportsError()
    {
        var result = Utils.Parse("string s = $\"result: { 1 + }\";");
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void Parse_MultipleTypeParams_InFunctionAndClass()
    {
        var func = Utils.ParseSingleStmt<FuncDecl>("void map<TKey, TValue>(TKey k, TValue v) { }");
        Assert.Equal(2, func.TypeParams.Count);
        Assert.Equal("TKey", func.TypeParams[0].Name);
        Assert.Equal("TValue", func.TypeParams[1].Name);

        var cls = Utils.ParseSingleStmt<ClassDecl>("class Map<K, V> { }");
        Assert.Equal(2, cls.TypeParams.Count);
        Assert.Equal("K", cls.TypeParams[0].Name);
        Assert.Equal("V", cls.TypeParams[1].Name);
    }

    [Fact]
    public void Parse_InvalidTypeRef_ReportsError()
    {
        var result = Utils.Parse("x is +;");
        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Message.Contains("Expected type"));
    }
}
