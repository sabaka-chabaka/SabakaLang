using SabakaLang.Compiler.AST;

namespace SabakaLang.Compiler.Binding;

public sealed class Symbol(
    string name,
    SymbolKind kind,
    string type,
    Span span,
    string? parentName = null,
    string? parameters = null,
    IReadOnlyList<string>? typeParams = null)
{
    public string     Name        { get; } = name;
    public SymbolKind Kind        { get; } = kind;
    public string     Type        { get; } = type;
    public Span       Span        { get; } = span;
    public string?    ParentName  { get; } = parentName;
    public string?    Parameters  { get; } = parameters;
    public IReadOnlyList<string> TypeParams { get; } = typeParams ?? [];

    public override string ToString() =>
        ParentName is null
            ? $"{Kind} {Name} : {Type}"
            : $"{Kind} {ParentName}.{Name} : {Type}";
}
