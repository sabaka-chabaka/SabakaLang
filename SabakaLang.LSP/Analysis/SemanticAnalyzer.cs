using SabakaLang.AST;
using SabakaLang.Lexer;

namespace SabakaLang.LSP.Analysis;

/// <summary>
/// Walks the AST of a single file, collecting symbols.
/// Imported symbols are injected before Analyze() via AddGlobalSymbols / AddModuleSymbols.
/// </summary>
public class SemanticAnalyzer
{
    private Scope _currentScope;
    private readonly Scope _globalScope;
    private readonly List<Expr> _ast;
    private readonly List<Symbol> _collected = new();
    private readonly Stack<(int start, int end)> _scopeRanges = new();
    private string? _currentClass = null;

    // alias -> list of symbols from that module (for dot-completion)
    private readonly Dictionary<string, List<Symbol>> _modules = new();

    public IReadOnlyList<Symbol> AllSymbols => _collected;
    public IReadOnlyDictionary<string, List<Symbol>> Modules => _modules;

    public SemanticAnalyzer(List<Expr> ast)
    {
        _ast = ast;
        _globalScope = new Scope();
        _currentScope = _globalScope;
        RegisterBuiltIns();
    }

    // ── injection API ─────────────────────────────────────────────────────────

    /// <summary>Global import (no alias): symbols go into global scope.</summary>
    public void AddGlobalSymbols(IEnumerable<Symbol> symbols)
    {
        foreach (var s in symbols)
        {
            var gs = new Symbol(s.Name, s.Kind, s.Type, s.Start, s.End,
                0, int.MaxValue, s.ParentName, s.SourceFile, s.Parameters, moduleAlias: null);
            _globalScope.Declare(gs);
            _collected.Add(gs);
        }
    }

    /// <summary>Namespaced import (with alias): symbols go into module dict only.</summary>
    public void AddModuleSymbols(string alias, IEnumerable<Symbol> symbols)
    {
        if (!_modules.ContainsKey(alias))
            _modules[alias] = new List<Symbol>();

        foreach (var s in symbols)
        {
            var ms = new Symbol(s.Name, s.Kind, s.Type, s.Start, s.End,
                0, int.MaxValue, s.ParentName, s.SourceFile, s.Parameters, moduleAlias: alias);
            _modules[alias].Add(ms);
        }

        // Also register the alias name itself as a Module symbol so hover/completion knows it
        var aliasSym = new Symbol(alias, SymbolKind.Module, alias, 0, 0,
            0, int.MaxValue, null, null, null, null);
        _globalScope.ForceReplace(aliasSym);
        _collected.RemoveAll(s => s.Name == alias && s.Kind == SymbolKind.Module);
        _collected.Add(aliasSym);
    }

    // ── main entry ────────────────────────────────────────────────────────────

    public void Analyze()
    {
        // Pass 1: hoist top-level declarations so forward-refs work
        foreach (var expr in _ast)
        {
            if (expr is ImportStatement) continue;
            HoistGlobal(expr);
        }
        // Pass 2: full analysis
        foreach (var expr in _ast)
        {
            if (expr is ImportStatement) continue;
            AnalyzeExpr(expr);
        }
    }

    // ── built-ins ─────────────────────────────────────────────────────────────

    private void RegisterBuiltIns()
    {
        void B(string name, string ret, string? parms = null) =>
            Declare(name, SymbolKind.BuiltIn, ret, 0, 0, parameters: parms);

        B("print",        "void",   "value");
        B("sleep",        "void",   "ms");
        B("input",        "string");
        B("readFile",     "string", "path");
        B("writeFile",    "void",   "path, content");
        B("appendFile",   "void",   "path, content");
        B("fileExists",   "bool",   "path");
        B("deleteFile",   "void",   "path");
        B("readLines",    "string[]","path");
        B("time",         "string");
        B("timeMs",       "int");
        B("httpGet",      "string", "url");
        B("httpPost",     "string", "url, body");
        B("httpPostJson", "string", "url, json");
        B("ord",          "int",    "str");
        B("chr",          "string", "code");
    }

    // ── hoist ─────────────────────────────────────────────────────────────────

    private void HoistGlobal(Expr expr)
    {
        switch (expr)
        {
            case FunctionDeclaration f:
                DeclareIfNew(f.Name, SymbolKind.Function,
                    TokenTypeToString(f.ReturnType, f.Name), f.Start, f.End,
                    parameters: ParamsString(f.Parameters));
                break;
            case ClassDeclaration c:
                DeclareIfNew(c.Name, SymbolKind.Class, c.Name, c.Start, c.End);
                break;
            case StructDeclaration s:
                DeclareIfNew(s.Name, SymbolKind.Class, s.Name, s.Start, s.End);
                break;
            case EnumDeclaration e:
                DeclareIfNew(e.Name, SymbolKind.Class, e.Name, e.Start, e.End);
                break;
        }
    }

    // ── full analysis ─────────────────────────────────────────────────────────

    private void AnalyzeExpr(Expr expr)
    {
        switch (expr)
        {
            case VariableDeclaration v:
                if (v.Initializer != null) AnalyzeExpr(v.Initializer);
                var vKind = _currentClass != null ? SymbolKind.Field : SymbolKind.Variable;
                string vType = v.CustomType ?? TokenTypeToString(v.TypeToken, null);
                Declare(v.Name, vKind, vType, v.Start, v.End,
                    v.End, CurrentRange.end, _currentClass);
                break;

            case FunctionDeclaration f:
                var fKind = _currentClass != null ? SymbolKind.Method : SymbolKind.Function;
                string fType = TokenTypeToString(f.ReturnType, f.Name);
                Declare(f.Name, fKind, fType, f.Start, f.End,
                    CurrentRange.start, CurrentRange.end, _currentClass,
                    parameters: ParamsString(f.Parameters));

                var prevScope = _currentScope;
                var prevClass = _currentClass;
                _currentScope = new Scope(prevScope);
                _scopeRanges.Push((f.Start, f.End));
                _currentClass = null;

                foreach (var p in f.Parameters)
                    Declare(p.Name, SymbolKind.Parameter,
                        p.CustomType ?? TokenTypeToString(p.Type, null),
                        f.Start, f.End, f.Start, f.End);

                foreach (var s in f.Body) AnalyzeExpr(s);

                _scopeRanges.Pop();
                _currentScope = prevScope;
                _currentClass = prevClass;
                break;

            case ClassDeclaration c:
                Declare(c.Name, SymbolKind.Class, c.Name, c.Start, c.End,
                    CurrentRange.start, CurrentRange.end);

                var cs = _currentScope;
                _currentScope = new Scope(cs);
                _scopeRanges.Push((c.Start, c.End));
                var pc = _currentClass;
                _currentClass = c.Name;

                Declare("this", SymbolKind.Variable, c.Name,
                    c.Start, c.End, c.Start, c.End);

                foreach (var field in c.Fields)   AnalyzeExpr(field);
                foreach (var method in c.Methods) AnalyzeExpr(method);

                _currentClass = pc;
                _scopeRanges.Pop();
                _currentScope = cs;
                break;

            case StructDeclaration s:
                Declare(s.Name, SymbolKind.Class, s.Name, s.Start, s.End,
                    CurrentRange.start, CurrentRange.end);
                foreach (var fn in s.Fields)
                    Declare(fn, SymbolKind.Field, "unknown",
                        s.Start, s.End, s.Start, s.End, s.Name);
                break;

            case EnumDeclaration e:
                Declare(e.Name, SymbolKind.Class, e.Name, e.Start, e.End,
                    CurrentRange.start, CurrentRange.end);
                foreach (var mn in e.Members)
                    Declare(mn, SymbolKind.Field, e.Name,
                        e.Start, e.End, e.Start, e.End, e.Name);
                break;

            case AssignmentExpr a:
                AnalyzeExpr(a.Value);
                break;

            case BinaryExpr b:
                AnalyzeExpr(b.Left);
                AnalyzeExpr(b.Right);
                break;

            case UnaryExpr u:
                AnalyzeExpr(u.Operand);
                break;

            case CallExpr call:
                if (call.Target != null) AnalyzeExpr(call.Target);
                foreach (var a in call.Arguments) AnalyzeExpr(a);
                break;

            case IfStatement ifs:
                AnalyzeExpr(ifs.Condition);
                WithScope(ifs.Start, ifs.End, () => { foreach (var s in ifs.ThenBlock) AnalyzeExpr(s); });
                if (ifs.ElseBlock != null)
                    WithScope(ifs.Start, ifs.End, () => { foreach (var s in ifs.ElseBlock) AnalyzeExpr(s); });
                break;

            case WhileExpr w:
                AnalyzeExpr(w.Condition);
                WithScope(w.Start, w.End, () => { foreach (var s in w.Body) AnalyzeExpr(s); });
                break;

            case ForStatement f2:
                WithScope(f2.Start, f2.End, () =>
                {
                    if (f2.Initializer != null) AnalyzeExpr(f2.Initializer);
                    if (f2.Condition != null)   AnalyzeExpr(f2.Condition);
                    if (f2.Increment != null)   AnalyzeExpr(f2.Increment);
                    foreach (var s in f2.Body)  AnalyzeExpr(s);
                });
                break;

            case ForeachStatement fe:
                WithScope(fe.Start, fe.End, () =>
                {
                    AnalyzeExpr(fe.Collection);
                    Declare(fe.VarName, SymbolKind.Variable, "unknown",
                        fe.Start, fe.End, fe.Start, fe.End);
                    foreach (var s in fe.Body) AnalyzeExpr(s);
                });
                break;

            case SwitchStatement sw:
                AnalyzeExpr(sw.Expression);
                foreach (var c in sw.Cases)
                    WithScope(sw.Start, sw.End, () =>
                    {
                        if (c.Value != null) AnalyzeExpr(c.Value);
                        foreach (var s in c.Body) AnalyzeExpr(s);
                    });
                break;

            case ReturnStatement r:
                if (r.Value != null) AnalyzeExpr(r.Value);
                break;

            case MemberAccessExpr m:
                AnalyzeExpr(m.Object);
                break;

            case MemberAssignmentExpr ma:
                AnalyzeExpr(ma.Object);
                AnalyzeExpr(ma.Value);
                break;

            case NewExpr n:
                foreach (var a in n.Arguments) AnalyzeExpr(a);
                break;

            case ArrayAccessExpr aa:
                AnalyzeExpr(aa.Array);
                AnalyzeExpr(aa.Index);
                break;

            case ArrayExpr ae:
                foreach (var e in ae.Elements) AnalyzeExpr(e);
                break;

            case ArrayStoreExpr ast2:
                AnalyzeExpr(ast2.Array);
                AnalyzeExpr(ast2.Index);
                AnalyzeExpr(ast2.Value);
                break;
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private (int start, int end) CurrentRange =>
        _scopeRanges.Count > 0 ? _scopeRanges.Peek() : (0, int.MaxValue);

    private void WithScope(int start, int end, Action body)
    {
        var prev = _currentScope;
        _currentScope = new Scope(prev);
        _scopeRanges.Push((start, end));
        body();
        _scopeRanges.Pop();
        _currentScope = prev;
    }

    private bool Declare(string name, SymbolKind kind, string type,
        int start, int end,
        int scopeStart = 0, int scopeEnd = int.MaxValue,
        string? parentName = null, string? parameters = null)
    {
        var sym = new Symbol(name, kind, type, start, end,
            scopeStart, scopeEnd, parentName, null, parameters);
        _collected.Add(sym);
        return _currentScope.Declare(sym);
    }

    private void DeclareIfNew(string name, SymbolKind kind, string type,
        int start, int end, string? parameters = null)
    {
        if (_currentScope.Resolve(name) == null)
            Declare(name, kind, type, start, end, parameters: parameters);
    }

    private static string ParamsString(IEnumerable<Parameter> ps) =>
        string.Join(", ", ps.Select(p => $"{p.CustomType ?? p.Type.ToString()} {p.Name}"));

    private static string TokenTypeToString(TokenType t, string? funcName) => t switch
    {
        TokenType.IntKeyword    => "int",
        TokenType.FloatKeyword  => "float",
        TokenType.StringKeyword => "string",
        TokenType.BoolKeyword   => "bool",
        TokenType.VoidKeyword   => "void",
        TokenType.Identifier    => funcName ?? "unknown",
        _                       => t.ToString()
    };
}