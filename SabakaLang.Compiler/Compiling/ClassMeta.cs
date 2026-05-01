using SabakaLang.Compiler.AST;

namespace SabakaLang.Compiler.Compiling;

internal sealed class ClassMeta(string name, string? @base)
{
    public string  Name       { get; } = name;
    public string? Base       { get; } = @base;
    public List<string>         Fields  { get; } = [];
    public List<VarDecl>        FieldDecls { get; } = [];
    public List<FuncDecl>       Methods { get; } = [];

    public readonly List<string> Interfaces = [];
}
