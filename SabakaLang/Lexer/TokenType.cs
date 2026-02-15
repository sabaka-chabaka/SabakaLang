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
    While,
    LBrace,
    RBrace,
    
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
    StringKeyword,

    
    AndAnd,    // &&
    OrOr,      // ||
    Bang,       // !

    
    EOF
}