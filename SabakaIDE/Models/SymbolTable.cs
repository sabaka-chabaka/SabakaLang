namespace SabakaIDE.Models;

public class SymbolTable
{
    private Dictionary<string, Symbol> _symbols = new();

    public void Add(Symbol symbol)
    {
        _symbols[symbol.Name] = symbol;
    }

    public Symbol? Get(string name)
    {
        return _symbols.TryGetValue(name, out var s) ? s : null;
    }

    public IEnumerable<Symbol> All => _symbols.Values;
}
