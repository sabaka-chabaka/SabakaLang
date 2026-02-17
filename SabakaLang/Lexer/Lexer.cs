using System.Text;
using SabakaLang.Exceptions;

namespace SabakaLang.Lexer;

public class Lexer
{
    private readonly string _text;
    private int _position;
    private bool _ide;

    public Lexer(string text)
    {
        _text = text;
        _ide = false;
    }
    
    private char Current => _position >= _text.Length ? '\0' : _text[_position];
    
    private void Advance() => _position++;

    public List<Token> Tokenize(bool isIde)
    {
        _ide = isIde;
        var tokens = new List<Token>();
        while (Current != '\0')
        {
            if (char.IsWhiteSpace(Current))
            {
                Advance();
                continue;
            }

            int start = _position;

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
                    tokens.Add(new Token(TokenType.Plus, "+", start, start + 1));
                    break;
                case '-':
                    tokens.Add(new Token(TokenType.Minus, "-", start, start + 1));
                    break;
                case '*':
                    tokens.Add(new Token(TokenType.Star, "*", start, start + 1));
                    break;
                case '/':
                    if (PeekChar() == '/')
                    {
                        while (Current != '\n' && Current != '\0')
                            Advance();
                        continue;
                    }
                    tokens.Add(new Token(TokenType.Slash, "/", start, start + 1));
                    break;
                case '(':
                    tokens.Add(new Token(TokenType.LParen, "(", start, start + 1));
                    break;
                case ')':
                    tokens.Add(new Token(TokenType.RParen, ")", start, start + 1));
                    break;
                case ';':
                    tokens.Add(new Token(TokenType.Semicolon, ";", start, start + 1));
                    break;
                case '{':
                    tokens.Add(new Token(TokenType.LBrace, "{", start, start + 1));
                    break;
                case '}':
                    tokens.Add(new Token(TokenType.RBrace, "}", start, start + 1));
                    break;
                case '=':
                    if (PeekChar() == '=')
                    {
                        Advance();
                        tokens.Add(new Token(TokenType.EqualEqual, "==", start, _position + 1));
                    }
                    else
                    {
                        tokens.Add(new Token(TokenType.Equal, "=", start, start + 1));
                    }
                    break;

                case '>':
                    if (PeekChar() == '=')
                    {
                        Advance();
                        tokens.Add(new Token(TokenType.GreaterEqual, ">=", start, _position + 1));
                    }
                    else
                    {
                        tokens.Add(new Token(TokenType.Greater, ">", start, start + 1));
                    }
                    break;

                case '<':
                    if (PeekChar() == '=')
                    {
                        Advance();
                        tokens.Add(new Token(TokenType.LessEqual, "<=", start, _position + 1));
                    }
                    else
                    {
                        tokens.Add(new Token(TokenType.Less, "<", start, start + 1));
                    }
                    break;
                
                case '&':
                {
                    if (PeekChar() == '&')
                    {
                        Advance();
                        tokens.Add(new Token(TokenType.AndAnd, "&&", start, _position + 1));
                    }
                    else
                    {
                        if (!isIde)
                            throw new LexerException("Unexpected character '&'", _position);
                    }
                    break;
                }


                case '|':
                {
                    if (PeekChar() == '|')
                    {
                        Advance();
                        tokens.Add(new Token(TokenType.OrOr, "||", start, _position + 1));
                    }
                    else
                    {
                        if (!isIde)
                            throw new LexerException("Unexpected character '|'", _position);
                    }
                    break;
                }


                case '!':
                {
                    if (PeekChar() == '=')
                    {
                        Advance(); // !
                        tokens.Add(new Token(TokenType.NotEqual, "!=", start, _position + 1));
                    }
                    else
                    {
                        tokens.Add(new Token(TokenType.Bang, "!", start, start + 1));
                    }
                    break;
                }

                case ',':
                {
                    tokens.Add(new Token(TokenType.Comma, ",", start, start + 1));
                    break;
                }

                case ':':
                {
                    if (PeekChar() == ':')
                    {
                        Advance();
                        tokens.Add(new Token(TokenType.ColonColon, "::", start, _position + 1));
                    }
                    else
                    {
                        tokens.Add(new Token(TokenType.Colon, ":", start, start + 1));
                    }
                    break;
                }

                case '.':
                {
                    tokens.Add(new Token(TokenType.Dot, ".", start, start + 1));
                    break;
                }
                
                case '[':
                    tokens.Add(new Token(TokenType.LBracket, "[", start, start + 1));
                    break;

                case ']':
                    tokens.Add(new Token(TokenType.RBracket, "]", start, start + 1));
                    break;

                
                default:
                    if (!isIde)
                        throw new LexerException($"Unexpected character '{Current}'", _position);
                    break;
            }
            
            Advance();
        }
        
        tokens.Add(new Token(TokenType.EOF, "", _position, _position));
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
                    if (!_ide)
                        throw new Exception("Invalid number format");

                hasDot = true;
            }

            Advance();
        }

        string text = _text.Substring(start, _position - start);

        if (hasDot)
            return new Token(TokenType.FloatLiteral, text, start, _position);
        else
            return new Token(TokenType.IntLiteral, text, start, _position);
    }

    
    private Token ReadIdentifier()
    {
        int start = _position;
        var sb = new StringBuilder();

        while (char.IsLetterOrDigit(Current) || Current == '_')
        {
            sb.Append(Current);
            Advance();
        }

        var text = sb.ToString();

        return text switch
        {
            "bool" => new Token(TokenType.BoolKeyword, text, start, _position),
            "true" => new Token(TokenType.True, text, start, _position),
            "false" => new Token(TokenType.False, text, start, _position),
            "if" => new Token(TokenType.If, text, start, _position),
            "else" => new Token(TokenType.Else, text, start, _position),
            "while" => new Token(TokenType.While, text, start, _position),
            "int" => new Token(TokenType.IntKeyword, text, start, _position),
            "float" => new Token(TokenType.FloatKeyword, text, start, _position),
            "string" => new Token(TokenType.StringKeyword, text, start, _position),
            "void" => new Token(TokenType.VoidKeyword, text, start, _position),
            "func" => new Token(TokenType.Function, text, start, _position),
            "return" => new Token(TokenType.Return, text, start, _position),
            "for" => new Token(TokenType.For, text, start, _position),
            "foreach" => new Token(TokenType.Foreach, text, start, _position),
            "in"  => new Token(TokenType.In, text, start, _position),
            "struct" => new Token(TokenType.StructKeyword, text, start, _position),
            "enum" => new Token(TokenType.Enum, text, start, _position),
            "class" => new Token(TokenType.Class, text, start, _position),
            "interface" => new Token(TokenType.Interface, text, start, _position),
            "new" => new Token(TokenType.New, text, start, _position),
            "override" => new Token(TokenType.Override, text, start, _position),
            "super" => new Token(TokenType.Super, text, start, _position),
            "switch" => new Token(TokenType.Switch, text, start, _position),
            "case" => new Token(TokenType.Case, text, start, _position),
            "default" => new Token(TokenType.Default, text, start, _position),
            "public" => new Token(TokenType.Public, text, start, _position),
            "private" => new Token(TokenType.Private, text, start, _position),
            "protected" => new Token(TokenType.Protected, text, start, _position),

            _ => new Token(TokenType.Identifier, text, start, _position)
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
        int start = _position;
        Advance();

        var sb = new StringBuilder();

        while (Current != '"' && Current != '\0')
        {
            sb.Append(Current);
            Advance();
        }

        if (Current != '"')
            if (!_ide)
                throw new LexerException("Unterminated string", _position);

        Advance();

        return new Token(TokenType.StringLiteral, sb.ToString(), start, _position);
    }

}