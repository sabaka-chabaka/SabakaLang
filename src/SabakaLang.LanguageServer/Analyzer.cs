using SabakaLang.AST;
using SabakaLang.AST.Interfaces;
using SabakaLang.Binder;
using SabakaLang.Engine;
using SabakaLang.Extensions;
using SabakaLang.Lexer.Enums;
using SabakaLang.Lexer.Structures;
using BinderImpl = SabakaLang.Binder.Binder;

namespace SabakaLang.LanguageServer;

public sealed record Diag(int Start, int End, int Severity, string Message);

public sealed class LoadedModule
{
    public required string Name { get; init; }
    public required string Id { get; init; }
    public required string Source { get; init; }
    public required IReadOnlyList<Token> Tokens { get; init; }
    public required IReadOnlyList<Token> Code { get; init; }
    public required IReadOnlyList<IStmt> Statements { get; init; }
    public required ModuleInput Input { get; init; }
}

public sealed class Analysis
{
    public required string Uri { get; init; }
    public required string Text { get; init; }
    public required LineIndex Lines { get; init; }
    public required IReadOnlyList<LoadedModule> Modules { get; init; }
    public required BindResult? Bind { get; init; }
    public required IReadOnlyList<Diag> Diagnostics { get; init; }
    public required ExtensionRegistry Registry { get; init; }

    public LoadedModule Root => Modules[Modules.Count - 1];
    public IReadOnlyList<Ref> Refs { get; set; } = [];

    private Dictionary<int, Ref>? _byStart;

    public Ref? RefAtStart(int start)
    {
        _byStart ??= Refs.GroupBy(r => r.Start).ToDictionary(g => g.Key, g => g.First());
        return _byStart.GetValueOrDefault(start);
    }

    public Ref? RefAt(int offset) => Refs.FirstOrDefault(r => offset >= r.Start && offset <= r.End);
}

public sealed class Analyzer(ExtensionRegistry registry, DocumentStore docs)
{
    public ExtensionRegistry Registry => registry;

    public Analysis Analyze(string uri, string text)
    {
        var rootPath = Paths.ToPath(uri);
        var order = new List<ModuleInput>();
        var modules = new List<LoadedModule>();
        var rootErrors = new List<(string Message, int Offset)>();
        new Loader(this, order, modules, rootErrors).Load(rootPath, Path.GetFileNameWithoutExtension(rootPath), text, true);

        var root = modules[^1];
        var diags = new List<Diag>();
        void Add(string message, int offset, int severity)
        {
            var (s, e) = TokenRange(root.Tokens, offset, text.Length);
            diags.Add(new Diag(s, e, severity, message));
        }

        foreach (var (m, o) in rootErrors) Add(m, o, 1);

        BindResult? bind = null;
        try { bind = new BinderImpl(registry).Bind(order); }
        catch (Exception ex) { Console.Error.WriteLine("binder crashed: " + ex.Message); }

        if (bind is not null && rootErrors.Count == 0)
        {
            foreach (var d in bind.Errors.Where(d => d.Module == ""))
                if (!d.Message.StartsWith("Cannot resolve import")) Add(d.Message, d.Position.Offset, 1);
            foreach (var d in bind.Warnings.Where(d => d.Module == "")) Add(d.Message, d.Position.Offset, 2);
        }

        var a = new Analysis
        {
            Uri = uri, Text = text, Lines = new LineIndex(text), Modules = modules,
            Bind = bind, Diagnostics = diags, Registry = registry
        };
        try { a.Refs = RefBuilder.Build(a); }
        catch (Exception ex) { Console.Error.WriteLine("ref builder crashed: " + ex); }
        return a;
    }

    private static (int Start, int End) TokenRange(IReadOnlyList<Token> tokens, int offset, int len)
    {
        offset = Math.Clamp(offset, 0, len);
        int lo = 0, hi = tokens.Count - 1, found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (tokens[mid].Start.Offset <= offset) { found = mid; lo = mid + 1; } else hi = mid - 1;
        }
        if (found >= 0)
        {
            var t = tokens[found];
            var end = Math.Min(t.End.Offset + 1, len);
            if (t.Type != TokenType.Eof && end > offset) return (t.Start.Offset, end);
        }
        if (offset >= len) return (Math.Max(0, len - 1), len);
        return (offset, offset + 1);
    }

    private ModuleText? LoadImport(string importPath, string importerId)
    {
        var dir = Path.GetDirectoryName(importerId);
        if (string.IsNullOrEmpty(dir)) return null;
        var candidates = importPath.EndsWith(".sabaka", StringComparison.OrdinalIgnoreCase)
            ? new[] { importPath }
            : new[] { importPath, importPath + ".sabaka" };
        foreach (var cand in candidates)
        {
            string full;
            try { full = Path.GetFullPath(Path.Combine(dir, cand)); } catch { continue; }
            var open = docs.TryGetTextByPath(full);
            if (open is not null) return new ModuleText(full, Path.GetFileNameWithoutExtension(full), open);
            if (File.Exists(full)) return new ModuleText(full, Path.GetFileNameWithoutExtension(full), File.ReadAllText(full));
        }
        return null;
    }

    private sealed class Loader(Analyzer owner, List<ModuleInput> order, List<LoadedModule> loaded, List<(string, int)> rootErrors)
    {
        private readonly Dictionary<string, string> _namesById = new();
        private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _loading = new();

        public string Load(string id, string preferredName, string source, bool isRoot)
        {
            var name = isRoot ? preferredName : UniqueName(preferredName);
            if (!isRoot) _namesById[id] = name;
            _names.Add(name);
            _loading.Add(id);

            void Err(string m, int o) { if (isRoot) rootErrors.Add((m, o)); }

            var lex = new SabakaLang.Lexer.Lexer().Tokenize(source);
            foreach (var e in lex.Errors) Err(e.Message, e.Position.Offset);
            var parse = new SabakaLang.Parser.Parser(lex).Parse();
            foreach (var e in parse.Errors) Err(e.Message, e.Position.Offset);

            var imports = new Dictionary<string, ImportTarget>();
            foreach (var imp in parse.Statements.OfType<ImportStmt>())
            {
                if (imports.ContainsKey(imp.Path)) continue;
                if (owner.Registry.FindModule(imp.Path) is { } native)
                {
                    imports[imp.Path] = new NativeImport(native.Name);
                    continue;
                }
                var text = owner.LoadImport(imp.Path, id);
                if (text is null)
                {
                    Err($"Cannot resolve import '{imp.Path}': it is neither a registered native module nor a script file.", imp.Span.Start.Offset);
                    continue;
                }
                if (_loading.Contains(text.Id))
                {
                    Err($"Circular import of '{imp.Path}'.", imp.Span.Start.Offset);
                    continue;
                }
                var child = _namesById.TryGetValue(text.Id, out var known) ? known : Load(text.Id, text.Name, text.Source, false);
                imports[imp.Path] = new SourceImport(child);
            }

            _loading.Remove(id);
            var input = new ModuleInput { Name = name, IsRoot = isRoot, Statements = parse.Statements, Imports = imports };
            order.Add(input);
            loaded.Add(new LoadedModule
            {
                Name = name, Id = id, Source = source, Tokens = lex.Tokens,
                Code = lex.Tokens.Where(t => t.Type != TokenType.Comment).ToList(),
                Statements = parse.Statements, Input = input
            });
            return name;
        }

        private string UniqueName(string name)
        {
            var c = name;
            for (var i = 2; _names.Contains(c); i++) c = $"{name}_{i}";
            return c;
        }
    }
}
