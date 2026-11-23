namespace SabakaLang.Binder;

public sealed class Scope(Scope? parent = null)
{
    private readonly Dictionary<string, Symbol> _symbols = new();
    public Scope? Parent { get; } = parent;

    public IEnumerable<Symbol> Symbols => _symbols.Values;

    public bool TryDeclare(Symbol symbol) => _symbols.TryAdd(symbol.Name, symbol);

    public void Replace(Symbol symbol) => _symbols[symbol.Name] = symbol;

    public Symbol? LookupLocal(string name) => _symbols.GetValueOrDefault(name);

    public Symbol? Lookup(string name)
    {
        for (var s = this; s is not null; s = s.Parent)
            if (s._symbols.TryGetValue(name, out var sym)) return sym;
        return null;
    }
}
