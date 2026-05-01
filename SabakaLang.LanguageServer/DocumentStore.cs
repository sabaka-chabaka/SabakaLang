using SabakaLang.Compiler;
using SabakaLang.Compiler.Binding;
using SabakaLang.Compiler.Lexing;
using SabakaLang.Compiler.Parsing;

namespace SabakaLang.LanguageServer;

public sealed class DocumentAnalysis
{
    public string Uri { get; }
    public string Source { get; }
    public LexerResult Lexer { get; }
    public ParseResult Parse { get; }
    public BindResult Bind { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    
    public DocumentAnalysis(
        string uri,
        string source,
        LexerResult lexer,
        ParseResult parse,
        BindResult bind)
    {
        Uri = uri;
        Source = source;
        Lexer = lexer;
        Parse = parse;
        Bind = bind;
        Diagnostics = BuildDiagnostics(lexer, parse, bind);
    }
    
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

public sealed class Diagnostic
{
    public string Message { get; }
    public Position Start { get; }
    public Position End { get; }
    public DiagnosticSeverity Severity { get; }

    public Diagnostic(string message, Position start, Position end, DiagnosticSeverity severity)
    {
        Message = message;
        Start = start;
        End = end;
        Severity = severity;
    }
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