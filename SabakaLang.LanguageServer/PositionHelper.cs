using LspPosition = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using CompilerPosition = SabakaLang.Compiler.Lexing.Position;
using CompilerSpan = SabakaLang.Compiler.AST.Span;

namespace SabakaLang.LanguageServer;

public static class PositionHelper
{
    public static LspPosition ToLsp(CompilerPosition p) =>
        new(Math.Max(0, p.Line - 1), Math.Max(0, p.Column - 1));

    public static LspRange ToLspRange(CompilerSpan span) =>
        new(ToLsp(span.Start), ToLsp(span.End));

    public static LspRange ToLspRange(CompilerPosition start, CompilerPosition end) =>
        new(ToLsp(start), ToLsp(end));

    public static int ToOffset(string source, LspPosition position)
    {
        var line = 0;
        var offset = 0;
        while (offset < source.Length && line < position.Line)
        {
            if (source[offset] == '\n') line++;
            offset++;
        }
        var col = 0;
        while (offset < source.Length && col < position.Character && source[offset] != '\n')
        {
            col++;
            offset++;
        }
        return offset;
    }

    public static CompilerPosition ToCompiler(LspPosition p) =>
        new(p.Line + 1, p.Character + 1, 0);

    public static LspPosition OffsetToLsp(string source, int offset)
    {
        var line = 0;
        var col = 0;
        for (var i = 0; i < offset && i < source.Length; i++)
        {
            if (source[i] == '\n') { line++; col = 0; }
            else col++;
        }
        return new LspPosition(line, col);
    }

    public static string? WordAt(string source, LspPosition position)
    {
        var offset = ToOffset(source, position);
        if (offset >= source.Length) return null;

        var start = offset;
        while (start > 0 && IsIdentChar(source[start - 1])) start--;

        var end = offset;
        while (end < source.Length && IsIdentChar(source[end])) end++;

        if (start == end) return null;
        return source[start..end];
    }

    private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}