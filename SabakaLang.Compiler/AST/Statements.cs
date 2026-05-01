namespace SabakaLang.Compiler.AST;

public record ExprStmt(IExpr Expr, Span Span) : IStmt;
public record ReturnStmt(IExpr? Value, Span Span) : IStmt;
public record IfStmt(IExpr Condition, List<IStmt> Then, List<IStmt>? Else, Span Span) : IStmt;
public record WhileStmt(IExpr Condition, List<IStmt> Body, Span Span) : IStmt;
public record ForStmt(IStmt? Init, IExpr? Condition, IExpr? Step, List<IStmt> Body, Span Span) : IStmt;
public record ForeachStmt(TypeRef ItemType, string ItemName, IExpr Collection, List<IStmt> Body, Span Span) : IStmt;
public record SwitchStmt(IExpr Value, List<SwitchCase> Cases, Span Span) : IStmt;
public record ImportStmt(string Path, List<string> Names, string? Alias, Span Span) : IStmt;
