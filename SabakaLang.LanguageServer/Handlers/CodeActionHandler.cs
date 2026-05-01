using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SabakaLang.Compiler.AST;

namespace SabakaLang.LanguageServer.Handlers;

public sealed class CodeActionHandler(DocumentStore store) : CodeActionHandlerBase
{
    protected override CodeActionRegistrationOptions CreateRegistrationOptions(
        CodeActionCapability capability,
        ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = SabakaDocumentSelector.Instance,
            CodeActionKinds = new Container<CodeActionKind>(
                CodeActionKind.QuickFix,
                CodeActionKind.SourceOrganizeImports)
        };

    public override Task<CommandOrCodeActionContainer?> Handle(
        CodeActionParams request, CancellationToken ct)
    {
        var analysis = store.Get(request.TextDocument.Uri.ToString());
        if (analysis is null)
            return Task.FromResult<CommandOrCodeActionContainer?>(null);

        var actions = new List<CommandOrCodeAction>();

        foreach (var diag in request.Context.Diagnostics)
        {
            var msg = diag.Message ?? "";

            if (msg.StartsWith("Undefined symbol '"))
            {
                var symName = ExtractQuoted(msg);
                if (symName is not null)
                {
                    actions.Add(new CommandOrCodeAction(new CodeAction
                    {
                        Title = $"Declare variable '{symName}'",
                        Kind = CodeActionKind.QuickFix,
                        Diagnostics = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>(diag),
                        Edit = new WorkspaceEdit
                        {
                            Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                            {
                                [request.TextDocument.Uri] = new[]
                                {
                                    new TextEdit
                                    {
                                        Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                                            diag.Range.Start, diag.Range.Start),
                                        NewText = $"int {symName} = 0;\n"
                                    }
                                }
                            }
                        }
                    }));
                }
            }

            if (msg.StartsWith("Unknown type '"))
            {
                var typeName = ExtractQuoted(msg);
                if (typeName is not null)
                {
                    actions.Add(new CommandOrCodeAction(new CodeAction
                    {
                        Title = $"Create class '{typeName}'",
                        Kind = CodeActionKind.QuickFix,
                        Diagnostics = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>(diag),
                        Edit = new WorkspaceEdit
                        {
                            Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                            {
                                [request.TextDocument.Uri] = new[]
                                {
                                    new TextEdit
                                    {
                                        Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                                            new OmniSharp.Extensions.LanguageServer.Protocol.Models.Position(9999, 0),
                                            new OmniSharp.Extensions.LanguageServer.Protocol.Models.Position(9999, 0)),
                                        NewText = $"\nclass {typeName} {{\n}}\n"
                                    }
                                }
                            }
                        }
                    }));
                }
            }

            if (msg == "'return' outside a function.")
            {
                actions.Add(new CommandOrCodeAction(new CodeAction
                {
                    Title = "Wrap in function",
                    Kind = CodeActionKind.QuickFix,
                    Diagnostics = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>(diag),
                    IsPreferred = true
                }));
            }
        }

        actions.Add(new CommandOrCodeAction(new CodeAction
        {
            Title = "Sort imports",
            Kind = CodeActionKind.SourceOrganizeImports,
            Edit = OrganizeImports(request, analysis)
        }));

        return Task.FromResult<CommandOrCodeActionContainer?>(
            new CommandOrCodeActionContainer(actions));
    }

    public override Task<CodeAction> Handle(CodeAction request, CancellationToken ct) =>
        Task.FromResult(request);

    private static WorkspaceEdit OrganizeImports(
        CodeActionParams request, DocumentAnalysis analysis)
    {
        var imports = analysis.Parse.Statements
            .OfType<ImportStmt>()
            .ToList();

        if (imports.Count < 2) return new WorkspaceEdit();

        var sorted = imports
            .OrderBy(i => i.Path)
            .Select(i => $"import {i.Path};")
            .ToList();

        var firstStart = PositionHelper.ToLsp(imports.First().Span.Start);
        var lastEnd    = PositionHelper.ToLsp(imports.Last().Span.End);
        lastEnd = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Position(
            lastEnd.Line, lastEnd.Character + 1);

        return new WorkspaceEdit
        {
            Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
            {
                [request.TextDocument.Uri] = new[]
                {
                    new TextEdit
                    {
                        Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(firstStart, lastEnd),
                        NewText = string.Join("\n", sorted)
                    }
                }
            }
        };
    }

    private static string? ExtractQuoted(string msg)
    {
        var start = msg.IndexOf('\'');
        if (start < 0) return null;
        var end = msg.IndexOf('\'', start + 1);
        if (end < 0) return null;
        return msg[(start + 1)..end];
    }
}