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

public sealed class Scope
{
    public Scope? Parent { get; }
    private readonly Dictionary<string, Symbol> _symbols = new();
    public IEnumerable<Symbol> LocalSymbols => _symbols.Values;
 
    public Scope(Scope? parent = null) => Parent = parent;
 
    public bool Declare(Symbol symbol)
    {
        if (_symbols.ContainsKey(symbol.Name)) return false;
        _symbols[symbol.Name] = symbol;
        return true;
    }
 
    public Symbol? Resolve(string name)
    {
        if (_symbols.TryGetValue(name, out var s)) return s;
        return Parent?.Resolve(name);
    }
 
    public Symbol? ResolveLocal(string name) =>
        _symbols.GetValueOrDefault(name);
 
    public void ForceReplace(Symbol symbol) => _symbols[symbol.Name] = symbol;
}

public sealed class SymbolTable
{
    private readonly List<Symbol> _all = [];
    public IReadOnlyList<Symbol> All => _all;
    
    internal void Add(Symbol symbol) => _all.Add(symbol);
    
    public IEnumerable<Symbol> Lookup(string name) => _all.Where(s => s.Name == name);
    
    public IEnumerable<Symbol> MembersOf(string typeName) => _all.Where(s => s.ParentName == typeName);
    
    public Symbol? SymbolAt(int offset) => _all.FirstOrDefault(s => s.Span.Start.Offset <= offset && offset <= s.Span.End.Offset);
}

public class Binder
{
    
}