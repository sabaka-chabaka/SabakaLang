using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SabakaLang.Compiler;
using SymbolKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind;

namespace SabakaLang.LanguageServer.Handlers;

public sealed class DocumentSymbolHandler(DocumentStore store) : DocumentSymbolHandlerBase
{
    protected override DocumentSymbolRegistrationOptions CreateRegistrationOptions(
        DocumentSymbolCapability capability,
        ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = SabakaDocumentSelector.Instance };

    public override Task<SymbolInformationOrDocumentSymbolContainer?> Handle(
        DocumentSymbolParams request, CancellationToken ct)
    {
        var analysis = store.Get(request.TextDocument.Uri.ToString());
        if (analysis is null)
            return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(null);

        var result = new List<SymbolInformationOrDocumentSymbol>();

        foreach (var stmt in analysis.Parse.Statements)
        {
            var docSym = ToDocumentSymbol(stmt);
            if (docSym is not null)
                result.Add(new SymbolInformationOrDocumentSymbol(docSym));
        }

        return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(
            new SymbolInformationOrDocumentSymbolContainer(result));
    }

    private static DocumentSymbol? ToDocumentSymbol(IStmt stmt) =>
        stmt switch
        {
            ClassDecl c => new DocumentSymbol
            {
                Name = c.Name,
                Kind = SymbolKind2.Class,
                Range = PositionHelper.ToLspRange(c.Span),
                SelectionRange = PositionHelper.ToLspRange(c.Span),
                Detail = c.Base is not null ? $": {c.Base}" : null,
                Children = new Container<DocumentSymbol>(
                    c.Fields.Select(f => FieldSymbol(f, c.Name))
                        .Concat(c.Methods.Select(m => MethodSymbol(m))))
            },

            InterfaceDecl i => new DocumentSymbol
            {
                Name = i.Name,
                Kind = SymbolKind2.Interface,
                Range = PositionHelper.ToLspRange(i.Span),
                SelectionRange = PositionHelper.ToLspRange(i.Span),
                Children = new Container<DocumentSymbol>(
                    i.Methods.Select(m => MethodSymbol(m)))
            },

            StructDecl s => new DocumentSymbol
            {
                Name = s.Name,
                Kind = SymbolKind2.Struct,
                Range = PositionHelper.ToLspRange(s.Span),
                SelectionRange = PositionHelper.ToLspRange(s.Span),
                Children = new Container<DocumentSymbol>(
                    s.Fields.Select(f => FieldSymbol(f, s.Name)))
            },

            EnumDecl e => new DocumentSymbol
            {
                Name = e.Name,
                Kind = SymbolKind2.Enum,
                Range = PositionHelper.ToLspRange(e.Span),
                SelectionRange = PositionHelper.ToLspRange(e.Span),
                Children = new Container<DocumentSymbol>(
                    e.Members.Select(m => new DocumentSymbol
                    {
                        Name = m,
                        Kind = SymbolKind2.EnumMember,
                        Range = PositionHelper.ToLspRange(e.Span),
                        SelectionRange = PositionHelper.ToLspRange(e.Span)
                    }))
            },

            FuncDecl f => MethodSymbol(f),

            VarDecl v => new DocumentSymbol
            {
                Name = v.Name,
                Kind = SymbolKind2.Variable,
                Range = PositionHelper.ToLspRange(v.Span),
                SelectionRange = PositionHelper.ToLspRange(v.Span),
                Detail = TypeRefToString(v.Type)
            },

            _ => null
        };

    private static DocumentSymbol FieldSymbol(VarDecl f, string parent) =>
        new()
        {
            Name = f.Name,
            Kind = SymbolKind2.Field,
            Range = PositionHelper.ToLspRange(f.Span),
            SelectionRange = PositionHelper.ToLspRange(f.Span),
            Detail = TypeRefToString(f.Type)
        };

    private static DocumentSymbol MethodSymbol(FuncDecl m) =>
        new()
        {
            Name = m.Name,
            Kind = SymbolKind2.Method,
            Range = PositionHelper.ToLspRange(m.Span),
            SelectionRange = PositionHelper.ToLspRange(m.Span),
            Detail = $"{TypeRefToString(m.ReturnType)} ({ParamsString(m.Params)})"
        };

    private static string TypeRefToString(TypeRef t)
    {
        var s = t.Name;
        if (t.TypeArgs.Count > 0) s += $"<{string.Join(", ", t.TypeArgs)}>";
        if (t.IsArray) s += "[]";
        return s;
    }

    private static string ParamsString(IEnumerable<Param> ps) =>
        string.Join(", ", ps.Select(p => $"{TypeRefToString(p.Type)} {p.Name}"));
}

file static class SymbolKind2
{
    public static readonly SymbolKind Class     = SymbolKind.Class;
    public static readonly SymbolKind Interface = SymbolKind.Interface;
    public static readonly SymbolKind Struct    = SymbolKind.Struct;
    public static readonly SymbolKind Enum      = SymbolKind.Enum;
    public static readonly SymbolKind EnumMember= SymbolKind.EnumMember;
    public static readonly SymbolKind Method    = SymbolKind.Method;
    public static readonly SymbolKind Field     = SymbolKind.Field;
    public static readonly SymbolKind Variable  = SymbolKind.Variable;
}
