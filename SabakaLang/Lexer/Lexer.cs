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
            
            if (Current == '"')
            {
                tokens.Add(ReadString());
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
                case '{':
                    tokens.Add(new Token(TokenType.LBrace));
                    break;
                case '}':
                    tokens.Add(new Token(TokenType.RBrace));
                    break;
                case '=':
                    if (PeekChar() == '=')
                    {
                        Advance();
                        tokens.Add(new Token(TokenType.EqualEqual));
                    }
                    else
                    {
                        tokens.Add(new Token(TokenType.Equal));
                    }
                    break;

                case '>':
                    if (PeekChar() == '=')
                    {
                        Advance();
                        tokens.Add(new Token(TokenType.GreaterEqual));
                    }
                    else
                    {
                        tokens.Add(new Token(TokenType.Greater));
                    }
                    break;

                case '<':
                    if (PeekChar() == '=')
                    {
                        Advance();
                        tokens.Add(new Token(TokenType.LessEqual));
                    }
                    else
                    {
                        tokens.Add(new Token(TokenType.Less));
                    }
                    break;
                
                case '&':
                {
                    if (PeekChar() == '&')
                    {
                        Advance();
                        tokens.Add(new Token(TokenType.AndAnd, "&&"));
                    }
                    else
                    {
                        throw new LexerException("Unexpected character '&'", _position);
                    }
                    break;
                }


                case '|':
                {
                    if (PeekChar() == '|')
                    {
                        Advance();
                        tokens.Add(new Token(TokenType.OrOr, "||"));
                    }
                    else
                    {
                        throw new LexerException("Unexpected character '|'", _position);
                    }
                    break;
                }


                case '!':
                {
                    if (PeekChar() == '=')
                    {
                        Advance(); // !
                        tokens.Add(new Token(TokenType.NotEqual, "!="));
                    }
                    else
                    {
                        tokens.Add(new Token(TokenType.Bang, "!"));
                    }
                    break;
                }

                case ',':
                {
                    tokens.Add(new Token(TokenType.Comma, ","));
                    break;
                }
                
                case '[':
                    tokens.Add(new Token(TokenType.LBracket, "["));
                    break;

                case ']':
                    tokens.Add(new Token(TokenType.RBracket, "]"));
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
        int start = _position;
        bool hasDot = false;

        while (!IsAtEnd() && (char.IsDigit(Current) || Current == '.'))
        {
            if (Current == '.')
            {
                if (hasDot)
                    throw new Exception("Invalid number format");

                hasDot = true;
            }

            Advance();
        }

        string text = _text.Substring(start, _position - start);

        if (hasDot)
            return new Token(TokenType.FloatLiteral, text);
        else
            return new Token(TokenType.IntLiteral, text);
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
            "while" => new Token(TokenType.While, text),
            "int" => new Token(TokenType.IntKeyword, text),
            "float" => new Token(TokenType.FloatKeyword, text),
            "string" => new Token(TokenType.StringKeyword, text),
            "void" => new Token(TokenType.VoidKeyword, text),
            "return" => new Token(TokenType.Return, text),

            _ => new Token(TokenType.Identifier, text)
        };
    }
    
    private char PeekChar()
    {
        if (_position + 1 >= _text.Length)
            return '\0';

        return _text[_position + 1];
    }

    private bool IsAtEnd()
    {
        return _position >= _text.Length;
    }

    private Token ReadString()
    {
        Advance();

        var sb = new StringBuilder();

        while (Current != '"' && Current != '\0')
        {
            sb.Append(Current);
            Advance();
        }

        if (Current != '"')
            throw new LexerException("Unterminated string", _position);

        Advance();

        return new Token(TokenType.StringLiteral, sb.ToString());
    }

}