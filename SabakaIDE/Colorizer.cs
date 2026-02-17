using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using SabakaLang.Lexer;

namespace SabakaIDE;

public class Colorizer : DocumentColorizingTransformer
{
    private List<Token> _tokens = new();

    public void SetTokens(List<Token> tokens)
    {
        _tokens = tokens;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        int lineStart = line.Offset;
        int lineEnd = line.EndOffset;

        for (int i = 0; i < _tokens.Count; i++)
        {
            var token = _tokens[i];
            int tokenStart = token.TokenStart;
            int tokenEnd = token.TokenEnd;

            if (tokenEnd < lineStart || tokenStart > lineEnd)
                continue;

            TokenType type = token.Type;

            // Если за идентификатором идёт скобка — значит это функция
            if (type == TokenType.Identifier && i + 1 < _tokens.Count && _tokens[i + 1].Type == TokenType.LParen)
            {
                type = TokenType.Function;
            }

            ChangeLinePart(
                Math.Max(tokenStart, lineStart),
                Math.Min(tokenEnd, lineEnd),
                element =>
                {
                    if (token.Value == "print" || token.Value == "input")
                    {
                        element.TextRunProperties.SetForegroundBrush(
                            Brushes.DodgerBlue);
                        return;
                    }
                    element.TextRunProperties.SetForegroundBrush(
                        GetBrush(type));
                });
        }
    }

    private Brush GetBrush(TokenType type)
    {
        return type switch
        {
            TokenType.BoolKeyword => Brushes.DodgerBlue,
            TokenType.FloatKeyword => Brushes.DodgerBlue,
            TokenType.StringLiteral => Brushes.Orange,
            TokenType.IntKeyword => Brushes.DodgerBlue,
            TokenType.StructKeyword => Brushes.DodgerBlue,
            TokenType.StringKeyword => Brushes.DodgerBlue,
            TokenType.VoidKeyword => Brushes.DodgerBlue,
            TokenType.Class => Brushes.DodgerBlue,
            TokenType.Number => Brushes.PaleVioletRed,
            TokenType.IntLiteral => Brushes.PaleVioletRed,
            TokenType.FloatLiteral => Brushes.PaleVioletRed,
            TokenType.Super => Brushes.Green,
            TokenType.Identifier => Brushes.LightSkyBlue,
            TokenType.Function => Brushes.SpringGreen,
            TokenType.While => Brushes.DodgerBlue,
            TokenType.Else => Brushes.DodgerBlue,
            TokenType.If => Brushes.DodgerBlue,
            TokenType.True => Brushes.DodgerBlue,
            TokenType.False => Brushes.DodgerBlue,
            TokenType.Enum => Brushes.DodgerBlue,
            TokenType.Foreach => Brushes.DodgerBlue,
            TokenType.For => Brushes.DodgerBlue,
            TokenType.Return => Brushes.DodgerBlue,
            TokenType.Override => Brushes.DodgerBlue,
            TokenType.Public => Brushes.DodgerBlue,
            TokenType.Protected => Brushes.DodgerBlue,
            TokenType.Private => Brushes.DodgerBlue,
            TokenType.New => Brushes.DodgerBlue,
            TokenType.Interface => Brushes.DodgerBlue,
            
            _ => Brushes.White
        };
    }
}