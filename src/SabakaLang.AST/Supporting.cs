using SabakaLang.AST.Interfaces;
using SabakaLang.AST.Structures;

namespace SabakaLang.AST;

public record SwitchCase(IExpr? Value, List<IStmt> Body);
public record TypeRef(string Name, List<string> TypeArgs, bool IsArray, bool IsNullable = false, Span Span = default)
{
    /// <summary>Number of <c>[]</c> suffixes: <c>int[][]</c> has depth 2. <see cref="IsArray"/> is true when depth &gt; 0.</summary>
    public int ArrayDepth { get; init; } = IsArray ? 1 : 0;
}
public record TypeParam(string Name);
public record Param(TypeRef Type, string Name, Span Span = default);
public enum AccessMod { Public, Private, Protected }