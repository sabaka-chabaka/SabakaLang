using SabakaLang.AST;
using SabakaLang.Lexer.Enums;

namespace SabakaLang.Parser.Tests;

public class StatementTests
{
    [Fact]
    public void Parse_IfStmt_Simple()
    {
        var stmt = Utils.ParseSingleStmt<IfStmt>("if (x > 0) { return true; }");
        Assert.IsType<BinaryExpr>(stmt.Condition);
        Assert.Single(stmt.Then);
        Assert.Null(stmt.Else);
    }

    [Fact]
    public void Parse_IfStmt_SingleStatementBodyWithoutBraces()
    {
        var stmt = Utils.ParseSingleStmt<IfStmt>("if (flag) return;");
        Assert.IsType<NameExpr>(stmt.Condition);
        Assert.Single(stmt.Then);
        Assert.IsType<ReturnStmt>(stmt.Then[0]);
        Assert.Null(stmt.Else);
    }

    [Fact]
    public void Parse_IfStmt_WithElse()
    {
        var stmt = Utils.ParseSingleStmt<IfStmt>("if (x > 0) { return 1; } else { return -1; }");
        Assert.Single(stmt.Then);
        Assert.NotNull(stmt.Else);
        Assert.Single(stmt.Else);
    }

    [Fact]
    public void Parse_IfStmt_ElseIfChain()
    {
        var source = @"
            if (a == 1) {
                return 10;
            } else if (a == 2) {
                return 20;
            } else {
                return 30;
            }
        ";
        var stmt = Utils.ParseSingleStmt<IfStmt>(source);
        Assert.Single(stmt.Then);
        Assert.NotNull(stmt.Else);
        Assert.Single(stmt.Else);
        var elseIf = Assert.IsType<IfStmt>(stmt.Else[0]);
        Assert.Single(elseIf.Then);
        Assert.NotNull(elseIf.Else);
    }

    [Fact]
    public void Parse_WhileStmt()
    {
        var stmt = Utils.ParseSingleStmt<WhileStmt>("while (count > 0) { count--; }");
        Assert.IsType<BinaryExpr>(stmt.Condition);
        Assert.Single(stmt.Body);
        var exprStmt = Assert.IsType<ExprStmt>(stmt.Body[0]);
        Assert.IsType<AssignExpr>(exprStmt.Expr);
    }

    [Fact]
    public void Parse_ForStmt_Standard()
    {
        var stmt = Utils.ParseSingleStmt<ForStmt>("for (int i = 0; i < 10; i++) { sum += i; }");
        var init = Assert.IsType<VarDecl>(stmt.Init);
        Assert.Equal("i", init.Name);
        var cond = Assert.IsType<BinaryExpr>(stmt.Condition);
        Assert.Equal(TokenType.Less, cond.Op);
        var step = Assert.IsType<AssignExpr>(stmt.Step);
        Assert.Equal("i", Assert.IsType<NameExpr>(step.Target).Name);
        Assert.Single(stmt.Body);
    }

    [Fact]
    public void Parse_ForStmt_EmptyClauses()
    {
        var stmt = Utils.ParseSingleStmt<ForStmt>("for (;;) { break; }");
        Assert.Null(stmt.Init);
        Assert.Null(stmt.Condition);
        Assert.Null(stmt.Step);
        Assert.Single(stmt.Body);
    }

    [Fact]
    public void Parse_ForStmt_WithExprInit()
    {
        var stmt = Utils.ParseSingleStmt<ForStmt>("for (i = 0; i < 5; i++) { }");
        var init = Assert.IsType<ExprStmt>(stmt.Init);
        Assert.IsType<AssignExpr>(init.Expr);
        Assert.NotNull(stmt.Condition);
        Assert.NotNull(stmt.Step);
    }

    [Fact]
    public void Parse_ForeachStmt()
    {
        var stmt = Utils.ParseSingleStmt<ForeachStmt>("foreach (string item in items) { print(item); }");
        Assert.Equal("string", stmt.ItemType.Name);
        Assert.Equal("item", stmt.ItemName);
        Assert.Equal("items", Assert.IsType<NameExpr>(stmt.Collection).Name);
        Assert.Single(stmt.Body);
    }

    [Fact]
    public void Parse_SwitchStmt_CasesAndDefault()
    {
        var source = @"
            switch (op) {
                case 1:
                    return 10;
                case 2: {
                    return 20;
                }
                default:
                    return 0;
            }
        ";
        var stmt = Utils.ParseSingleStmt<SwitchStmt>(source);
        Assert.Equal("op", Assert.IsType<NameExpr>(stmt.Value).Name);
        Assert.Equal(3, stmt.Cases.Count);

        Assert.Equal(1, Assert.IsType<IntLit>(stmt.Cases[0].Value).Value);
        Assert.Single(stmt.Cases[0].Body);

        Assert.Equal(2, Assert.IsType<IntLit>(stmt.Cases[1].Value).Value);
        Assert.Single(stmt.Cases[1].Body);

        Assert.Null(stmt.Cases[2].Value); // default case
        Assert.Single(stmt.Cases[2].Body);
    }

    [Fact]
    public void Parse_ReturnStmt_WithValue()
    {
        var stmt = Utils.ParseSingleStmt<ReturnStmt>("return 42;");
        Assert.NotNull(stmt.Value);
        Assert.Equal(42, Assert.IsType<IntLit>(stmt.Value).Value);
    }

    [Fact]
    public void Parse_ReturnStmt_Empty()
    {
        var stmt = Utils.ParseSingleStmt<ReturnStmt>("return;");
        Assert.Null(stmt.Value);
    }

    [Fact]
    public void Parse_ImportStmt_Simple()
    {
        var stmt = Utils.ParseSingleStmt<ImportStmt>("import \"std/io\";");
        Assert.Equal("std/io", stmt.Path);
        Assert.Empty(stmt.Names);
        Assert.Null(stmt.Alias);
    }

    [Fact]
    public void Parse_ImportStmt_WithFrom()
    {
        var stmt = Utils.ParseSingleStmt<ImportStmt>("import \"math\" from sin, cos;");
        Assert.Equal("math", stmt.Path);
        Assert.Equal(new[] { "sin", "cos" }, stmt.Names);
        Assert.Null(stmt.Alias);
    }

    [Fact]
    public void Parse_ImportStmt_WithAlias()
    {
        var stmt = Utils.ParseSingleStmt<ImportStmt>("import \"network\" as net;");
        Assert.Equal("network", stmt.Path);
        Assert.Empty(stmt.Names);
        Assert.Equal("net", stmt.Alias);
    }

    [Fact]
    public void Parse_ImportStmt_WithFromAndAlias()
    {
        var stmt = Utils.ParseSingleStmt<ImportStmt>("import \"utils\" from helper as h;");
        Assert.Equal("utils", stmt.Path);
        Assert.Equal(new[] { "helper" }, stmt.Names);
        Assert.Equal("h", stmt.Alias);
    }
}
