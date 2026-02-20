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
using System.Reflection;
using SabakaLang.AST;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using SymbolKind = SabakaLang.LSP.Analysis.SymbolKind;

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

    private static readonly string _executableDirectory;

    static TextDocumentHandler()
    {
        string? location = Assembly.GetEntryAssembly()?.Location;
        if (!string.IsNullOrEmpty(location))
            _executableDirectory = Path.GetDirectoryName(location) ?? AppDomain.CurrentDomain.BaseDirectory;
        else
            _executableDirectory = AppDomain.CurrentDomain.BaseDirectory;
    }

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
        var importedSymbols = new List<Symbol>();

        try
        {
            var lexer = new Lexer.Lexer(source);
            var tokens = lexer.Tokenize(false);
            var parser = new Parser.Parser(tokens);
            var ast = parser.ParseProgram();

            var imports = ast.OfType<ImportStatement>().ToList();
            string baseDir = Path.GetDirectoryName(uri.GetFileSystemPath()) ?? Directory.GetCurrentDirectory();

            foreach (var import in imports)
            {
                string importPath;
                if (Path.IsPathRooted(import.FilePath))
                {
                    importPath = import.FilePath;
                }
                else
                {
                    importPath = Path.Combine(baseDir, import.FilePath);
                }

                // Обработка .dll
                if (importPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    string dllFullPath;
                    if (Path.IsPathRooted(import.FilePath))
                    {
                        dllFullPath = import.FilePath;
                    }
                    else
                    {
                        // Ищем DLL относительно исполняемого файла сервера
                        dllFullPath = Path.Combine(_executableDirectory, import.FilePath);
                    }

                    dllFullPath = Path.GetFullPath(dllFullPath);

                    if (!File.Exists(dllFullPath))
                    {
                        diagnostics.Add(new Diagnostic
                        {
                            Severity = DiagnosticSeverity.Error,
                            Range = PositionHelper.GetRange(source, import.Start, import.End),
                            Message = $"DLL not found: {import.FilePath}",
                            Source = "SabakaLang"
                        });
                        continue;
                    }

                    try
                    {
                        var dllSymbols = LoadDllSymbols(dllFullPath, uri);
                        importedSymbols.AddRange(dllSymbols);

                        // Обновляем SymbolIndex для виртуального URI (dll://...)
                        var virtualUri = DocumentUri.From($"dll://{dllFullPath}");
                        _symbolIndex.UpdateSymbols(virtualUri, dllSymbols);
                    }
                    catch (Exception ex)
                    {
                        diagnostics.Add(new Diagnostic
                        {
                            Severity = DiagnosticSeverity.Error,
                            Range = PositionHelper.GetRange(source, import.Start, import.End),
                            Message = $"Failed to load DLL '{import.FilePath}': {ex.Message}",
                            Source = "SabakaLang"
                        });
                    }
                }
                else // .sabaka
                {
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
                            // Добавляем SourceFile для символов из импортированного файла
                            var fileSymbols = importAnalyzer.AllSymbols.Select(s =>
                                new Symbol(s.Name, s.Kind, s.Type, s.Start, s.End, s.ScopeStart, s.ScopeEnd,
                                    s.ParentName, importPath)).ToList();
                            importedSymbols.AddRange(fileSymbols);

                            // Обновляем SymbolIndex для импортированного файла
                            var importUri = DocumentUri.FromFileSystemPath(importPath);
                            _symbolIndex.UpdateSymbols(importUri, fileSymbols);
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
            }

            var analyzer = new SemanticAnalyzer(ast);
            analyzer.AddImportedSymbols(uri, importedSymbols);
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

    private List<Symbol> LoadDllSymbols(string dllPath, DocumentUri currentUri)
    {
        var symbols = new List<Symbol>();
        Assembly asm = Assembly.LoadFrom(dllPath);

        foreach (var type in asm.GetTypes())
        {
            var classAttr = type.GetCustomAttributes()
                .FirstOrDefault(a => a.GetType().Name == "SabakaExportAttribute");
            if (classAttr == null) continue;

            string className = (string)classAttr.GetType()
                .GetProperty("Name")!.GetValue(classAttr)!;

            symbols.Add(new Symbol(
                name: className,
                kind: SymbolKind.Class,
                type: className,
                start: 0, end: 0,
                scopeStart: 0, scopeEnd: int.MaxValue,
                parentName: null,
                sourceFile: dllPath
            ));

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var allAttrs = method.GetCustomAttributes().ToList();
    
                var methodAttr = allAttrs.FirstOrDefault(a => a.GetType().Name == "SabakaExportAttribute");
                if (methodAttr == null) continue;

                string methodName = (string)methodAttr.GetType()
                    .GetProperty("Name")!.GetValue(methodAttr)!;
                string returnType = method.ReturnType == typeof(void)
                    ? "void"
                    : method.ReturnType.Name;
                var paramsStr = string.Join(", ", method.GetParameters()
                    .Select(p => $"{p.ParameterType.Name} {p.Name}"));

                symbols.Add(new Symbol(
                    name: methodName,
                    kind: SymbolKind.Method,
                    type: $"{returnType}({paramsStr})",
                    start: 0, end: 0,
                    scopeStart: 0, scopeEnd: int.MaxValue,
                    parentName: className,
                    sourceFile: dllPath
                ));
            }
        }

        return symbols;
    }
}