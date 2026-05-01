namespace SabakaLang.Compiler.Compiling;

public enum OpCode
{
    Push,
    Pop,
    Dup,
    Swap,
 
    Add, Sub, Mul, Div, Mod,
 
    Equal, NotEqual, Greater, Less, GreaterEqual, LessEqual,
 
    And, Or, Not, Negate,
 
    Declare,
    Load,
    Store,
 
    EnterScope,
    ExitScope,
 
    Jump,
    JumpIfFalse,
    JumpIfTrue,
    
    Function,
    Call,
    Return,
    
    Is,
    
    CreateObject,
    CallMethod,
    LoadField,
    StoreField,
    PushThis,
    Inherit,
 
    CreateArray,
    ArrayLoad,
    ArrayStore,
    ArrayLength,
 
    CreateStruct,
 
    PushEnum,
 
    Print,
    Input,
    Sleep,
    ReadFile, WriteFile, AppendFile, FileExists, DeleteFile, ReadLines,
    Time, TimeMs,
    HttpGet, HttpPost, HttpPostJson,
    Ord, Chr,
 
    CallExternal
}
