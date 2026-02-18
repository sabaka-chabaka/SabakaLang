using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace SabakaLang.LSP;

public static class PositionHelper
{
    public static int GetOffset(string source, Position position)
    {
        int line = 0;
        int character = 0;
        int offset = 0;

        while (offset < source.Length && line < position.Line)
        {
            if (source[offset] == '\n')
            {
                line++;
            }
            offset++;
        }

        if (line < position.Line) return source.Length;

        while (offset < source.Length && character < position.Character)
        {
            if (source[offset] == '\n' || source[offset] == '\r') break;
            character++;
            offset++;
        }

        return offset;
    }

    public static Position GetPosition(string source, int offset)
    {
        if (offset < 0) offset = 0;
        if (offset > source.Length) offset = source.Length;

        int line = 0;
        int character = 0;
        for (int i = 0; i < offset; i++)
        {
            if (source[i] == '\n')
            {
                line++;
                character = 0;
            }
            else if (source[i] != '\r')
            {
                character++;
            }
        }
        return new Position(line, character);
    }

    public static OmniSharp.Extensions.LanguageServer.Protocol.Models.Range GetRange(string source, int start, int end)
    {
        return new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(GetPosition(source, start), GetPosition(source, end));
    }
}
