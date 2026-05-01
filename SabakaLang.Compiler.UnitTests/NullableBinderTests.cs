using SabakaLang.Compiler.AST;
using SabakaLang.Compiler.Binding;
using SabakaLang.Compiler.Lexing;
using SabakaLang.Compiler.Parsing;

namespace SabakaLang.Compiler.UnitTests;

public class NullableBinderTests
{
    private static BindResult Bind(string source)
    {
        var lexResult   = new Lexer(source).Tokenize();
        var parseResult = new Parser(lexResult).Parse();
        if (parseResult.HasErrors)
            throw new Exception("Parse errors:\n" +
                                string.Join("\n", parseResult.Errors.Select(e => e.Message)));
        return new Binder().Bind(parseResult.Statements);
    }

    private static BindResult BindNoThrow(string source)
    {
        var lexResult   = new Lexer(source).Tokenize();
        var parseResult = new Parser(lexResult).Parse();
        return new Binder().Bind(parseResult.Statements);
    }

    private static void AssertNoErrors(BindResult r) =>
        Assert.False(r.HasErrors, "Unexpected errors:\n" +
                                  string.Join("\n", r.Errors.Select(e => e.ToString())));

    private static void AssertNoWarnings(BindResult r) =>
        Assert.False(r.Warnings.Any(), "Unexpected warnings:\n" +
                                       string.Join("\n", r.Warnings.Select(w => w.ToString())));

    [Fact]
    public void TypeRef_NullableInt_IsNullable()
    {
        var lexResult = new Lexer("int? x;").Tokenize();
        var parseResult = new Parser(lexResult).Parse();
        Assert.False(parseResult.HasErrors);
        var decl = Assert.IsType<VarDecl>(parseResult.Statements[0]);
        Assert.True(decl.Type.IsNullable);
        Assert.Equal("int", decl.Type.Name);
    }

    [Fact]
    public void TypeRef_NonNullableInt_IsNotNullable()
    {
        var lexResult = new Lexer("int x;").Tokenize();
        var parseResult = new Parser(lexResult).Parse();
        Assert.False(parseResult.HasErrors);
        var decl = Assert.IsType<VarDecl>(parseResult.Statements[0]);
        Assert.False(decl.Type.IsNullable);
    }

    [Fact]
    public void TypeRef_NullableClass_IsNullable()
    {
        var lexResult = new Lexer("MyClass? obj;").Tokenize();
        var parseResult = new Parser(lexResult).Parse();
        Assert.False(parseResult.HasErrors);
        var decl = Assert.IsType<VarDecl>(parseResult.Statements[0]);
        Assert.True(decl.Type.IsNullable);
        Assert.Equal("MyClass", decl.Type.Name);
    }

    [Fact]
    public void TypeRef_NullableString_TypeStringInSymbolTable()
    {
        var r = Bind("string? s;");
        AssertNoErrors(r);
        var sym = Assert.Single(r.Symbols.Lookup("s"));
        Assert.Equal("string?", sym.Type);
    }

    [Fact]
    public void TypeRef_NonNullableString_TypeStringNoQuestion()
    {
        var r = Bind("string s;");
        AssertNoErrors(r);
        var sym = Assert.Single(r.Symbols.Lookup("s"));
        Assert.Equal("string", sym.Type);
    }

    [Fact]
    public void VarDecl_NullableInt_InitNull_NoWarning()
    {
        var r = Bind("int? x = null;");
        AssertNoErrors(r);
        AssertNoWarnings(r);
    }

    [Fact]
    public void VarDecl_NullableClass_InitNull_NoWarning()
    {
        var r = Bind("string? s = null;");
        AssertNoErrors(r);
        AssertNoWarnings(r);
    }

    [Fact]
    public void VarDecl_NonNullableInt_InitNull_Warning()
    {
        var r = BindNoThrow("int x = null;");
        Assert.True(r.Warnings.Any(), "Expected a nullable warning");
        Assert.Contains(r.Warnings, w => w.Message.Contains("x") && w.Message.Contains("non-nullable"));
    }

    [Fact]
    public void VarDecl_NonNullableString_InitNull_Warning()
    {
        var r = BindNoThrow("string s = null;");
        Assert.True(r.Warnings.Any());
        Assert.Contains(r.Warnings, w => w.Message.Contains("s"));
    }

    [Fact]
    public void VarDecl_NonNullableClass_InitNull_Warning()
    {
        var r = BindNoThrow("""
            class MyClass {}
            MyClass obj = null;
            """);
        Assert.True(r.Warnings.Any());
        Assert.Contains(r.Warnings, w => w.Message.Contains("obj"));
    }

    [Fact]
    public void VarDecl_NonNullableInt_InitValue_NoWarning()
    {
        var r = Bind("int x = 5;");
        AssertNoErrors(r);
        AssertNoWarnings(r);
    }

    [Fact]
    public void VarDecl_NonNullable_NoInit_NoWarning()
    {
        var r = Bind("int x;");
        AssertNoErrors(r);
        AssertNoWarnings(r);
    }

    [Fact]
    public void Assign_NonNullable_Null_Warning()
    {
        // int x = 0; x = null; — варн
        var r = BindNoThrow("int x = 0; x = null;");
        Assert.True(r.Warnings.Any());
        Assert.Contains(r.Warnings, w => w.Message.Contains("x"));
    }

    [Fact]
    public void Assign_Nullable_Null_NoWarning()
    {
        var r = Bind("int? x = 0; x = null;");
        AssertNoErrors(r);
        AssertNoWarnings(r);
    }

    [Fact]
    public void Assign_NonNullableString_Null_Warning()
    {
        var r = BindNoThrow("string s = \"hello\"; s = null;");
        Assert.True(r.Warnings.Any());
        Assert.Contains(r.Warnings, w => w.Message.Contains("s"));
    }

    [Fact]
    public void Assign_NonNullable_Value_NoWarning()
    {
        var r = Bind("int x = 0; x = 42;");
        AssertNoErrors(r);
        AssertNoWarnings(r);
    }

    [Fact]
    public void FuncParam_NullableType_RecordedInSymbol()
    {
        var r = Bind("void foo(int? x) {}");
        AssertNoErrors(r);
        var sym = Assert.Single(r.Symbols.Lookup("x"));
        Assert.Equal("int?", sym.Type);
    }

    [Fact]
    public void FuncParam_NonNullableType_RecordedInSymbol()
    {
        var r = Bind("void foo(int x) {}");
        AssertNoErrors(r);
        var sym = Assert.Single(r.Symbols.Lookup("x"));
        Assert.Equal("int", sym.Type);
    }
    
    [Fact]
    public void ClassField_NullableField_NoWarning()
    {
        var r = Bind("""
            class Foo {
                string? name = null;
            }
            """);
        AssertNoErrors(r);
        AssertNoWarnings(r);
    }

    [Fact]
    public void ClassField_NonNullableField_InitNull_Warning()
    {
        var r = BindNoThrow("""
            class Foo {
                string name = null;
            }
            """);
        Assert.True(r.Warnings.Any());
        Assert.Contains(r.Warnings, w => w.Message.Contains("name"));
    }
    
    [Fact]
    public void Warning_ContainsPosition()
    {
        var r = BindNoThrow("int x = null;");
        Assert.True(r.Warnings.Any());
        var w = r.Warnings[0];
        Assert.True(w.Position.Line > 0 || w.Position.Column >= 0);
    }
    
    [Fact]
    public void MultipleNullAssigns_MultipleWarnings()
    {
        var r = BindNoThrow("""
            int a = null;
            string b = null;
            bool c = null;
            """);
        Assert.True(r.Warnings.Count >= 3,
            $"Expected at least 3 warnings, got {r.Warnings.Count}");
    }

    [Fact]
    public void NullableVarsAmongNonNullable_OnlyNonNullableGetWarning()
    {
        var r = BindNoThrow("""
            int  a = null;
            int? b = null;
            int  c = null;
            """);
        Assert.Equal(2, r.Warnings.Count(w => w.Message.Contains("non-nullable")));
    }

    [Fact]
    public void NullAssign_StillNoBindError()
    {
        var r = BindNoThrow("int x = null;");
        Assert.False(r.HasErrors, "Null assign should be a warning, not an error");
        Assert.True(r.Warnings.Any());
    }
}