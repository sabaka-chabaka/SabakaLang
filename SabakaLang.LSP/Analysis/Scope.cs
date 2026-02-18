namespace SabakaLang.LSP.Analysis;

public class Scope
{
    public Scope? Parent { get; }
    private readonly Dictionary<string, Symbol> _symbols = new();
    public IEnumerable<Symbol> Symbols => _symbols.Values;

    public Scope(Scope? parent = null)
    {
        Parent = parent;
    }

    public bool Declare(Symbol symbol)
    {
        if (_symbols.ContainsKey(symbol.Name))
            return false;
        _symbols[symbol.Name] = symbol;
        return true;
    }

    public Symbol? Resolve(string name)
    {
        if (_symbols.TryGetValue(name, out var symbol))
            return symbol;
        return Parent?.Resolve(name);
    }
}
