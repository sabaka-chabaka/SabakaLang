namespace SabakaLang.Compiler.Binding;

public sealed class SymbolTable
{
    private readonly List<Symbol> _all = [];

    public IReadOnlyList<Symbol> All => _all;

    internal void Add(Symbol s) => _all.Add(s);
    
    internal void AddOrReplace(Symbol s)
    {
        var idx = _all.FindIndex(x => x.Name == s.Name && x.ParentName == s.ParentName);
        if (idx >= 0) _all[idx] = s;
        else          _all.Add(s);
    }

    public IEnumerable<Symbol> Lookup(string name) =>
        _all.Where(s => s.Name == name);

    public IEnumerable<Symbol> MembersOf(string typeName) =>
        _all.Where(s => s.ParentName == typeName);

    public Symbol? SymbolAt(int offset) =>
        _all.FirstOrDefault(s =>
            s.Span.Start.Offset <= offset && offset <= s.Span.End.Offset);
}
