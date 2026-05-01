namespace SabakaLang.Compiler.AST;

public record SwitchCase(IExpr? Value, List<IStmt> Body);
public record TypeRef(string Name, List<string> TypeArgs, bool IsArray, bool IsNullable = false);
public record TypeParam(string Name);
public record Param(TypeRef Type, string Name);
public enum AccessMod { Public, Private, Protected }
