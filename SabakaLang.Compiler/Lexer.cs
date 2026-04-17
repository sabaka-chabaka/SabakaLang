using System.Text;

namespace SabakaLang.Compiler;

public enum TokenType
{
    Number,
    
    Plus,
    Minus,
    Star,
    Slash, 
    Percent,
    
    PlusEqual,
    MinusEqual,
    StarEqual,
    PlusPlus,
    MinusMinus,
    
    LParen,
    RParen,
    
    Semicolon,
    Identifier,
    
    BoolKeyword,
    True,
    False,
    Equal,
    
    If,
    Else,
    While,
    For,
    LBrace,
    RBrace,
    Enum,
    
    EqualEqual,
    NotEqual,
    Greater,
    Less,
    GreaterEqual,
    LessEqual,
    
    IntLiteral,
    FloatLiteral,
    IntKeyword,
    FloatKeyword,
    StringLiteral,
    InterpolatedStringLiteral,
    StringKeyword,

    Return,
    VoidKeyword,
    Comma,
    
    Foreach,
    In,
    Dot,
    StructKeyword,
    
    AndAnd,
    OrOr,
    Bang,

    LBracket,
    RBracket,
    
    Class,
    New,
    Override,
    Colon,
    ColonColon,
    Super,
    Interface,
    Null,
    Is,
    
    Switch,
    Case,
    Default,
    
    Comment,
    Question,
    
    Public,
    Private,
    Protected,
    
    Import,
    
    Eof
}

public readonly record struct Position(int Line, int Column, int Offset);

public readonly record struct Token(TokenType Type, string Value, Position Start, Position End)
{
    public override string ToString() => Type == TokenType.IntLiteral || Type == TokenType.FloatLiteral ? $"{Type}({Value}) at {Start.Line}:{Start.Column}" : $"{Type} at {Start.Line}:{Start.Column}";
}

public readonly record struct LexerError(string Message, Position Position);

public sealed class LexerResult(IReadOnlyList<Token> tokens, IReadOnlyList<LexerError> errors)
{
    public IReadOnlyList<Token> Tokens { get; } = tokens;
    public IReadOnlyList<LexerError> Errors { get; } = errors;
    public bool HasErrors => Errors.Count > 0;
}

public sealed class Lexer(string source)
{
    private int _offset;
    private int _line = 1;
    private int _column = 1;
    private List<LexerError> _errors = [];

    private static readonly Dictionary<string, TokenType> Keywords = new()
    {
        ["bool"]      = TokenType.BoolKeyword,
        ["true"]      = TokenType.True,
        ["false"]     = TokenType.False,
        ["if"]        = TokenType.If,
        ["else"]      = TokenType.Else,
        ["while"]     = TokenType.While,
        ["for"]       = TokenType.For,
        ["foreach"]   = TokenType.Foreach,
        ["in"]        = TokenType.In,
        ["int"]       = TokenType.IntKeyword,
        ["float"]     = TokenType.FloatKeyword,
        ["string"]    = TokenType.StringKeyword,
        ["void"]      = TokenType.VoidKeyword,
        ["return"]    = TokenType.Return,
        ["struct"]    = TokenType.StructKeyword,
        ["enum"]      = TokenType.Enum,
        ["class"]     = TokenType.Class,
        ["interface"] = TokenType.Interface,
        ["new"]       = TokenType.New,
        ["override"]  = TokenType.Override,
        ["super"]     = TokenType.Super,
        ["null"]      = TokenType.Null,
        ["is"]        = TokenType.Is,
        ["switch"]    = TokenType.Switch,
        ["case"]      = TokenType.Case,
        ["default"]   = TokenType.Default,
        ["public"]    = TokenType.Public,
        ["private"]   = TokenType.Private,
        ["protected"] = TokenType.Protected,
        ["import"]    = TokenType.Import
    };

    public LexerResult Tokenize()
    {
        _errors = [];
        var tokens = new List<Token>();

        while (!IsAtEnd())
        {
            SkipWhitespace();
            if (IsAtEnd()) break;

            var token = ReadNextToken();
            if (token.HasValue) tokens.Add(token.Value);
        }
        
        tokens.Add(MakeToken(TokenType.Eof, ""));
        return new LexerResult(tokens, _errors);
    }

    private Token? ReadNextToken()
    {
        var start = CurrentPosition();
        var ch = Current();

        if (char.IsDigit(ch)) return ReadNumber(start);
        if (char.IsLetter(ch) || ch == '_') return ReadIdentifier(start);
        if (ch == '"') return ReadString(start);
        if (ch == '$' && _offset + 1 < source.Length && source[_offset + 1] == '"') return ReadInterpolatedString(start);

        return ch switch
        {
            '+' => ReadTriple('+', TokenType.PlusPlus, '=', TokenType.PlusEqual, TokenType.Plus, start),
            '-' => ReadTriple('-', TokenType.MinusMinus, '=', TokenType.MinusEqual, TokenType.Minus, start),
            '*' => ReadDouble('=', TokenType.StarEqual, TokenType.Star, start),
            '%' => Consume(TokenType.Percent,   "%", start),
            '(' => Consume(TokenType.LParen,    "(", start),
            ')' => Consume(TokenType.RParen,    ")", start),
            '{' => Consume(TokenType.LBrace,    "{", start),
            '}' => Consume(TokenType.RBrace,    "}", start),
            '[' => Consume(TokenType.LBracket,  "[", start),
            ']' => Consume(TokenType.RBracket,  "]", start),
            ';' => Consume(TokenType.Semicolon, ";", start),
            ',' => Consume(TokenType.Comma,     ",", start),
            '.' => Consume(TokenType.Dot,       ".", start),
            '?' => Consume(TokenType.Question,  "?", start),
 
            '/' => ReadSlashOrComment(start),
            '=' => ReadDouble('=', TokenType.EqualEqual, TokenType.Equal, start),
            '!' => ReadDouble('=', TokenType.NotEqual,   TokenType.Bang,  start),
            '>' => ReadDouble('=', TokenType.GreaterEqual, TokenType.Greater, start),
            '<' => ReadDouble('=', TokenType.LessEqual,    TokenType.Less,    start),
            '&' => ReadDouble('&', TokenType.AndAnd, null, start),
            '|' => ReadDouble('|', TokenType.OrOr,   null, start),
            ':' => ReadDouble(':', TokenType.ColonColon, TokenType.Colon, start),
 
            _   => HandleUnknown(start)
        };
    }

    private Token ReadNumber(Position start)
    {
        var sb = new StringBuilder();
        bool hasDot = false;

        while (!IsAtEnd() && (char.IsDigit(Current()) || Current() == '.'))
        {
            if (Current() == '.')
            {
                if (hasDot)
                {
                    AddError("Invalid number: multiple dots.",  CurrentPosition());
                    break;
                }
                hasDot = true;
            }

            sb.Append(Current());
            Advance();
        }

        var type = hasDot ? TokenType.FloatLiteral : TokenType.IntLiteral;
        return new Token(type, sb.ToString(), start, PreviousPosition());
    }
    
    private Token ReadIdentifier(Position start)
    {
        var sb = new StringBuilder();
        while (!IsAtEnd() && (char.IsLetterOrDigit(Current()) || Current() == '_'))
        {
            sb.Append(Current());
            Advance();
        }
        
        var text = sb.ToString();
        var type = Keywords.GetValueOrDefault(text, TokenType.Identifier);
        
        return new Token(type, text, start, PreviousPosition());
    }
    
    private Token ReadString(Position start)
    {
        Advance();
        var sb = new StringBuilder();
 
        while (!IsAtEnd() && Current() != '"')
        {
            if (Current() == '\\')
            {
                Advance();
                var escaped = Current() switch
                {
                    '"'  => '"',
                    '\\' => '\\',
                    'n'  => '\n',
                    'r'  => '\r',
                    't'  => '\t',
                    '0'  => '\0',
                    var c => c
                };
                sb.Append(escaped);
            }
            else
            {
                sb.Append(Current());
            }
            Advance();
        }
 
        if (IsAtEnd())
            AddError("Unterminated string literal", start);
        else
            Advance();
 
        return new Token(TokenType.StringLiteral, sb.ToString(), start, PreviousPosition());
    }

    private Token ReadInterpolatedString(Position start)
    {
        Advance();
        Advance();
        var sb = new StringBuilder();
        int depth = 0;

        while (!IsAtEnd())
        {
            var c = Current();
            
            if (depth == 0 && c == '"')
            {
                Advance();
                break;
            }

            if (c == '{')
            {
                depth++;
                sb.Append(c);
                Advance();
                continue;
            }
            
            if (c == '}')
            {
                depth--;
                sb.Append(c);
                Advance();
                continue;
            }
            
            if (depth == 0 && c == '\\')
            {
                Advance();
                var escaped = Current() switch
                {
                    '"'  => '"',
                    '\\' => '\\',
                    'n'  => '\n',
                    'r'  => '\r',
                    't'  => '\t',
                    '0'  => '\0',
                    var x => x
                };
                sb.Append(escaped);
                Advance();
                continue;
            }
 
            sb.Append(c);
            Advance();
        }

        if (IsAtEnd() && depth > 0)
            AddError("Unterminated interpolated string", start);
        
        return new Token(TokenType.InterpolatedStringLiteral, sb.ToString(), start, PreviousPosition());
    }
    
    private Token ReadSlashOrComment(Position start)
    {
        Advance();
 
        if (!IsAtEnd() && Current() == '/')
        {
            var commentStart = start;
            var sb = new StringBuilder("//");
            Advance();
 
            while (!IsAtEnd() && Current() != '\n' && Current() != '\r')
            {
                sb.Append(Current());
                Advance();
            }
 
            return new Token(TokenType.Comment, sb.ToString(), commentStart, PreviousPosition());
        }
 
        return new Token(TokenType.Slash, "/", start, CurrentPosition());
    }

    private Token? ReadDouble(char next, TokenType doubleType, TokenType? singleType, Position start)
    {
        Advance();
        if (!IsAtEnd() && Current() == next)
        {
            Advance();
            return new Token(doubleType, source.Substring(start.Offset, _offset - start.Offset), start, PreviousPosition());
        }
 
        if (singleType.HasValue)
            return new Token(singleType.Value, source.Substring(start.Offset, _offset - start.Offset), start, PreviousPosition());
 
        AddError($"Expected '{next}' after '{source[start.Offset]}'", start);
        return null;
    }
    
    private Token Consume(TokenType type, string value, Position start)
    {
        Advance();
        return new Token(type, value, start, PreviousPosition());
    }
    
    private Token? HandleUnknown(Position start)
    {
        var ch = Current();
        Advance();
        AddError($"Unexpected character: '{ch}'", start);
        return null;
    }
    
    private char Current() => _offset < source.Length ? source[_offset] : '\0';
 
    private void Advance()
    {
        if (_offset >= source.Length) return;
 
        if (source[_offset] == '\n')
        {
            _line++;
            _column = 1;
        }
        else
        {
            _column++;
        }
        _offset++;
    }
 
    private void SkipWhitespace()
    {
        while (!IsAtEnd() && char.IsWhiteSpace(Current()))
            Advance();
    }
 
    private bool IsAtEnd() => _offset >= source.Length;
 
    private Position CurrentPosition() => new(_line, _column, _offset);
    
    private Position PreviousPosition()
    {
        if (_offset == 0) return new Position(_line, _column, _offset);

        var col = _column - 1;
        var line = _line;

        if (col <= 0)
        {
            line--;
            col = 1;
        }

        return new Position(line, col, _offset - 1);
    }
 
    private Token ReadTriple(
        char second,
        TokenType doubleType,
        char third,
        TokenType assignType,
        TokenType singleType,
        Position start)
    {
        Advance();

        if (!IsAtEnd())
        {
            if (Current() == second)
            {
                Advance();
                return new Token(doubleType, source.Substring(start.Offset, _offset - start.Offset), start, PreviousPosition());
            }

            if (Current() == third)
            {
                Advance();
                return new Token(assignType, source.Substring(start.Offset, _offset - start.Offset), start, PreviousPosition());
            }
        }

        return new Token(singleType, source.Substring(start.Offset, _offset - start.Offset), start, PreviousPosition());
    }
    
    private Token MakeToken(TokenType type, string value) =>
        new(type, value, CurrentPosition(), CurrentPosition());
    
    private void AddError(string message, Position position)
    {
        _errors.Add(new LexerError(message, position));
    }
}