using SabakaLang.Compiler.Binding;
using SabakaLang.Compiler.Lexing;
using SabakaLang.Compiler.Parsing;

namespace SabakaLang.LanguageServer;

public sealed class DocumentAnalysis(
    string uri,
    string source,
    LexerResult lexer,
    ParseResult parse,
    BindResult bind)
{
    public string Uri { get; } = uri;
    public string Source { get; } = source;
    public LexerResult Lexer { get; } = lexer;
    public ParseResult Parse { get; } = parse;
    public BindResult Bind { get; } = bind;
    public IReadOnlyList<Diagnostic> Diagnostics { get; } = BuildDiagnostics(lexer, parse, bind);

    private static List<Diagnostic> BuildDiagnostics(
        LexerResult lexer,
        ParseResult parse,
        BindResult bind)
    {
        var list = new List<Diagnostic>();

        foreach (var e in lexer.Errors)
            list.Add(new Diagnostic(e.Message, e.Position, e.Position, DiagnosticSeverity.Error));

        foreach (var e in parse.Errors)
            list.Add(new Diagnostic(e.Message, e.Position, e.Position, DiagnosticSeverity.Error));

        foreach (var e in bind.Errors)
            list.Add(new Diagnostic(e.Message, e.Position, e.Position, DiagnosticSeverity.Error));

        foreach (var w in bind.Warnings)
        {
            list.Add(new Diagnostic(w.Message, w.Position, w.Position, DiagnosticSeverity.Warning));
        }

        return list;
    }
}

public enum DiagnosticSeverity { Error, Warning, Information, Hint }

public sealed class Diagnostic(string message, Position start, Position end, DiagnosticSeverity severity)
{
    public string Message { get; } = message;
    public Position Start { get; } = start;
    public Position End { get; } = end;
    public DiagnosticSeverity Severity { get; } = severity;
}

public sealed class DocumentStore
{
    private readonly Dictionary<string, DocumentAnalysis> _docs = new();
    private readonly Lock _lock = new();

    public DocumentAnalysis Analyze(string uri, string source)
    {
        LexerResult lexer;
        ParseResult parse;
        BindResult  bind;

        try { lexer = new Lexer(source).Tokenize(); }
        catch { lexer = new LexerResult([], []); }

        try { parse = new Parser(lexer).Parse(); }
        catch { parse = new ParseResult([], []); }

        try { bind = new Binder().Bind(parse.Statements); }
        catch { bind = new BindResult(new SymbolTable(), [], []); }

        var analysis = new DocumentAnalysis(uri, source, lexer, parse, bind);

        lock (_lock)
            _docs[uri] = analysis;
        return analysis;
    }
    
    public DocumentAnalysis? Get(string uri)
    { 
        lock (_lock) return _docs.GetValueOrDefault(uri);
    } 
    
    public void Remove(string uri)
    {
        lock (_lock) _docs.Remove(uri);
    }

    public IReadOnlyList<DocumentAnalysis> All()
    {
        lock (_lock) return _docs.Values.ToList();
    }
}