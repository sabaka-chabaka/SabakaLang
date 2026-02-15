namespace SabakaLang.Compiler;

public enum OpCode
{
    Push,

    Add,
    Sub,
    Mul,
    Div,
    Print,
    Store,
    Jump,
    JumpIfFalse,
    Load,
    Equal,
    NotEqual,
    Greater,
    Less,
    GreaterEqual,
    LessEqual,
    Negate,
    Declare,
    EnterScope,
    ExitScope,
    Not,
    Call,
    Return,
    FunctionStart,
    Function,
    JumpIfTrue,
    CreateArray,
    ArrayLoad,
    ArrayStore,
    ArrayLength
}