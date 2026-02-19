using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using SabakaLang.Lexer;
using SabakaLang.Parser;
using SabakaLang.Exceptions;
using SabakaLang.LSP.Analysis;
using System.IO;
using SabakaLang.AST;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace SabakaLang.LSP;

public class TextDocumentHandler : TextDocumentSyncHandlerBase
{
    private readonly ILanguageServerFacade _router;
    private readonly DocumentStore _documentStore;
    private readonly SymbolIndex _symbolIndex;

    private readonly TextDocumentSelector _documentSelector = new TextDocumentSelector(
        new TextDocumentFilter { Pattern = "**/*.sabaka" },
        new TextDocumentFilter { Language = "sabaka" }
    );

    public TextDocumentHandler(ILanguageServerFacade router, DocumentStore documentStore, SymbolIndex symbolIndex)
    {
        _router = router;
        _documentStore = documentStore;
        _symbolIndex = symbolIndex;
    }

    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri)
    {
        return new TextDocumentAttributes(uri, "sabaka");
    }

    public override async Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri;
        var text = request.ContentChanges.First().Text;
        _documentStore.UpdateDocument(uri, text);

        await AnalyzeDocument(uri, text);
        return Unit.Value;
    }

    public override async Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri;
        var text = request.TextDocument.Text;
        _documentStore.UpdateDocument(uri, text);

        await AnalyzeDocument(uri, text);
        return Unit.Value;
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken)
    {
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken)
    {
        _documentStore.RemoveDocument(request.TextDocument.Uri);
        _symbolIndex.RemoveDocument(request.TextDocument.Uri);
        return Unit.Task;
    }

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
        TextSynchronizationCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new TextDocumentSyncRegistrationOptions
        {
            DocumentSelector = _documentSelector,
            Change = TextDocumentSyncKind.Full
        };
    }

    private async Task AnalyzeDocument(DocumentUri uri, string source)
    {
        var diagnostics = new List<Diagnostic>();

        try
        {
            var lexer = new Lexer.Lexer(source);
            var tokens = lexer.Tokenize(false);
            var parser = new Parser.Parser(tokens);
            var ast = parser.ParseProgram();

            var imports = ast.OfType<ImportStatement>().ToList();

            var importedSymbols = new List<Symbol>();
            string baseDir = Path.GetDirectoryName(uri.GetFileSystemPath()) ?? Directory.GetCurrentDirectory();

            foreach (var import in imports)
            {
                string importPath = Path.Combine(baseDir, import.FilePath);
                if (!importPath.EndsWith(".sabaka"))
                    importPath += ".sabaka";

                if (File.Exists(importPath))
                {
                    var importSource = await File.ReadAllTextAsync(importPath);
                    var importLexer = new Lexer.Lexer(importSource);
                    var importTokens = importLexer.Tokenize(false);
                    var importParser = new Parser.Parser(importTokens);
                    var importAst = importParser.ParseProgram();

                    var importAnalyzer = new SemanticAnalyzer(importAst);
                    try
                    {
                        importAnalyzer.Analyze();
                        importedSymbols.AddRange(importAnalyzer.AllSymbols);
                    }
                    catch (SabakaLangException ex)
                    {
                        diagnostics.Add(new Diagnostic
                        {
                            Severity = DiagnosticSeverity.Warning,
                            Range = PositionHelper.GetRange(source, import.Start, import.End),
                            Message = $"Error in imported file '{import.FilePath}': {ex.Message}",
                            Source = "SabakaLang"
                        });
                    }
                }
                else
                {
                    diagnostics.Add(new Diagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Range = PositionHelper.GetRange(source, import.Start, import.End),
                        Message = $"Import file not found: {import.FilePath}",
                        Source = "SabakaLang"
                    });
                }
            }

            var analyzer = new SemanticAnalyzer(ast);

            if (importedSymbols.Any())
            {
                analyzer.AddImportedSymbols(uri, importedSymbols);
            }

            analyzer.Analyze();

            var allSymbols = analyzer.AllSymbols.Concat(importedSymbols).ToList();
            _symbolIndex.UpdateSymbols(uri, allSymbols);
        }
        catch (SabakaLangException ex)
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Range = PositionHelper.GetRange(source, ex.Position, ex.Position),
                Message = ex.Message,
                Source = "SabakaLang"
            });
        }
        catch (Exception ex)
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Range = new Range(new Position(0, 0), new Position(0, 0)),
                Message = ex.Message,
                Source = "SabakaLang"
            });
        }

        _router.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = uri,
            Diagnostics = diagnostics
        });
    }
}