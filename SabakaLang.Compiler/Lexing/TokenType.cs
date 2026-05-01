namespace SabakaLang.Compiler.Lexing;

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
    CharLiteral,
    InterpolatedStringLiteral,
    StringKeyword,
    CharKeyword,

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
    QuestionQuestion,
    
    Public,
    Private,
    Protected,
    
    Import,
    
    Const,
    
    Eof
}
