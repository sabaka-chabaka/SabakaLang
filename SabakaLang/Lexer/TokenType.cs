namespace SabakaLang.Lexer;

public enum TokenType
{
    Number,
    
    Plus,
    Minus,
    Star,
    Slash,
    
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
    LBrace,
    RBrace,

    
    EOF
}