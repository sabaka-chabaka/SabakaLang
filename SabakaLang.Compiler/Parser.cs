namespace SabakaLang.Compiler;

public readonly record struct Span(Position Start, Position End);

public interface INode {Span Span { get; }}

public interface IExpr : INode {}
public interface IStmt : INode {}

public record IntLit (int Value, Span Span) : IExpr;
public record FloatLit (double Value, Span Span) : IExpr;
public record StringLit (string Value, Span Span) : IExpr;
public record BoolLit (bool Value, Span Span) : IExpr;

public record NameExpr(string Name, Span Span) : IExpr;
public record BinaryExpr(IExpr Left, TokenType Op, IExpr Right, Span Span) : IExpr;
public record UnaryExpr(TokenType Op, IExpr Operand, Span Span) : IExpr;
public record CallExpr(IExpr Callee, List<IExpr> Args, Span Span) : IExpr;
public record MemberExpr(IExpr Object, string Member, Span Span) : IExpr;
public record IndexExpr(IExpr Object, IExpr Index, Span Span) : IExpr;
public record ArrayExpr(List<IExpr> Elements, Span Span) : IExpr;
public record NewExpr(string TypeName, List<string> TypeArgs, List<IExpr> Args, Span Span) : IExpr;
public record SuperExpr(Span Span) : IExpr;
public record AssignExpr(IExpr Target, IExpr Value, Span Span) : IExpr;

public record ExprStmt(IExpr Expr, Span Span) : IStmt;
public record ReturnStmt(IExpr? Value, Span Span) : IStmt;
public record IfStmt(IExpr Condition, List<IStmt> Then, List<IStmt>? Else, Span Span) : IStmt;
public record WhileStmt(IExpr Condition, List<IStmt> Body, Span Span) : IStmt;
public record ForStmt(IStmt? Init, IExpr? Condition, IExpr? Step, List<IStmt> Body, Span Span) : IStmt;
public record ForeachStmt(TypeRef ItemType, string ItemName, IExpr Collection, List<IStmt> Body, Span Span) : IStmt;
public record SwitchStmt(IExpr Value, List<SwitchCase> Cases, Span Span) : IStmt;
public record ImportStmt(string Path, List<string> Names, string? Alias, Span Span) : IStmt;

public record VarDecl(TypeRef Type, string Name, IExpr? Init, AccessMod Access, Span Span) : IStmt;
public record FuncDecl(
    TypeRef ReturnType,
    string Name,
    List<TypeParam> TypeParams,
    List<Param> Params,
    List<IStmt> Body,
    AccessMod Access,
    bool IsOverride,
    Span Span) : IStmt;
public record ClassDecl(
    string Name,
    List<TypeParam> TypeParams,
    string? Base,
    List<string> Interfaces,
    List<VarDecl> Fields,
    List<FuncDecl> Methods,
    Span Span) : IStmt;
public record InterfaceDecl(
    string Name,
    List<TypeParam> TypeParams,
    List<string> Parents,
    List<FuncDecl> Methods,
    Span Span) : IStmt;
public record StructDecl(string Name, List<VarDecl> Fields, Span Span) : IStmt;
public record EnumDecl(string Name, List<string> Members, Span Span) : IStmt;

public record SwitchCase(IExpr? Value, List<IStmt> Body);
public record TypeRef(string Name, List<string> TypeArgs, bool IsArray);
public record TypeParam(string Name);
public record Param(TypeRef Type, string Name);
public enum AccessMod { Public, Private, Protected }

public record ParseError(string Message, Position Position);

public sealed class ParseResult
{
    public IReadOnlyList<IStmt> Statements { get; }
    public IReadOnlyList<ParseError> Errors { get; }
    public bool HasErrors => Errors.Count > 0;
    
    public ParseResult(IReadOnlyList<IStmt> statements, IReadOnlyList<ParseError> errors)
    {
        Statements = statements;
        Errors = errors;
    }
}

public sealed class Parser
{
    private readonly IReadOnlyList<Token> _tokens;
    private int _pos;
    private readonly List<ParseError> _errors = [];

    public Parser(LexerResult lexer)
    {
        _tokens = lexer.Tokens.Where(t => t.Type != TokenType.Comment).ToList();
    }
    
    public ParseResult Parse()
    {
        var stmts = new List<IStmt>();
        while (!IsAtEnd())
        {
            var stmt = ParseTopLevel();
            if (stmt is not null) stmts.Add(stmt);
        }
        return new ParseResult(stmts, _errors);
    }

    private Token Current => Peek(0);
    private Token Next => Peek(1);

    private Token Peek(int offset = 1)
    {
        var i = _pos + offset;
        return i < _tokens.Count ? _tokens[i] : _tokens[^1];
    }

    private Token Advance()
    {
        var t = Current;
        if (!IsAtEnd()) _pos++;
        return t;
    }
    
    private bool IsAtEnd() => Current.Type == TokenType.EOF;
    
    private bool Check(TokenType type) => Current.Type == type;
    private bool Match(TokenType type)
    {
        if (!Check(type)) return false;
        Advance();
        return true;
    }

    private Token Expect(TokenType type)
    {
        if (Check(type)) return Advance();
        AddError($"Expected {type}, got {Current.Type}", SpanOf(Current).Start);
        return Current; 
    }
    
    private Span SpanFrom(Position start) => new(start, Current.Start);
    private Span SpanOf(Token t) => new(t.Start, t.End);
    
    private void Synchronize()
    {
        while (!IsAtEnd())
        {
            if (Current.Type == TokenType.Semicolon) { Advance(); return; }
            switch (Current.Type)
            {
                case TokenType.Class:
                case TokenType.If:
                case TokenType.While:
                case TokenType.For:
                case TokenType.Return:
                    return;
            }
            Advance();
        }
    }

    private IStmt? ParseTopLevel()
    {
        return null;
    }
    
    private void AddError(string message, Position position)
    {
        _errors.Add(new ParseError(message, position));
    }
}

file sealed class ParseException : Exception { }