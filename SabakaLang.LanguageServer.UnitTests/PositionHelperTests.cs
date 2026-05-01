using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SabakaLang.Compiler;
using SabakaLang.Compiler.AST;
using SabakaLang.LanguageServer;
using LspPosition = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;
using CompilerPosition = SabakaLang.Compiler.Lexing.Position;

namespace SabakaLang.LanguageServer.UnitTests;

public class PositionHelperTests
{
    [Theory]
    [InlineData(1, 1, 0, 0)]  // compiler line 1 col 1 → lsp line 0 col 0
    [InlineData(1, 5, 0, 4)]
    [InlineData(3, 2, 2, 1)]
    public void ToLsp_ConvertsCorrectly(int compilerLine, int compilerCol, int lspLine, int lspCol)
    {
        var pos = new CompilerPosition(compilerLine, compilerCol, 0);
        var lsp = PositionHelper.ToLsp(pos);
        lsp.Line.Should().Be(lspLine);
        lsp.Character.Should().Be(lspCol);
    }

    [Theory]
    [InlineData("hello world", 0, 0, 0)]   // line 0, col 0 → offset 0
    [InlineData("hello world", 0, 5, 5)]   // col 5 → offset 5
    [InlineData("hello\nworld", 1, 0, 6)]  // line 1, col 0 → offset 6
    [InlineData("hello\nworld", 1, 3, 9)]  // line 1, col 3 → offset 9
    public void ToOffset_ConvertsCorrectly(string source, int line, int col, int expectedOffset)
    {
        var pos = new LspPosition(line, col);
        var offset = PositionHelper.ToOffset(source, pos);
        offset.Should().Be(expectedOffset);
    }

    [Theory]
    [InlineData("int x = 5;", 0, 4, "x")]       // cursor on 'x'
    [InlineData("int myVar = 0;", 0, 6, "myVar")] // cursor in middle of identifier
    [InlineData("x + y", 0, 4, "y")]              // cursor on 'y'
    [InlineData("int x = 5;", 0, 3, "int")]       // cursor on keyword
    public void WordAt_ReturnsIdentifier(string source, int line, int col, string expected)
    {
        var pos = new LspPosition(line, col);
        var word = PositionHelper.WordAt(source, pos);
        word.Should().Be(expected);
    }

    [Theory]
    [InlineData("x + y", 0, 2, null)] // cursor on '+'
    [InlineData("", 0, 0, null)]      // empty source
    public void WordAt_NoIdentifier_ReturnsNull(string source, int line, int col, string? expected)
    {
        var pos = new LspPosition(line, col);
        var word = PositionHelper.WordAt(source, pos);
        word.Should().Be(expected);
    }

    [Fact]
    public void OffsetToLsp_SingleLine()
    {
        var source = "hello world";
        var lsp = PositionHelper.OffsetToLsp(source, 6);
        lsp.Line.Should().Be(0);
        lsp.Character.Should().Be(6);
    }

    [Fact]
    public void OffsetToLsp_MultiLine()
    {
        var source = "hello\nworld";
        var lsp = PositionHelper.OffsetToLsp(source, 8); // 'r' in world
        lsp.Line.Should().Be(1);
        lsp.Character.Should().Be(2);
    }

    [Fact]
    public void ToLspRange_FromSpan_CorrectRange()
    {
        var span = new Span(
            new CompilerPosition(1, 1, 0),
            new CompilerPosition(1, 5, 4));
        var range = PositionHelper.ToLspRange(span);
        range.Start.Line.Should().Be(0);
        range.Start.Character.Should().Be(0);
        range.End.Line.Should().Be(0);
        range.End.Character.Should().Be(4);
    }
}
