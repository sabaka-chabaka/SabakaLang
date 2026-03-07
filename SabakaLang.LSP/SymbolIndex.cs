using OmniSharp.Extensions.LanguageServer.Protocol;
using SabakaLang.LSP.Analysis;

namespace SabakaLang.LSP;

/// <summary>
/// Thread-safe index of all symbols across all open documents.
/// Each document stores its own symbols + its module map.
/// </summary>
public class SymbolIndex
{
    private readonly object _lock = new();

    // uri -> flat list of symbols in that document (includes imported global symbols)
    private readonly Dictionary<DocumentUri, List<Symbol>> _docSymbols = new();

    // uri -> alias -> members (for dot-completion)
    private readonly Dictionary<DocumentUri, Dictionary<string, List<Symbol>>> _docModules = new();

    // ── write ──────────────────────────────────────────────────────────────

    public void UpdateDocument(DocumentUri uri, List<Symbol> symbols,
        Dictionary<string, List<Symbol>>? modules = null)
    {
        lock (_lock)
        {
            _docSymbols[uri] = symbols;
            _docModules[uri] = modules ?? new Dictionary<string, List<Symbol>>();
        }
    }

    public void RemoveDocument(DocumentUri uri)
    {
        lock (_lock)
        {
            _docSymbols.Remove(uri);
            _docModules.Remove(uri);
        }
    }

    // ── read: general ──────────────────────────────────────────────────────

    /// <summary>
    /// Symbols visible at a given offset in a given document.
    /// Returns:
    ///  - all symbols from THIS document where offset is in [scopeStart, scopeEnd]
    ///  - module alias symbols (kind=Module) from this document
    ///  - global symbols (scopeStart=0, scopeEnd=MaxValue) from all OTHER documents
    /// </summary>
    public IEnumerable<Symbol> GetAvailableSymbols(DocumentUri uri, int offset)
    {
        lock (_lock)
        {
            var result = new Dictionary<string, Symbol>();

            // Other documents — only truly global symbols (no module members)
            foreach (var kv in _docSymbols)
            {
                if (kv.Key == uri) continue;
                foreach (var s in kv.Value)
                {
                    if (s.ModuleAlias == null &&
                        s.ParentName == null &&
                        s.ScopeStart == 0 && s.ScopeEnd == int.MaxValue)
                    {
                        result.TryAdd(s.Name, s);
                    }
                }
            }

            // This document — scope-filtered
            if (_docSymbols.TryGetValue(uri, out var mine))
            {
                foreach (var s in mine)
                {
                    if (offset >= s.ScopeStart && offset <= s.ScopeEnd)
                        result[s.Name] = s; // own doc wins
                }
            }

            return result.Values;
        }
    }

    /// <summary>
    /// Members of a type (for obj.| completion and hover on member access).
    /// Searches by parentName across all documents.
    /// </summary>
    public IEnumerable<Symbol> GetMembers(string typeName)
    {
        lock (_lock)
        {
            return _docSymbols.Values
                .SelectMany(v => v)
                .Where(s => string.Equals(s.ParentName, typeName, StringComparison.OrdinalIgnoreCase))
                .GroupBy(s => s.Name)
                .Select(g => g.First())
                .ToList();
        }
    }

    /// <summary>
    /// Module members for alias.| completion.
    /// First looks in the requesting document's own modules,
    /// then falls back to searching all documents.
    /// </summary>
    public IEnumerable<Symbol> GetModuleMembers(DocumentUri uri, string alias)
    {
        lock (_lock)
        {
            // Own document first
            if (_docModules.TryGetValue(uri, out var myMods) &&
                myMods.TryGetValue(alias, out var myList))
                return myList;

            // Fallback: any document that knows this alias
            foreach (var kv in _docModules)
            {
                if (kv.Value.TryGetValue(alias, out var list))
                    return list;
            }

            // Last resort: symbols tagged with ModuleAlias
            return _docSymbols.Values
                .SelectMany(v => v)
                .Where(s => string.Equals(s.ModuleAlias, alias, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    /// <summary>All symbols across all documents (used by semantic tokens).</summary>
    public IEnumerable<Symbol> GetAllSymbols()
    {
        lock (_lock)
        {
            return _docSymbols.Values.SelectMany(v => v).ToList();
        }
    }
}