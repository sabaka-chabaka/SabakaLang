namespace SabakaLang.Compiler;

public enum SymbolKind
{
    Variable,
    Parameter,
    Function,
    Method,
    Field,
    Class,
    Interface,
    Struct,
    Enum,
    EnumMember,
    TypeParam,
    BuiltIn,
    Module
}

public sealed class Symbol
{
    public string Name { get; }
    public SymbolKind Kind { get; }
    public string Type { get; }
    public Span Span { get; }
    public string? ParentName { get; }
    public string? Parameters { get; }
    public IReadOnlyList<string> TypeParams { get; }
    
    public Symbol(
        string name, SymbolKind kind, string type, Span span,
        string? parentName = null, string? parameters = null,
        IReadOnlyList<string>? typeParams = null)
    {
        Name       = name;
        Kind       = kind;
        Type       = type;
        Span       = span;
        ParentName = parentName;
        Parameters = parameters;
        TypeParams = typeParams ?? [];
    }
    
    public override string ToString() => ParentName is null
        ? $"{Kind} {Name} : {Type}"
        : $"{Kind} {ParentName}.{Name} : {Type}";
}

public sealed class Scope
{
    public Scope? Parent { get; }
    private readonly Dictionary<string, Symbol> _symbols = new();
    public IEnumerable<Symbol> LocalSymbols => _symbols.Values;
 
    public Scope(Scope? parent = null) => Parent = parent;
 
    public bool Declare(Symbol symbol)
    {
        if (_symbols.ContainsKey(symbol.Name)) return false;
        _symbols[symbol.Name] = symbol;
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

public sealed class SymbolTable
{
    private readonly List<Symbol> _all = [];
    public IReadOnlyList<Symbol> All => _all;
    
    internal void Add(Symbol symbol) => _all.Add(symbol);
    
    public IEnumerable<Symbol> Lookup(string name) => _all.Where(s => s.Name == name);
    
    public IEnumerable<Symbol> MembersOf(string typeName) => _all.Where(s => s.ParentName == typeName);
    
    public Symbol? SymbolAt(int offset) => _all.FirstOrDefault(s => s.Span.Start.Offset <= offset && offset <= s.Span.End.Offset);
}

public readonly record struct BindError(string Message, Position Position)
{
    public override string ToString() => $"[{Position.Line}:{Position.Column}] {Message}";
}

public sealed class BindResult
{
    public SymbolTable              Symbols   { get; }
    public IReadOnlyList<BindError> Errors    { get; }
    public bool                     HasErrors => Errors.Count > 0;
 
    public BindResult(SymbolTable symbols, IReadOnlyList<BindError> errors)
    {
        Symbols = symbols;
        Errors  = errors;
    }
}

public class Binder
{
    private Scope _scope;
    private readonly Scope _globalScope;
    private readonly SymbolTable _table = new();
    private readonly List<BindError> _errors = [];

    private string? _currentType = null;
    private string? _currentReturn = null;
    private static readonly HashSet<string> BuiltinTypes =
        ["int", "float", "bool", "string", "void"];
    
    public Binder()
    {
        _globalScope = new Scope();
        _scope       = _globalScope;
        RegisterBuiltIns();
    }
    
    public void AddGlobalSymbols(IEnumerable<Symbol> symbols)
    {
        foreach (var s in symbols)
        {
            _globalScope.Declare(s);
            _table.Add(s);
        }
    }
 
    public BindResult Bind(IReadOnlyList<IStmt> statements)
    {
        foreach (var stmt in statements)
            HoistTopLevel(stmt);
 
        foreach (var stmt in statements)
            BindStmt(stmt);
 
        return new BindResult(_table, _errors);
    }
    
    private void RegisterBuiltIns()
    {
        void B(string name, string ret, string parms = "") =>
            DeclareGlobal(name, SymbolKind.BuiltIn, ret,
                new Span(default, default), parameters: parms);
 
        B("print",        "void",     "value");
        B("sleep",        "void",     "int ms");
        B("input",        "string");
        B("readFile",     "string",   "string path");
        B("writeFile",    "void",     "string path, string content");
        B("appendFile",   "void",     "string path, string content");
        B("fileExists",   "bool",     "string path");
        B("deleteFile",   "void",     "string path");
        B("readLines",    "string[]", "string path");
        B("time",         "string");
        B("timeMs",       "int");
        B("httpGet",      "string",   "string url");
        B("httpPost",     "string",   "string url, string body");
        B("httpPostJson", "string",   "string url, string json");
        B("ord",          "int",      "string str");
        B("chr",          "string",   "int code");
    }
    
    private void HoistTopLevel(IStmt stmt)
    {
        switch (stmt)
        {
            case FuncDecl f:
                DeclareIfAbsent(f.Name, SymbolKind.Function,
                    TypeRefToString(f.ReturnType), f.Span,
                    parameters: ParamsString(f.Params),
                    typeParams:  f.TypeParams.Select(tp => tp.Name).ToArray());
                break;
 
            case ClassDecl c:
                DeclareIfAbsent(c.Name, SymbolKind.Class, c.Name, c.Span,
                    typeParams: c.TypeParams.Select(tp => tp.Name).ToArray());
                break;
 
            case InterfaceDecl i:
                DeclareIfAbsent(i.Name, SymbolKind.Interface, i.Name, i.Span,
                    typeParams: i.TypeParams.Select(tp => tp.Name).ToArray());
                break;
 
            case StructDecl s:
                DeclareIfAbsent(s.Name, SymbolKind.Struct, s.Name, s.Span);
                break;
 
            case EnumDecl e:
                DeclareIfAbsent(e.Name, SymbolKind.Enum, e.Name, e.Span);
                break;
        }
    }

    private void BindStmt(IStmt stmt)
    {
        switch (stmt)
        {
            case ImportStmt imp:     BindImport(imp);      break;
            case VarDecl    v:       BindVarDecl(v);       break;
            case FuncDecl   f:       BindFuncDecl(f);      break;
            case ClassDecl  c:       BindClassDecl(c);     break;
            case InterfaceDecl i:    BindInterfaceDecl(i); break;
            case StructDecl s:       BindStructDecl(s);    break;
            case EnumDecl   e:       BindEnumDecl(e);      break;
            case IfStmt     ifs:     BindIf(ifs);          break;
            case WhileStmt  w:       BindWhile(w);         break;
            case ForStmt    f2:      BindFor(f2);          break;
            case ForeachStmt fe:     BindForeach(fe);      break;
            case SwitchStmt sw:      BindSwitch(sw);       break;
            case ReturnStmt ret:     BindReturn(ret);      break;
            case ExprStmt   es:      BindExpr(es.Expr);    break;
        }
    }
    
    private void BindImport(ImportStmt imp)
    {
        if (imp.Alias is not null)
        {
            var sym = new Symbol(imp.Alias, SymbolKind.Module, imp.Alias, imp.Span);
            DeclareSymbol(sym, imp.Span.Start);
        }
    }

    private void BindVarDecl(VarDecl v, string? forceParent = null)
    {
        if (v.Init is not null) BindExpr(v.Init);
 
        var kind = (forceParent ?? _currentType) is not null
            ? SymbolKind.Field
            : SymbolKind.Variable;
 
        var sym = new Symbol(v.Name, kind, TypeRefToString(v.Type), v.Span,
            parentName: forceParent ?? _currentType);
        DeclareSymbol(sym, v.Span.Start);
    }
    
    private void BindFuncDecl(FuncDecl f, string? ownerType = null)
    {
        var kind = ownerType is not null ? SymbolKind.Method : SymbolKind.Function;
 
        var funcSym = new Symbol(f.Name, kind, TypeRefToString(f.ReturnType), f.Span,
            parentName: ownerType,
            parameters: ParamsString(f.Params),
            typeParams:  f.TypeParams.Select(tp => tp.Name).ToArray());
        _scope.ForceReplace(funcSym);
        _table.Add(funcSym);
 
        if (f.IsOverride && ownerType is null)
            AddError("'override' is only valid inside a class.", f.Span.Start);
 
        var savedReturn = _currentReturn;
        _currentReturn  = TypeRefToString(f.ReturnType);
 
        WithScope(() =>
        {
            foreach (var tp in f.TypeParams)
            {
                var tpSym = new Symbol(tp.Name, SymbolKind.TypeParam, tp.Name, f.Span);
                DeclareSymbol(tpSym, f.Span.Start);
            }
            foreach (var p in f.Params)
            {
                var pSym = new Symbol(p.Name, SymbolKind.Parameter, TypeRefToString(p.Type), f.Span);
                DeclareSymbol(pSym, f.Span.Start);
            }
            foreach (var s in f.Body) BindStmt(s);
        });
 
        _currentReturn = savedReturn;
    }
    
    private void BindClassDecl(ClassDecl c)
    {
        if (c.Base is not null && _scope.Resolve(c.Base) is null)
            AddError($"Unknown base class '{c.Base}'.", c.Span.Start);
 
        foreach (var iface in c.Interfaces)
            if (_scope.Resolve(iface) is null)
                AddError($"Unknown interface '{iface}'.", c.Span.Start);
 
        var savedType = _currentType;
        _currentType  = c.Name;
 
        WithScope(() =>
        {
            foreach (var tp in c.TypeParams)
            {
                var tpSym = new Symbol(tp.Name, SymbolKind.TypeParam, tp.Name, c.Span);
                DeclareSymbol(tpSym, c.Span.Start);
            }
 
            var thisSym = new Symbol("this", SymbolKind.Variable, c.Name, c.Span);
            _scope.Declare(thisSym);
            _table.Add(thisSym);
 
            foreach (var m in c.Methods)
            {
                var ms = new Symbol(m.Name, SymbolKind.Method, TypeRefToString(m.ReturnType),
                    m.Span, parentName: c.Name,
                    parameters: ParamsString(m.Params),
                    typeParams:  m.TypeParams.Select(tp => tp.Name).ToArray());
                _scope.Declare(ms);
                _table.Add(ms);
            }
 
            foreach (var field  in c.Fields)  BindVarDecl(field,  c.Name);
            foreach (var method in c.Methods) BindFuncDecl(method, c.Name);
        });
 
        _currentType = savedType;
    }
 
    private void BindInterfaceDecl(InterfaceDecl i)
    {
        foreach (var parent in i.Parents)
            if (_scope.Resolve(parent) is null)
                AddError($"Unknown parent interface '{parent}'.", i.Span.Start);
 
        var savedType = _currentType;
        _currentType  = i.Name;
 
        WithScope(() =>
        {
            foreach (var tp in i.TypeParams)
            {
                var tpSym = new Symbol(tp.Name, SymbolKind.TypeParam, tp.Name, i.Span);
                DeclareSymbol(tpSym, i.Span.Start);
            }
 
            foreach (var m in i.Methods)
            {
                var ms = new Symbol(m.Name, SymbolKind.Method, TypeRefToString(m.ReturnType),
                    m.Span, parentName: i.Name, parameters: ParamsString(m.Params));
                DeclareSymbol(ms, m.Span.Start);
            }
        });
 
        _currentType = savedType;
    }

    private void BindStructDecl(StructDecl s)
    {
        var savedType = _currentType;
        _currentType  = s.Name;

        WithScope(() =>
        {
            foreach (var field in s.Fields)
                BindVarDecl(field, s.Name);
        });
 
        _currentType = savedType;
    }

    private void BindEnumDecl(EnumDecl e)
    {
        foreach (var member in e.Members)
        {
            var ms = new Symbol(member, SymbolKind.EnumMember, e.Name, e.Span, parentName: e.Name);
            DeclareSymbol(ms, e.Span.Start);
        }
    }
    
    private void BindIf(IfStmt s)
    {
        BindExpr(s.Condition);
        WithScope(() => { foreach (var st in s.Then) BindStmt(st); });
        if (s.Else is not null)
            WithScope(() => { foreach (var st in s.Else) BindStmt(st); });
    }
 
    private void BindWhile(WhileStmt s)
    {
        BindExpr(s.Condition);
        WithScope(() => { foreach (var st in s.Body) BindStmt(st); });
    }
 
    private void BindFor(ForStmt s)
    {
        WithScope(() =>
        {
            if (s.Init is not null) BindStmt(s.Init);
            if (s.Condition is not null) BindExpr(s.Condition);
            if (s.Step is not null) BindExpr(s.Step);
            foreach (var st in s.Body) BindStmt(st);
        });
    }
 
    private void BindForeach(ForeachStmt s)
    {
        BindExpr(s.Collection);
        WithScope(() =>
        {
            var itemSym = new Symbol(s.ItemName, SymbolKind.Variable,
                TypeRefToString(s.ItemType), s.Span);
            DeclareSymbol(itemSym, s.Span.Start);
            foreach (var st in s.Body) BindStmt(st);
        });
    }
    
    private void BindSwitch(SwitchStmt s)
    {
        BindExpr(s.Value);
        foreach (var c in s.Cases)
        {
            if (c.Value is not null) BindExpr(c.Value);
            WithScope(() => { foreach (var st in c.Body) BindStmt(st); });
        }
    }
 
    private void BindReturn(ReturnStmt r)
    {
        if (r.Value is not null) BindExpr(r.Value);
 
        if (_currentReturn is null)
            AddError("'return' outside a function.", r.Span.Start);
        else if (r.Value is null && _currentReturn != "void")
            AddError($"Expected a return value of type '{_currentReturn}'.", r.Span.Start);
        else if (r.Value is not null && _currentReturn == "void")
            AddError("Cannot return a value from a void function.", r.Span.Start);
    }
    
    private void BindExpr(IExpr expr)
    {
        switch (expr)
        {
            case IntLit:
            case FloatLit:
            case StringLit:
            case BoolLit:
                break;
 
            case NameExpr n:
                if (_scope.Resolve(n.Name) is null)
                    AddError($"Undefined symbol '{n.Name}'.", n.Span.Start);
                break;
 
            case BinaryExpr b:
                BindExpr(b.Left);
                BindExpr(b.Right);
                break;
 
            case UnaryExpr u:
                BindExpr(u.Operand);
                break;
 
            case AssignExpr a:
                BindExpr(a.Target);
                BindExpr(a.Value);
                break;
 
            case CallExpr call:
                BindExpr(call.Callee);
                foreach (var arg in call.Args) BindExpr(arg);
                break;
 
            case MemberExpr m:
                BindExpr(m.Object);
                break;
 
            case IndexExpr idx:
                BindExpr(idx.Object);
                BindExpr(idx.Index);
                break;
 
            case ArrayExpr arr:
                foreach (var el in arr.Elements) BindExpr(el);
                break;
 
            case NewExpr nw:
                if (_scope.Resolve(nw.TypeName) is null)
                    AddError($"Unknown type '{nw.TypeName}'.", nw.Span.Start);
                foreach (var arg in nw.Args) BindExpr(arg);
                break;
 
            case SuperExpr sup:
                if (_currentType is null)
                    AddError("'super' used outside a class.", sup.Span.Start);
                break;
        }
    }

    private void WithScope(Action body)
    {
        var prev = _scope;
        _scope = new Scope(prev);
        body();
        _scope = prev;
    }
    
    private void DeclareSymbol(Symbol sym, Position pos)
    {
        if (!_scope.Declare(sym))
            AddError($"'{sym.Name}' is already declared in this scope.", pos);
        _table.Add(sym);
    }

    private void DeclareGlobal(string name, SymbolKind kind, string type, Span span,
        string? parameters = null, string[]? typeParams = null)
    {
        var sym = new Symbol(name, kind, type, span, parameters: parameters, typeParams: typeParams);
        _globalScope.ForceReplace(sym);
        _table.Add(sym);
    }
    
    private void DeclareIfAbsent(string name, SymbolKind kind, string type, Span span,
        string? parameters = null, string[]? typeParams = null)
    {
        if (_scope.Resolve(name) is not null) return;
        var sym = new Symbol(name, kind, type, span,
            parameters: parameters, typeParams: typeParams);
        _scope.Declare(sym);
        _table.Add(sym);
    }
    
    private void AddError(string message, Position pos) =>
        _errors.Add(new BindError(message, pos));
    
    private static string TypeRefToString(TypeRef t)
    {
        var sb = new System.Text.StringBuilder(t.Name);
        if (t.TypeArgs.Count > 0)
        {
            sb.Append('<');
            sb.Append(string.Join(", ", t.TypeArgs));
            sb.Append('>');
        }
        if (t.IsArray) sb.Append("[]");
        return sb.ToString();
    }
 
    private static string ParamsString(IEnumerable<Param> ps) =>
        string.Join(", ", ps.Select(p => $"{TypeRefToString(p.Type)} {p.Name}"));
}