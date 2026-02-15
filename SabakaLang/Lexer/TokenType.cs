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
    For,
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

    Function,      // можно func если хочешь
    Return,
    VoidKeyword,
    Comma,
    
    Foreach,
    In,

    
    AndAnd,    // &&
    OrOr,      // ||
    Bang,       // !

    LBracket,
    RBracket,
    
    EOF
}