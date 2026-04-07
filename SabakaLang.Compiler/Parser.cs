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
    
}

file sealed class ParseException : Exception { }