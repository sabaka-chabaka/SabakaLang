using OmniSharp.Extensions.LanguageServer.Protocol;
using SabakaLang.LSP.Analysis;

namespace SabakaLang.LSP;

public class SymbolIndex
{
    private readonly Dictionary<DocumentUri, List<Symbol>> _documentSymbols = new();

    public void UpdateSymbols(DocumentUri uri, List<Symbol> symbols)
    {
        lock (_documentSymbols)
        {
            _documentSymbols[uri] = symbols;
        }
    }

    public void RemoveDocument(DocumentUri uri)
    {
        lock (_documentSymbols)
        {
            _documentSymbols.Remove(uri);
        }
    }

    public IEnumerable<Symbol> GetAvailableSymbols(DocumentUri uri, int offset)
    {
        lock (_documentSymbols)
        {
            var result = new List<Symbol>();
            
            foreach (var entry in _documentSymbols)
            {
                if (entry.Key == uri)
                {
                    // For the current file, include all symbols that are in scope at the given offset
                    result.AddRange(entry.Value.Where(s => offset >= s.ScopeStart && offset <= s.ScopeEnd));
                }
                else
                {
                    // For other files, include only global symbols
                    result.AddRange(entry.Value.Where(s => s.ScopeStart == 0 && s.ScopeEnd == int.MaxValue));
                }
            }
            
            // Return unique symbols by name, prioritizing the most specific ones (implied by order of addition if we were more careful, 
            // but for now simple DistinctBy is better than nothing)
            return result.GroupBy(s => s.Name).Select(g => g.First()).ToList();
        }
    }

    public IEnumerable<Symbol> GetAllSymbols()
    {
        lock (_documentSymbols)
        {
            return _documentSymbols.Values.SelectMany(s => s).ToList();
        }
    }
}
