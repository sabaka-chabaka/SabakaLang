using System.Runtime.Loader;
using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using SabakaLang.AST;
using SabakaLang.Exceptions;
using SabakaLang.LSP.Analysis;
using System.IO;
using System.Reflection;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using SymbolKind = SabakaLang.LSP.Analysis.SymbolKind;

namespace SabakaLang.LSP;

public class TextDocumentHandler : TextDocumentSyncHandlerBase
{
    private readonly ILanguageServerFacade _router;
    private readonly DocumentStore _documentStore;
    private readonly SymbolIndex _symbolIndex;

    private readonly TextDocumentSelector _selector = new(
        new TextDocumentFilter { Pattern = "**/*.sabaka" },
        new TextDocumentFilter { Language  = "sabaka" }
    );

    // Cache of already-parsed import files to avoid re-parsing on every keystroke
    private readonly Dictionary<string, List<Symbol>> _importCache = new();
    private readonly Dictionary<string, long> _importCacheTime = new();

    private static readonly string _execDir;
    static TextDocumentHandler()
    {
        string? loc = Assembly.GetEntryAssembly()?.Location;
        _execDir = string.IsNullOrEmpty(loc)
            ? AppDomain.CurrentDomain.BaseDirectory
            : (Path.GetDirectoryName(loc) ?? AppDomain.CurrentDomain.BaseDirectory);
    }

    public TextDocumentHandler(ILanguageServerFacade router,
        DocumentStore documentStore, SymbolIndex symbolIndex)
    {
        _router = router;
        _documentStore = documentStore;
        _symbolIndex = symbolIndex;
    }

    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri) =>
        new(uri, "sabaka");

    public override async Task<Unit> Handle(DidOpenTextDocumentParams r, CancellationToken ct)
    {
        _documentStore.UpdateDocument(r.TextDocument.Uri, r.TextDocument.Text);
        await AnalyzeDocument(r.TextDocument.Uri, r.TextDocument.Text);
        return Unit.Value;
    }

    public override async Task<Unit> Handle(DidChangeTextDocumentParams r, CancellationToken ct)
    {
        var text = r.ContentChanges.First().Text;
        _documentStore.UpdateDocument(r.TextDocument.Uri, text);
        await AnalyzeDocument(r.TextDocument.Uri, text);
        return Unit.Value;
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams r, CancellationToken ct) => Unit.Task;

    public override Task<Unit> Handle(DidCloseTextDocumentParams r, CancellationToken ct)
    {
        _documentStore.RemoveDocument(r.TextDocument.Uri);
        _symbolIndex.RemoveDocument(r.TextDocument.Uri);
        return Unit.Task;
    }

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
        TextSynchronizationCapability c, ClientCapabilities cc) =>
        new() { DocumentSelector = _selector, Change = TextDocumentSyncKind.Full };

    // ── core ──────────────────────────────────────────────────────────────────

    private async Task AnalyzeDocument(DocumentUri uri, string source)
    {
        var diagnostics = new List<Diagnostic>();

        try
        {
            var lexer  = new Lexer.Lexer(source);
            var tokens = lexer.Tokenize(false);
            var parser = new Parser.Parser(tokens);
            var ast    = parser.ParseProgram();

            string baseDir = Path.GetDirectoryName(uri.GetFileSystemPath())
                ?? Directory.GetCurrentDirectory();

            var analyzer = new SemanticAnalyzer(ast);

            // Process imports
            foreach (var expr in ast.OfType<ImportStatement>())
            {
                string ext = Path.GetExtension(expr.FilePath).ToLowerInvariant();
                if (ext == ".dll")
                    await HandleDllImport(expr, uri, baseDir, analyzer, diagnostics, source);
                else
                    await HandleSabakaImport(expr, uri, baseDir, analyzer, diagnostics, source);
            }

            analyzer.Analyze();

            _symbolIndex.UpdateDocument(uri,
                analyzer.AllSymbols.ToList(),
                analyzer.Modules.ToDictionary(kv => kv.Key, kv => kv.Value));
        }
        catch (SabakaLangException ex)
        {
            diagnostics.Add(MakeDiagnostic(source, ex.Position, ex.Position, ex.Message));
        }
        catch (Exception ex)
        {
            diagnostics.Add(MakeDiagnostic(source, 0, 0, ex.Message));
        }

        _router.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = uri,
            Diagnostics = diagnostics
        });
    }

    // ── import handlers ───────────────────────────────────────────────────────

    private async Task HandleSabakaImport(ImportStatement import,
        DocumentUri currentUri, string baseDir,
        SemanticAnalyzer analyzer, List<Diagnostic> diagnostics, string source)
    {
        string path = Path.GetFullPath(Path.Combine(baseDir, import.FilePath));
        if (!path.EndsWith(".sabaka", StringComparison.OrdinalIgnoreCase))
            path += ".sabaka";

        if (!File.Exists(path))
        {
            diagnostics.Add(MakeDiagnostic(source, import.Start, import.End,
                $"Import not found: {import.FilePath}"));
            return;
        }

        var symbols = await GetOrParseFile(path, diagnostics, source, import);
        if (symbols == null) return;

        string? alias = import.Alias?.ToLower()
            ?? (import.Alias == null ? null : null); // explicit: null = global

        if (import.Alias != null)
        {
            // "import X as alias" — namespaced
            analyzer.AddModuleSymbols(import.Alias.ToLower(), symbols);
        }
        else
        {
            // "import X" — global
            analyzer.AddGlobalSymbols(symbols);
        }

        // Always update the SymbolIndex for the imported file so other features work
        var importUri = DocumentUri.FromFileSystemPath(path);
        _symbolIndex.UpdateDocument(importUri, symbols,
            new Dictionary<string, List<Symbol>>());
    }

    private Task HandleDllImport(ImportStatement import,
        DocumentUri currentUri, string baseDir,
        SemanticAnalyzer analyzer, List<Diagnostic> diagnostics, string source)
    {
        string fileName = Path.GetFileName(import.FilePath);
        string nearScript = Path.GetFullPath(Path.Combine(baseDir, fileName));
        string nearExe    = Path.GetFullPath(Path.Combine(_execDir, fileName));
        string dllPath    = File.Exists(nearScript) ? nearScript : nearExe;

        if (!File.Exists(dllPath))
        {
            diagnostics.Add(MakeDiagnostic(source, import.Start, import.End,
                $"DLL not found: {fileName}"));
            return Task.CompletedTask;
        }

        try
        {
            var symbols = LoadDllSymbols(dllPath);
            string alias = (import.Alias ?? Path.GetFileNameWithoutExtension(fileName)).ToLower();

            if (import.Alias != null)
                analyzer.AddModuleSymbols(alias, symbols);
            else
                analyzer.AddGlobalSymbols(symbols);

            var dllUri = DocumentUri.From($"dll://{dllPath}");
            _symbolIndex.UpdateDocument(dllUri, symbols,
                new Dictionary<string, List<Symbol>>());
        }
        catch (Exception ex)
        {
            diagnostics.Add(MakeDiagnostic(source, import.Start, import.End,
                $"Failed to load DLL '{fileName}': {ex.Message}"));
        }

        return Task.CompletedTask;
    }

    // ── file parsing cache ────────────────────────────────────────────────────

    private async Task<List<Symbol>?> GetOrParseFile(string path,
        List<Diagnostic> diagnostics, string source, ImportStatement import)
    {
        long lastWrite = File.GetLastWriteTimeUtc(path).Ticks;
        if (_importCache.TryGetValue(path, out var cached) &&
            _importCacheTime.TryGetValue(path, out var cachedTime) &&
            cachedTime == lastWrite)
            return cached;

        try
        {
            string importSource = await File.ReadAllTextAsync(path);
            var lexer  = new Lexer.Lexer(importSource);
            var tokens = lexer.Tokenize(false);
            var parser = new Parser.Parser(tokens);
            var ast    = parser.ParseProgram();

            // Recursively handle transitive imports
            var subAnalyzer = new SemanticAnalyzer(ast);
            string subDir = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();

            foreach (var subImport in ast.OfType<ImportStatement>())
            {
                string ext = Path.GetExtension(subImport.FilePath).ToLowerInvariant();
                if (ext != ".dll")
                {
                    var subPath = Path.GetFullPath(Path.Combine(subDir, subImport.FilePath));
                    var subSymbols = await GetOrParseFile(subPath,
                        diagnostics, importSource, subImport);
                    if (subSymbols != null)
                    {
                        if (subImport.Alias != null)
                            subAnalyzer.AddModuleSymbols(subImport.Alias.ToLower(), subSymbols);
                        else
                            subAnalyzer.AddGlobalSymbols(subSymbols);
                    }
                }
            }

            subAnalyzer.Analyze();

            // Tag all symbols with source file, flatten to list
            var symbols = subAnalyzer.AllSymbols
                .Select(s => new Symbol(s.Name, s.Kind, s.Type,
                    s.Start, s.End, 0, int.MaxValue,
                    s.ParentName, path, s.Parameters, s.ModuleAlias))
                .ToList();

            _importCache[path]     = symbols;
            _importCacheTime[path] = lastWrite;
            return symbols;
        }
        catch (SabakaLangException ex)
        {
            diagnostics.Add(MakeDiagnostic(source, import.Start, import.End,
                $"Error in '{Path.GetFileName(path)}': {ex.Message}", DiagnosticSeverity.Warning));
            return null;
        }
        catch
        {
            return null;
        }
    }

    // ── DLL reflection ────────────────────────────────────────────────────────

    private static List<Symbol> LoadDllSymbols(string dllPath)
    {
        var symbols = new List<Symbol>();

        // Isolated, collectible context — unloaded after we're done.
        // Dependencies that can't be resolved (e.g. PresentationFramework/WPF)
        // are returned as dummy assemblies instead of throwing, because we
        // override Load() to swallow missing-assembly errors.
        var alc = new MetaSafeLoadContext(dllPath);
        Assembly asm;
        try
        {
            asm = alc.LoadFromAssemblyPath(dllPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[LSP] Cannot load DLL '" + dllPath + "': " + ex.Message);
            alc.Unload();
            return symbols;
        }

        Type[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[LSP] GetTypes failed for '" + dllPath + "': " + ex.Message);
            alc.Unload();
            return symbols;
        }

        foreach (var type in types)
        {
            if (type == null) continue;

            bool isSabakaModule;
            CustomAttributeData? classAttr;
            try
            {
                isSabakaModule = type.GetInterfaces().Any(i => i.Name == "ISabakaModule");
                classAttr = type.GetCustomAttributesData()
                    .FirstOrDefault(a => a.AttributeType.Name == "SabakaExportAttribute");
            }
            catch { continue; }

            if (!isSabakaModule && classAttr == null) continue;

            string className = classAttr != null
                ? classAttr.ConstructorArguments[0].Value as string ?? type.Name
                : type.Name;

            symbols.Add(new Symbol(className, SymbolKind.Module, className,
                0, 0, 0, int.MaxValue, null, dllPath));

            // Methods
            try
            {
                foreach (var method in type.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    var mAttr = method.GetCustomAttributesData()
                        .FirstOrDefault(a => a.AttributeType.Name == "SabakaExportAttribute");
                    if (mAttr == null) continue;

                    string exportName = mAttr.ConstructorArguments[0].Value as string ?? method.Name;
                    string retType;
                    string parms;
                    try
                    {
                        retType = method.ReturnType.Name == "Void" ? "void" : MapType(method.ReturnType.Name);
                        parms   = string.Join(", ", method.GetParameters()
                            .Select(p => MapType(p.ParameterType.Name) + " " + p.Name));
                    }
                    catch { retType = "?"; parms = ""; }

                    symbols.Add(new Symbol(exportName, SymbolKind.Method, retType,
                        0, 0, 0, int.MaxValue, className, dllPath, parms));
                }
            }
            catch { /* skip methods on this type */ }

            // Properties
            try
            {
                foreach (var prop in type.GetProperties(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                {
                    var pAttr = prop.GetCustomAttributesData()
                        .FirstOrDefault(a => a.AttributeType.Name == "SabakaExportAttribute");
                    if (pAttr == null) continue;

                    string exportName = pAttr.ConstructorArguments[0].Value as string ?? prop.Name;
                    string retType;
                    try { retType = MapType(prop.PropertyType.Name); } catch { retType = "?"; }

                    symbols.Add(new Symbol(exportName, SymbolKind.Variable, retType,
                        0, 0, 0, int.MaxValue, className, dllPath));
                }
            }
            catch { /* skip props on this type */ }

            // Fields
            try
            {
                foreach (var field in type.GetFields(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                {
                    var fAttr = field.GetCustomAttributesData()
                        .FirstOrDefault(a => a.AttributeType.Name == "SabakaExportAttribute");
                    if (fAttr == null) continue;

                    string exportName = fAttr.ConstructorArguments[0].Value as string ?? field.Name;
                    string retType;
                    try { retType = MapType(field.FieldType.Name); } catch { retType = "?"; }

                    symbols.Add(new Symbol(exportName, SymbolKind.Variable, retType,
                        0, 0, 0, int.MaxValue, className, dllPath));
                }
            }
            catch { /* skip fields on this type */ }
        }

        alc.Unload();
        return symbols;
    }

    private static string MapType(string clrName) => clrName switch
    {
        "Int32" or "Int64" => "int",
        "Double" or "Single" => "float",
        "Boolean" => "bool",
        "String"  => "string",
        "Void"    => "void",
        _ => clrName.ToLower()
    };

    // ── util ──────────────────────────────────────────────────────────────────

    private static Diagnostic MakeDiagnostic(string source, int start, int end,
        string message, DiagnosticSeverity severity = DiagnosticSeverity.Error) =>
        new()
        {
            Severity = severity,
            Range    = PositionHelper.GetRange(source, start, end),
            Message  = message,
            Source   = "SabakaLang"
        };
}

/// <summary>
/// Collectible AssemblyLoadContext that silently swallows missing dependencies.
/// When loading e.g. SabakaUI.dll which depends on PresentationFramework (WPF),
/// instead of throwing FileNotFoundException, Load() returns null — the runtime
/// then skips those types rather than aborting the whole load.
/// After reading metadata, call Unload() to release the DLL file handle.
/// </summary>
internal sealed class MetaSafeLoadContext : AssemblyLoadContext
{
    private readonly string _dllDir;

    public MetaSafeLoadContext(string dllPath)
        : base(name: "SabakaLSP-meta-" + Path.GetFileName(dllPath), isCollectible: true)
    {
        _dllDir = Path.GetDirectoryName(dllPath) ?? "";
    }

    protected override Assembly? Load(AssemblyName name)
    {
        // 1. Try next to the DLL being inspected
        string local = Path.Combine(_dllDir, name.Name + ".dll");
        if (File.Exists(local))
        {
            try { return LoadFromAssemblyPath(local); }
            catch { /* fall through */ }
        }

        // 2. Try the default context (already-loaded system assemblies)
        try
        {
            var existing = Default.LoadFromAssemblyName(name);
            if (existing != null) return existing;
        }
        catch { /* fall through */ }

        // 3. Can't find it — return null so the runtime skips dependent types
        //    rather than throwing FileNotFoundException and aborting everything.
        return null;
    }
}