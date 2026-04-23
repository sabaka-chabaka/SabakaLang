using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Avalonia.Media;
using SabakaLang.Compiler;
using SabakaLang.LanguageServer;

namespace SabakaLang.Studio.Completion;

public sealed class SabakaCompletionData(
    string text,
    string detail,
    CompletionIcon icon,
    string insertText) : ICompletionData
{
    public string        Text       { get; } = text;
    public string        Detail     { get; } = detail;
    public string        InsertText { get; } = insertText;
    public CompletionIcon Icon      { get; } = icon;

    public IImage? Image => CompletionIconProvider.GetIcon(Icon);
    public object   Description => Detail;
    public double   Priority    => 0;
    public object   Content     => Text;

    public void Complete(TextArea area, ISegment completionSegment, EventArgs args)
    {
        area.Document.Replace(completionSegment, InsertText);
        // Place caret inside parens for callables
        if (InsertText.EndsWith("()"))
            area.Caret.Offset -= 1;
    }
}

public enum CompletionIcon
{
    Keyword, Variable, Function, Method,
    Class, Interface, Struct, Enum, EnumMember,
    Property, Namespace, TypeParam
}

public sealed class SabakaCompletionProvider
{
    private static readonly string[] Keywords =
    [
        "if", "else", "while", "for", "foreach", "in", "return",
        "class", "interface", "struct", "enum", "import", "new",
        "override", "super", "null", "is", "switch", "case", "default",
        "true", "false", "public", "private", "protected",
        "int", "float", "bool", "string", "void"
    ];

    private readonly DocumentStore _store;
    private const string DocUri = "studio://active-document";

    public SabakaCompletionProvider(DocumentStore store)
    {
        _store = store;
    }
    
    public IReadOnlyList<SabakaCompletionData> GetCompletions(string source, int offset)
    {
        var analysis = _store.Analyze(DocUri, source);

        if (IsInsideStringOrComment(analysis, offset))
            return [];

        if (TryGetMemberPrefix(source, offset, out var memberOf))
            return GetMemberCompletions(analysis, memberOf);

        if (TryGetStaticPrefix(source, offset, out var staticOf))
            return GetStaticCompletions(analysis, staticOf);

        var prefix = GetWordPrefix(source, offset);
        return GetGeneralCompletions(analysis, prefix);
    }

    private static bool IsInsideStringOrComment(DocumentAnalysis analysis, int offset)
    {
        foreach (var t in analysis.Lexer.Tokens)
        {
            if (t.Type is TokenType.Eof) break;
            if (t.Type is TokenType.StringLiteral
                       or TokenType.InterpolatedStringLiteral
                       or TokenType.Comment)
            {
                if (t.Start.Offset <= offset && offset <= t.End.Offset + 1)
                    return true;
            }
        }
        return false;
    }

    private static bool TryGetMemberPrefix(string source, int offset, out string objectName)
    {
        objectName = "";
        int pos = offset - 1;
        while (pos >= 0 && char.IsWhiteSpace(source[pos])) pos--;
        if (pos < 0 || source[pos] != '.') return false;
        pos--;
        int end = pos + 1;
        while (pos >= 0 && IsIdentChar(source[pos])) pos--;
        objectName = source[(pos + 1)..end];
        return objectName.Length > 0;
    }

    private static bool TryGetStaticPrefix(string source, int offset, out string typeName)
    {
        typeName = "";
        int pos = offset - 1;
        while (pos >= 0 && char.IsWhiteSpace(source[pos])) pos--;
        if (pos < 1 || source[pos] != ':' || source[pos - 1] != ':') return false;
        pos -= 2;
        int end = pos + 1;
        while (pos >= 0 && IsIdentChar(source[pos])) pos--;
        typeName = source[(pos + 1)..end];
        return typeName.Length > 0;
    }

    private static string GetWordPrefix(string source, int offset)
    {
        int pos = offset - 1;
        while (pos >= 0 && IsIdentChar(source[pos])) pos--;
        return source[(pos + 1)..offset];
    }

    private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static IReadOnlyList<SabakaCompletionData> GetMemberCompletions(
        DocumentAnalysis analysis, string objectName)
    {
        var result = new List<SabakaCompletionData>();

        var sym      = analysis.Bind.Symbols.Lookup(objectName).FirstOrDefault();
        var typeName = sym?.Type ?? objectName;
        var baseType = typeName.TrimEnd('[', ']');

        foreach (var member in analysis.Bind.Symbols.MembersOf(baseType))
            result.Add(SymbolToData(member));

        if (baseType == "string")
            result.AddRange(StringBuiltins());

        if (typeName.EndsWith("[]"))
        {
            result.Add(new SabakaCompletionData("length",   "int length",           CompletionIcon.Property, "length"));
            result.Add(new SabakaCompletionData("push",     "void push(value)",     CompletionIcon.Method,   "push()"));
            result.Add(new SabakaCompletionData("pop",      "T pop()",              CompletionIcon.Method,   "pop()"));
            result.Add(new SabakaCompletionData("contains", "bool contains(value)", CompletionIcon.Method,   "contains()"));
            result.Add(new SabakaCompletionData("indexOf",  "int indexOf(value)",   CompletionIcon.Method,   "indexOf()"));
        }

        return result;
    }

    private static IReadOnlyList<SabakaCompletionData> GetStaticCompletions(
        DocumentAnalysis analysis, string typeName)
    {
        return analysis.Bind.Symbols
            .MembersOf(typeName)
            .Where(s => s.Kind == SymbolKind.EnumMember)
            .Select(s => new SabakaCompletionData(
                s.Name,
                $"{typeName}::{s.Name}",
                CompletionIcon.EnumMember,
                s.Name))
            .ToList();
    }

    private static IReadOnlyList<SabakaCompletionData> GetGeneralCompletions(
        DocumentAnalysis analysis, string prefix)
    {
        var result = new List<SabakaCompletionData>();
        var seen   = new HashSet<string>();

        foreach (var sym in analysis.Bind.Symbols.All)
        {
            if (sym.ParentName is not null &&
                sym.Kind is SymbolKind.Field or SymbolKind.Method or SymbolKind.EnumMember)
                continue;

            if (!seen.Add(sym.Name)) continue;

            if (prefix.Length > 0 &&
                !sym.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(SymbolToData(sym));
        }

        foreach (var kw in Keywords)
        {
            if (!seen.Contains(kw) &&
                (prefix.Length == 0 ||
                 kw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(new SabakaCompletionData(kw, "keyword", CompletionIcon.Keyword, kw));
            }
        }

        result.Sort((a, b) =>
        {
            bool aKw = a.Icon == CompletionIcon.Keyword;
            bool bKw = b.Icon == CompletionIcon.Keyword;
            if (aKw != bKw) return aKw ? 1 : -1;
            return string.Compare(a.Text, b.Text, StringComparison.OrdinalIgnoreCase);
        });

        return result;
    }

    private static SabakaCompletionData SymbolToData(Symbol sym)
    {
        var icon = sym.Kind switch
        {
            SymbolKind.Class      => CompletionIcon.Class,
            SymbolKind.Interface  => CompletionIcon.Interface,
            SymbolKind.Struct     => CompletionIcon.Struct,
            SymbolKind.Enum       => CompletionIcon.Enum,
            SymbolKind.EnumMember => CompletionIcon.EnumMember,
            SymbolKind.Function   => CompletionIcon.Function,
            SymbolKind.Method     => CompletionIcon.Method,
            SymbolKind.BuiltIn    => CompletionIcon.Function,
            SymbolKind.Field      => CompletionIcon.Property,
            SymbolKind.Parameter  => CompletionIcon.Variable,
            SymbolKind.TypeParam  => CompletionIcon.TypeParam,
            SymbolKind.Module     => CompletionIcon.Namespace,
            _                     => CompletionIcon.Variable
        };

        bool callable = sym.Kind is SymbolKind.Function or SymbolKind.Method or SymbolKind.BuiltIn;
        string detail = callable
            ? $"{sym.Type} {sym.Name}({sym.Parameters ?? ""})"
            : $"{sym.Type} {sym.Name}";
        string insert = callable ? $"{sym.Name}()" : sym.Name;

        return new SabakaCompletionData(sym.Name, detail, icon, insert);
    }

    private static IEnumerable<SabakaCompletionData> StringBuiltins()
    {
        var methods = new (string name, string ret, string parms)[]
        {
            ("length",     "int",      ""),
            ("toUpper",    "string",   ""),
            ("toLower",    "string",   ""),
            ("trim",       "string",   ""),
            ("contains",   "bool",     "string value"),
            ("startsWith", "bool",     "string prefix"),
            ("endsWith",   "bool",     "string suffix"),
            ("indexOf",    "int",      "string value"),
            ("replace",    "string",   "string old, string new"),
            ("split",      "string[]", "string separator"),
            ("substring",  "string",   "int start, int length"),
            ("charAt",     "string",   "int index"),
            ("toInt",      "int",      ""),
            ("toFloat",    "float",    ""),
        };
        foreach (var (name, ret, parms) in methods)
        {
            bool hasParams = parms.Length > 0;
            yield return new SabakaCompletionData(
                name,
                $"{ret} {name}({parms})",
                hasParams ? CompletionIcon.Method : CompletionIcon.Property,
                hasParams ? $"{name}()" : name);
        }
    }
}