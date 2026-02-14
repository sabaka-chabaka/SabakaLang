using System.Text;
using SabakaLang.Exceptions;

namespace SabakaLang.Lexer;

public class Lexer
{
    private readonly string _text;
    private int _position;

    public Lexer(string text)
    {
        _text = text;
    }
    
    private char Current => _position >= _text.Length ? '\0' : _text[_position];
    
    private void Advance() => _position++;

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();
        while (Current != '\0')
        {
            if (char.IsWhiteSpace(Current))
            {
                Advance();
                continue;
            }

            if (char.IsDigit(Current))
            {
                tokens.Add(ReadNumber());
                continue;
            }
            
            if (char.IsLetter(Current))
            {
                tokens.Add(ReadIdentifier());
                continue;
            }


            switch (Current)
            {
                case '+':
                    tokens.Add(new Token(TokenType.Plus));
                    break;
                case '-':
                    tokens.Add(new Token(TokenType.Minus));
                    break;
                case '*':
                    tokens.Add(new Token(TokenType.Star));
                    break;
                case '/':
                    tokens.Add(new Token(TokenType.Slash));
                    break;
                case '(':
                    tokens.Add(new Token(TokenType.LParen));
                    break;
                case ')':
                    tokens.Add(new Token(TokenType.RParen));
                    break;
                case ';':
                    tokens.Add(new Token(TokenType.Semicolon));
                    break;
                case '=':
                    tokens.Add(new Token(TokenType.Equal));
                    break;
                case '{':
                    tokens.Add(new Token(TokenType.LBrace));
                    break;
                case '}':
                    tokens.Add(new Token(TokenType.RBrace));
                    break;

                default:
                    throw new LexerException($"Unexpected character '{Current}'", _position);
            }
            
            Advance();
        }
        
        tokens.Add(new Token(TokenType.EOF));
        return tokens;
    }

    private Token ReadNumber()
    {
        var sb = new StringBuilder();

        while (char.IsDigit(Current))
        {
            sb.Append(Current);
            Advance();
        }
        
        return new Token(TokenType.Number, sb.ToString());
    }
    
    private Token ReadIdentifier()
    {
        var sb = new StringBuilder();

        while (char.IsLetterOrDigit(Current))
        {
            sb.Append(Current);
            Advance();
        }

        var text = sb.ToString();

        return text switch
        {
            "bool" => new Token(TokenType.BoolKeyword, text),
            "true" => new Token(TokenType.True, text),
            "false" => new Token(TokenType.False, text),
            "if" => new Token(TokenType.If, text),
            "else" => new Token(TokenType.Else, text),
            _ => new Token(TokenType.Identifier, text)
        };
    }
}