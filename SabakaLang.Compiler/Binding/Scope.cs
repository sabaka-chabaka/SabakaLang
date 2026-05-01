namespace SabakaLang.Compiler.Binding;

public sealed class Scope(Scope? parent = null)
{
    private Scope? Parent { get; } = parent;
    private readonly Dictionary<string, Symbol> _symbols = new();
    public IEnumerable<Symbol> LocalSymbols => _symbols.Values;

    public bool Declare(Symbol symbol)
    {
        if (!_symbols.TryAdd(symbol.Name, symbol)) return false;
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
