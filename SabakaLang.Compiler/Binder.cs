namespace SabakaLang.Compiler;

public enum SymbolKind
{
    Variable,
    Parameter,
    Function,
    Method,
    Field,
    Class,
    Interface,
    Struct,
    Enum,
    EnumMember,
    TypeParam,
    BuiltIn,
    Module
}

public sealed class Symbol
{
    public string Name { get; }
    public SymbolKind Kind { get; }
    public string Type { get; }
    public Span Span { get; }
    public string? ParentName { get; }
    public string? Parameters { get; }
    public IReadOnlyList<string> TypeParams { get; }
    
    public Symbol(
        string name, SymbolKind kind, string type, Span span,
        string? parentName = null, string? parameters = null,
        IReadOnlyList<string>? typeParams = null)
    {
        Name       = name;
        Kind       = kind;
        Type       = type;
        Span       = span;
        ParentName = parentName;
        Parameters = parameters;
        TypeParams = typeParams ?? [];
    }
    
    public override string ToString() => ParentName is null
        ? $"{Kind} {Name} : {Type}"
        : $"{Kind} {ParentName}.{Name} : {Type}";
}

public class Binder
{
    
}