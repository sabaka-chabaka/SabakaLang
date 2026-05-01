using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SabakaLang.Compiler;
using SabakaLang.Compiler.AST;

namespace SabakaLang.LanguageServer.Handlers;

public sealed class FoldingRangeHandler(DocumentStore store) : FoldingRangeHandlerBase
{
    protected override FoldingRangeRegistrationOptions CreateRegistrationOptions(
        FoldingRangeCapability capability,
        ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = SabakaDocumentSelector.Instance };

    public override Task<Container<FoldingRange>?> Handle(
        FoldingRangeRequestParam request, CancellationToken ct)
    {
        var analysis = store.Get(request.TextDocument.Uri.ToString());
        if (analysis is null) return Task.FromResult<Container<FoldingRange>?>(null);

        var ranges = new List<FoldingRange>();

        foreach (var stmt in analysis.Parse.Statements)
            CollectFolds(stmt, ranges);

        var imports = analysis.Parse.Statements
            .OfType<ImportStmt>()
            .ToList();
        if (imports.Count > 1)
        {
            ranges.Add(new FoldingRange
            {
                StartLine = PositionHelper.ToLsp(imports.First().Span.Start).Line,
                EndLine   = PositionHelper.ToLsp(imports.Last().Span.End).Line,
                Kind = FoldingRangeKind.Imports
            });
        }

        return Task.FromResult<Container<FoldingRange>?>(new Container<FoldingRange>(ranges));
    }

    private static void CollectFolds(IStmt stmt, List<FoldingRange> ranges)
    {
        switch (stmt)
        {
            case ClassDecl c:
                AddFold(ranges, c.Span);
                foreach (var m in c.Methods) CollectFolds(m, ranges);
                break;

            case InterfaceDecl i:
                AddFold(ranges, i.Span);
                foreach (var m in i.Methods) CollectFolds(m, ranges);
                break;

            case StructDecl s:
                AddFold(ranges, s.Span);
                break;

            case EnumDecl e:
                AddFold(ranges, e.Span);
                break;

            case FuncDecl f:
                AddFold(ranges, f.Span);
                foreach (var s in f.Body) CollectFolds(s, ranges);
                break;

            case IfStmt ifs:
                AddFold(ranges, ifs.Span);
                foreach (var s in ifs.Then) CollectFolds(s, ranges);
                if (ifs.Else is not null)
                    foreach (var s in ifs.Else) CollectFolds(s, ranges);
                break;

            case WhileStmt w:
                AddFold(ranges, w.Span);
                foreach (var s in w.Body) CollectFolds(s, ranges);
                break;

            case ForStmt f2:
                AddFold(ranges, f2.Span);
                foreach (var s in f2.Body) CollectFolds(s, ranges);
                break;

            case ForeachStmt fe:
                AddFold(ranges, fe.Span);
                foreach (var s in fe.Body) CollectFolds(s, ranges);
                break;

            case SwitchStmt sw:
                AddFold(ranges, sw.Span);
                foreach (var c in sw.Cases)
                    foreach (var s in c.Body) CollectFolds(s, ranges);
                break;
        }
    }

    private static void AddFold(List<FoldingRange> ranges, Span span)
    {
        var startLine = PositionHelper.ToLsp(span.Start).Line;
        var endLine   = PositionHelper.ToLsp(span.End).Line;
        if (endLine > startLine)
        {
            ranges.Add(new FoldingRange
            {
                StartLine = startLine,
                EndLine   = endLine,
                Kind = FoldingRangeKind.Region
            });
        }
    }
}
