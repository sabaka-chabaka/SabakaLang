using System.Text.Json.Nodes;
using SabakaLang.AST;
using SabakaLang.AST.Interfaces;
using SabakaLang.AST.Structures;
using SabakaLang.Binder;
using SabakaLang.Extensions;
using SabakaLang.Runtime;
using SabakaLang.Lexer.Enums;

namespace SabakaLang.LanguageServer;

public static class Features
{
    public static JsonObject Pos(Analysis a, int offset)
    {
        var (l, c) = a.Lines.ToPosition(offset);
        return new JsonObject { ["line"] = l, ["character"] = c };
    }

    public static JsonObject Range(Analysis a, int start, int end) =>
        new() { ["start"] = Pos(a, start), ["end"] = Pos(a, end) };

    public static string Describe(Symbol s)
    {
        switch (s)
        {
            case VariableSymbol v:
                return $"({(v.Kind == SymbolKind.Parameter ? "parameter" : v.IsConst ? "const" : "variable")}) {v.Type} {v.Name}";
            case ConstantSymbol c: return $"(const) {c.Type} {c.Name}";
            case FieldSymbol f: return $"(field) {f.Type} {f.Owner.Name}.{f.Name}";
            case PropertySymbol p: return $"(property) {p.Type} {p.Owner.Name}.{p.Name}";
            case FunctionSymbol fn: return (fn.Owner is null ? "" : fn.Owner.Name + ".") + fn.Signature;
            case FunctionGroupSymbol g: return string.Join("\n", g.Overloads.Select(o => o.Signature));
            case EnumMemberSymbol em: return $"(enum member) {em.Owner.Name}.{em.Name}";
            case ModuleSymbol m: return $"(module) {m.ModuleName}";
            case TypeSymbol t:
            {
                var s2 = $"{t.Kind.ToString().ToLowerInvariant()} {t.Name}";
                var parents = new List<string>();
                if (t.BaseType is not null) parents.Add(t.BaseType.ToString());
                parents.AddRange(t.Interfaces.Select(i => i.ToString()));
                return parents.Count > 0 ? s2 + " : " + string.Join(", ", parents) : s2;
            }
            default: return s.ToString();
        }
    }

    public static JsonNode? Hover(Analysis a, int offset)
    {
        var r = a.RefAt(offset);
        if (r?.Symbol is null) return null;
        var md = "```sabaka\n" + Describe(r.Symbol) + "\n```";
        var desc = r.Symbol is TypeSymbol { Native: { } nc } ? nc.Description : null;
        if (!string.IsNullOrWhiteSpace(desc)) md += "\n\n" + desc;
        return new JsonObject
        {
            ["contents"] = new JsonObject { ["kind"] = "markdown", ["value"] = md },
            ["range"] = Range(a, r.Start, r.End)
        };
    }

    public static JsonNode? Definition(Analysis a, int offset)
    {
        var imp = a.Root.Statements.OfType<ImportStmt>()
            .FirstOrDefault(i => offset >= i.Span.Start.Offset && offset < i.Span.End.Offset);
        if (imp is not null && a.Root.Input.Imports.TryGetValue(imp.Path, out var target) && target is SourceImport si)
        {
            var m = a.Modules.FirstOrDefault(x => x.Name == si.Module);
            if (m is not null && File.Exists(m.Id)) return Location(Paths.ToUri(m.Id), a.Lines, 0, 0);
        }

        var r = a.RefAt(offset);
        var sym = r?.Symbol;
        if (sym is null || sym.IsNative || sym.Span.Start.Line <= 0) return null;
        if (sym is FunctionGroupSymbol g && g.Overloads.Count > 0) sym = g.Overloads[0];

        var module = a.Modules.FirstOrDefault(x => x.Name == sym.Module);
        if (module is null) return null;
        var idx = Locator.Find(module.Code, sym.Name, Locator.KindOf(sym), sym.Span.Start.Offset, sym.Span.End.Offset);
        if (idx < 0) return null;
        var tok = module.Code[idx];
        var lines = module == a.Root ? a.Lines : new LineIndex(module.Source);
        var uri = module == a.Root ? a.Uri : Paths.ToUri(module.Id);
        return Location(uri, lines, tok.Start.Offset, tok.Start.Offset + sym.Name.Length);
    }

    private static JsonObject Location(string uri, LineIndex lines, int start, int end)
    {
        JsonObject P(int o)
        {
            var (l, c) = lines.ToPosition(o);
            return new JsonObject { ["line"] = l, ["character"] = c };
        }
        return new JsonObject
        {
            ["uri"] = uri,
            ["range"] = new JsonObject { ["start"] = P(start), ["end"] = P(end) }
        };
    }

    public static JsonArray DocumentSymbols(Analysis a)
    {
        var code = a.Root.Code;
        var result = new JsonArray();

        int TrimEnd(int end)
        {
            end = Math.Min(end, a.Text.Length);
            while (end > 0 && char.IsWhiteSpace(a.Text[end - 1])) end--;
            return end;
        }

        JsonObject? Make(string name, int kind, Span span, NameKind nk, JsonArray? children = null)
        {
            var idx = Locator.Find(code, name, nk, span.Start.Offset, span.End.Offset);
            if (idx < 0) return null;
            var nameTok = code[idx];
            var start = span.Start.Offset;
            var end = Math.Max(TrimEnd(span.End.Offset), nameTok.Start.Offset + name.Length);
            var o = new JsonObject
            {
                ["name"] = name,
                ["kind"] = kind,
                ["range"] = Range(a, start, end),
                ["selectionRange"] = Range(a, nameTok.Start.Offset, nameTok.Start.Offset + name.Length)
            };
            if (children is not null) o["children"] = children;
            return o;
        }

        JsonArray Methods(IEnumerable<FuncDecl> ms, string owner)
        {
            var arr = new JsonArray();
            foreach (var m in ms)
                if (Make(m.Name, m.Name == owner ? 9 : 6, m.Span, NameKind.Function) is { } n) arr.Add(n);
            return arr;
        }

        JsonArray Fields(IEnumerable<VarDecl> fs)
        {
            var arr = new JsonArray();
            foreach (var f in fs)
                if (Make(f.Name, 8, f.Span, NameKind.Variable) is { } n) arr.Add(n);
            return arr;
        }

        foreach (var s in a.Root.Statements)
        {
            JsonObject? node = s switch
            {
                ClassDecl c => Make(c.Name, 5, c.Span, NameKind.Type, Merge(Fields(c.Fields), Methods(c.Methods, c.Name))),
                InterfaceDecl i => Make(i.Name, 11, i.Span, NameKind.Type, Methods(i.Methods, "")),
                StructDecl st => Make(st.Name, 23, st.Span, NameKind.Type, Fields(st.Fields)),
                EnumDecl e => Make(e.Name, 10, e.Span, NameKind.Type, EnumMembers(a, e)),
                FuncDecl f => Make(f.Name, 12, f.Span, NameKind.Function),
                VarDecl v => Make(v.Name, v.IsConst ? 14 : 13, v.Span, NameKind.Variable),
                ConstDecl cd => Make(cd.Name, 14, cd.Span, NameKind.Variable),
                _ => null
            };
            if (node is not null) result.Add(node);
        }
        return result;
    }

    private static JsonArray Merge(JsonArray a, JsonArray b)
    {
        var res = new JsonArray();
        foreach (var n in a.ToList()) { a.Remove(n); res.Add(n); }
        foreach (var n in b.ToList()) { b.Remove(n); res.Add(n); }
        return res;
    }

    private static JsonArray EnumMembers(Analysis a, EnumDecl e)
    {
        var arr = new JsonArray();
        var code = a.Root.Code;
        foreach (var m in e.Members)
        {
            var idx = Locator.Find(code, m, NameKind.EnumMember, e.Span.Start.Offset, e.Span.End.Offset);
            if (idx < 0) continue;
            var s = code[idx].Start.Offset;
            var r = Range(a, s, s + m.Length);
            arr.Add(new JsonObject { ["name"] = m, ["kind"] = 22, ["range"] = r, ["selectionRange"] = Range(a, s, s + m.Length) });
        }
        return arr;
    }
}

public sealed class CompletionProvider(Analyzer analyzer)
{
    private const string Placeholder = "__cursor__";

    private static readonly string[] Keywords =
    [
        "if", "else", "while", "for", "foreach", "in", "return", "break", "continue", "switch", "case", "default",
        "class", "interface", "struct", "enum", "new", "override", "super", "this", "is", "import", "const",
        "public", "private", "protected", "true", "false", "null", "func"
    ];

    private static readonly string[] BuiltinTypes = ["int", "float", "bool", "string", "char", "void", "dynamic"];

    private sealed record Item(string Name, int Kind, string? Detail);

    private static bool IsIdent(char c) => char.IsLetterOrDigit(c) || c == '_';

    public JsonObject Complete(string uri, string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);

        var plain = analyzer.Analyze(uri, text);
        foreach (var t in plain.Root.Tokens)
            if (t.Type is TokenType.Comment or TokenType.StringLiteral or TokenType.CharLiteral or TokenType.InterpolatedStringLiteral
                && offset > t.Start.Offset && offset <= t.End.Offset)
                return List([]);

        var p = offset;
        while (p > 0 && IsIdent(text[p - 1])) p--;
        var e = offset;
        while (e < text.Length && IsIdent(text[e])) e++;
        var q = p;
        while (q > 0 && (text[q - 1] == ' ' || text[q - 1] == '\t')) q--;
        var afterDot = q > 0 && text[q - 1] == '.';

        var patched = analyzer.Analyze(uri, text[..p] + Placeholder + text[e..]);
        var items = afterDot ? Members(patched, p) : General(patched, p);
        return List(items);
    }

    private static JsonObject List(IEnumerable<Item> items)
    {
        var arr = new JsonArray();
        foreach (var i in items)
        {
            var o = new JsonObject { ["label"] = i.Name, ["kind"] = i.Kind };
            if (i.Detail is not null) o["detail"] = i.Detail;
            arr.Add(o);
        }
        return new JsonObject { ["isIncomplete"] = false, ["items"] = arr };
    }

    private sealed class Finder : AstWalker
    {
        public MemberExpr? Found;
        protected override void OnExpr(IExpr e)
        {
            if (e is MemberExpr m && m.Member == Placeholder) Found = m;
        }
    }

    private static int KindOf(Symbol s) => s.Kind switch
    {
        SymbolKind.Field => 5,
        SymbolKind.Property => 10,
        SymbolKind.Function or SymbolKind.FunctionGroup => 2,
        SymbolKind.EnumMember => 20,
        SymbolKind.Class => 7,
        SymbolKind.Interface => 8,
        SymbolKind.Struct => 22,
        SymbolKind.Enum => 13,
        SymbolKind.Constant => 21,
        SymbolKind.Module => 9,
        SymbolKind.Variable or SymbolKind.Parameter => 6,
        _ => 1
    };

    private static Item ToItem(Symbol s) => new(s.Name, KindOf(s), Features.Describe(s).Split('\n')[0]);

    internal static List<NativeModule> VisibleModules(Analysis a)
    {
        var modules = new List<NativeModule>(a.Registry.AutoImported);
        foreach (var target in a.Root.Input.Imports.Values)
            if (target is NativeImport ni && a.Registry.FindModule(ni.Module) is { } nm && !modules.Contains(nm)) modules.Add(nm);
        return modules;
    }

    private static string Base(string t)
    {
        t = t.TrimEnd('?');
        var lt = t.IndexOf('<');
        if (lt >= 0) t = t[..lt];
        var dot = t.LastIndexOf('.');
        return dot >= 0 ? t[(dot + 1)..] : t;
    }

    private static bool Matches(NativeExtension ext, BoundType bt)
    {
        var pat = TypeSyntax.TryParse(ext.Target);
        if (pat is null) return false;
        if (pat.Name == "dynamic") return true;
        var typeVar = pat.Name.Length == 1 && char.IsUpper(pat.Name[0]);
        var text = bt.ToString().TrimEnd('?');
        if (bt.Kind == TypeKind.Array)
            return pat.ArrayDepth > 0 && (typeVar || text == pat.ToString().TrimEnd('?'));
        if (pat.ArrayDepth > 0) return false;
        return typeVar || Base(text) == Base(pat.Name);
    }

    private static IEnumerable<Item> Members(Analysis a, int pos)
    {
        if (a.Bind is null) return [];
        var finder = new Finder();
        finder.WalkAll(a.Root.Statements);
        if (finder.Found is not { } me) return [];
        var model = a.Bind.Model;

        if (me.Object is NameExpr ne && model.Names.TryGetValue(ne, out var sym))
        {
            if (sym is ModuleSymbol ms) return ms.Exports.Values.Select(ToItem).ToList();
            if (sym is TypeSymbol ets && (ets.Kind == SymbolKind.Enum || ets.Native is not null))
                return ets.Members.Values.Where(m => !m.Name.StartsWith('<')).Select(ToItem).ToList();
        }

        var bt = model.TypeOf(me.Object);
        var result = new Dictionary<string, Item>();
        if (bt.Symbol is TypeSymbol ts)
        {
            var queue = new Queue<TypeSymbol>();
            queue.Enqueue(ts);
            foreach (var i in ts.AllInterfaces()) queue.Enqueue(i);
            var seen = new HashSet<TypeSymbol>();
            while (queue.Count > 0)
            {
                var t = queue.Dequeue();
                if (!seen.Add(t)) continue;
                var inside = t.Module == a.Root.Name && t.Span.Start.Offset <= pos && pos < t.Span.End.Offset;
                foreach (var m in t.Members.Values)
                {
                    if (m.Name.StartsWith('<') || (m.Access == AccessMod.Private && !inside)) continue;
                    if (m is FunctionSymbol { IsConstructor: true }) continue;
                    result.TryAdd(m.Name, ToItem(m));
                }
                if (t.BaseSymbol is { } b) queue.Enqueue(b);
            }
        }
        else if (bt.Kind is TypeKind.Array or TypeKind.String)
        {
            result["length"] = new Item("length", 10, "int");
        }
        if (bt.Kind is not (TypeKind.Error or TypeKind.Void or TypeKind.Null))
            foreach (var ext in VisibleModules(a).SelectMany(m => m.Extensions))
                if (Matches(ext, bt)) result.TryAdd(ext.Name, new Item(ext.Name, ext.IsProperty ? 10 : 2, ext.Signature.ToString()));
        return result.Values;
    }

    private static string TypeText(TypeRef t) =>
        t.Name + (t.TypeArgs.Count > 0 ? "<" + string.Join(",", t.TypeArgs) + ">" : "") +
        string.Concat(Enumerable.Repeat("[]", t.ArrayDepth)) + (t.IsNullable ? "?" : "");

    private IEnumerable<Item> General(Analysis a, int pos)
    {
        var items = new Dictionary<string, Item>();
        foreach (var k in Keywords) items[k] = new Item(k, 14, null);
        foreach (var t in BuiltinTypes) items[t] = new Item(t, 14, "type");

        foreach (var m in VisibleModules(a))
        {
            foreach (var f in m.Functions) items[f.Signature.Name] = new Item(f.Signature.Name, 3, f.Signature.ToString());
            foreach (var c in m.Classes) items[c.Name] = new Item(c.Name, 7, c.Description);
            foreach (var c in m.Constants) items[c.Name] = new Item(c.Name, 21, c.Type);
            foreach (var en in m.Enums) items[en.Name] = new Item(en.Name, 13, null);
        }

        foreach (var target in a.Root.Input.Imports.Values)
            if (target is SourceImport si)
                foreach (var s in a.Modules.Where(x => x.Name == si.Module).SelectMany(x => TopLevel(x.Statements)))
                    items[s.Name] = s;

        foreach (var s in TopLevel(a.Root.Statements)) items[s.Name] = s;
        new Scope(a, pos, items).Run();
        return items.Values;
    }

    private static IEnumerable<Item> TopLevel(IEnumerable<IStmt> stmts)
    {
        foreach (var s in stmts)
            switch (s)
            {
                case ClassDecl c: yield return new Item(c.Name, 7, "class"); break;
                case InterfaceDecl i: yield return new Item(i.Name, 8, "interface"); break;
                case StructDecl st: yield return new Item(st.Name, 22, "struct"); break;
                case EnumDecl e: yield return new Item(e.Name, 13, "enum"); break;
                case FuncDecl f: yield return new Item(f.Name, 3, TypeText(f.ReturnType) + " " + f.Name + "(" + string.Join(", ", f.Params.Select(p => TypeText(p.Type) + " " + p.Name)) + ")"); break;
                case VarDecl v: yield return new Item(v.Name, 6, TypeText(v.Type)); break;
                case ConstDecl cd: yield return new Item(cd.Name, 21, TypeText(cd.Type)); break;
            }
    }

    private sealed class Scope(Analysis a, int pos, Dictionary<string, Item> items)
    {
        public void Run() => Block(a.Root.Statements, true);

        private static bool Contains(Span s, int o) => s.Start.Offset <= o && o < s.End.Offset;

        private void Block(IEnumerable<IStmt> stmts, bool top)
        {
            foreach (var s in stmts)
            {
                if (Contains(s.Span, pos)) Enter(s);
                else if (s.Span.End.Offset <= pos) Declare(s);
            }
        }

        private void Declare(IStmt s)
        {
            switch (s)
            {
                case VarDecl v: items[v.Name] = new Item(v.Name, 6, TypeText(v.Type)); break;
                case ConstDecl c: items[c.Name] = new Item(c.Name, 21, TypeText(c.Type)); break;
                case FuncDecl f: items[f.Name] = new Item(f.Name, 3, f.Name + "(...)"); break;
            }
        }

        private void Sub(List<IStmt>? stmts)
        {
            if (stmts is null || stmts.Count == 0) return;
            if (pos >= stmts[0].Span.Start.Offset && pos <= stmts[^1].Span.End.Offset) Block(stmts, false);
        }

        private void Enter(IStmt s)
        {
            switch (s)
            {
                case FuncDecl f:
                    foreach (var p in f.Params) items[p.Name] = new Item(p.Name, 6, TypeText(p.Type));
                    Sub(f.Body);
                    break;
                case ClassDecl c:
                    items["this"] = new Item("this", 6, c.Name);
                    foreach (var fd in c.Fields) items[fd.Name] = new Item(fd.Name, 5, TypeText(fd.Type));
                    foreach (var m in c.Methods) items[m.Name] = new Item(m.Name, 2, m.Name + "(...)");
                    foreach (var m in c.Methods.Where(m => Contains(m.Span, pos))) Enter(m);
                    break;
                case IfStmt i: Sub(i.Then); Sub(i.Else); break;
                case WhileStmt w: Sub(w.Body); break;
                case ForStmt f:
                    if (f.Init is not null) Declare(f.Init);
                    Sub(f.Body);
                    break;
                case ForeachStmt fe:
                    items[fe.ItemName] = new Item(fe.ItemName, 6, TypeText(fe.ItemType));
                    Sub(fe.Body);
                    break;
                case SwitchStmt sw:
                    foreach (var sc in sw.Cases) Sub(sc.Body);
                    break;
            }
        }
    }
}
