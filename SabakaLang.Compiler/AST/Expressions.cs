using SabakaLang.Compiler.Lexing;

namespace SabakaLang.Compiler.AST;

public record IntLit (int Value, Span Span) : IExpr;
public record FloatLit (double Value, Span Span) : IExpr;
public record StringLit (string Value, Span Span) : IExpr;
public record CharLit (char Value, Span Span) : IExpr;
public record BoolLit (bool Value, Span Span) : IExpr;
public record NullLit(Span Span) : IExpr;

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
public record TernaryExpr(IExpr Condition, IExpr Then, IExpr Else, Span Span) : IExpr;
public record InterpolatedStringExpr(List<IExpr> Parts, Span Span) : IExpr;

public record CoalesceExpr(IExpr Left, IExpr Right, Span Span) : IExpr;
public record IsExpr(IExpr Left, TypeRef Right, Span Span) : IExpr;
