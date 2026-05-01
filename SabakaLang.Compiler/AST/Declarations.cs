namespace SabakaLang.Compiler.AST;

public record VarDecl(TypeRef Type, string Name, IExpr? Init, AccessMod Access, Span Span, bool IsConst = false) : IStmt;

public record ConstDecl(TypeRef Type, string Name, IExpr Value, AccessMod Access, Span Span) : IStmt
{
    public VarDecl ToVarDecl() => new(Type, Name, Value, Access, Span, IsConst: true);
}

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
