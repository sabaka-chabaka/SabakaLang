using SabakaLang.Compiler;
using System.Collections.Generic;
using System.Linq;

namespace SabakaLang.Compiler.UnitTests;

public class CompilerCoverageTests
{
    private static BindResult BindNoThrow(string source)
    {
        var lexResult   = new Lexer(source).Tokenize();
        var parseResult = new Parser(lexResult).Parse();
        return new Binder().Bind(parseResult.Statements);
    }

    private static CompileResult CompileUnchecked(string source)
    {
        var lex   = new Lexer(source).Tokenize();
        var parse = new Parser(lex).Parse();
        var binder = new Binder();
        var bound = binder.Bind(parse.Statements);
        var comp  = new Compiler();
        return comp.Compile(parse.Statements, bound);
    }

    [Fact]
    public void Binder_ReturnOutsideFunction_Error()
    {
        var r = BindNoThrow("return 1;");
        Assert.True(r.HasErrors);
        Assert.Contains("outside a function", r.Errors[0].Message);
    }

    [Fact]
    public void Binder_ReturnVoidWithValue_Error()
    {
        var r = BindNoThrow("void foo() { return 1; }");
        Assert.True(r.HasErrors);
        Assert.Contains("Cannot return a value from a void function", r.Errors[0].Message);
    }

    [Fact]
    public void Binder_ReturnIntWithoutValue_Error()
    {
        var r = BindNoThrow("int foo() { return; }");
        Assert.True(r.HasErrors);
        Assert.Contains("Expected a return value of type 'int'", r.Errors[0].Message);
    }

    [Fact]
    public void Binder_UnknownTypeInIs_Error()
    {
        var r = BindNoThrow("var x = 1 is UnknownType;");
        Assert.True(r.HasErrors);
        Assert.Contains("Unknown type 'UnknownType'", r.Errors[0].Message);
    }

    [Fact]
    public void Binder_SuperOutsideClass_Error()
    {
        var r = BindNoThrow("super.foo();");
        Assert.True(r.HasErrors);
        Assert.Contains("'super' used outside a class", r.Errors[0].Message);
    }

    [Fact]
    public void Compiler_InvalidAssignmentTarget_Error()
    {
        // Actually, if we use a target that's not a NameExpr, MemberExpr or IndexExpr,
        // it SHOULD hit the default case.
        // Let's try to bypass Binder entirely by constructing the AST manually.
        var expr = new AssignExpr(new IntLit(1, default), new IntLit(2, default), default);
        var comp = new SabakaLang.Compiler.Compiler();
        // BindResult(SymbolTable table, IReadOnlyList<BindError> errors)
        var result = comp.Compile(new List<IStmt> { new ExprStmt(expr, default) }, new BindResult(new SymbolTable(), new List<BindError>()));
        Assert.True(result.HasErrors);
        Assert.Contains("Invalid assignment target", result.Errors[0].Message);
    }

    [Fact]
    public void Compiler_SuperOutsideClass_EmitError()
    {
        // Binder should catch this, but Compiler also has a check
        var lex = new Lexer("super;").Tokenize();
        var parse = new Parser(lex).Parse();
        var binder = new Binder();
        var bound = binder.Bind(parse.Statements);
        var comp = new Compiler();
        var r = comp.Compile(parse.Statements, bound);
        Assert.True(r.HasErrors);
        Assert.Contains("used outside a class", r.Errors.Any(e => e.Message.Contains("super")) ? r.Errors.First(e => e.Message.Contains("super")).Message : "");
    }

    [Fact]
    public void Compiler_NewExpr_NoConstructorArgs_Error()
    {
        var source = "class C {} var x = new C(1);";
        var r = CompileUnchecked(source);
        Assert.True(r.HasErrors);
        Assert.Contains("has no constructor but received arguments", r.Errors[0].Message);
    }

    [Fact]
    public void Parser_UnexpectedToken_Error()
    {
        var lex = new Lexer("if (true) { } else else { }").Tokenize();
        var parser = new Parser(lex);
        var result = parser.Parse();
        Assert.True(result.HasErrors);
        Assert.Contains("Unexpected token", result.Errors[0].Message);
    }

    [Fact]
    public void Parser_InterpolatedString_UnmatchedBrace_Error()
    {
        var lex = new Lexer("$\"{x\"").Tokenize();
        var parser = new Parser(lex);
        var result = parser.Parse();
        Assert.True(result.HasErrors);
        Assert.Contains("Unmatched '{'", result.Errors[0].Message);
    }

    [Fact]
    public void Parser_InterpolatedString_MultipleExpr_Error()
    {
        var lex = new Lexer("$\"{x; y}\"").Tokenize();
        var parser = new Parser(lex);
        var result = parser.Parse();
        Assert.True(result.HasErrors);
        Assert.Contains("Expected a single expression", result.Errors[0].Message);
    }
}
