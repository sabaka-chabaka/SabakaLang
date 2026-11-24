using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace SabakaLang.LanguageServer;

public sealed class Server(JsonRpc rpc, Analyzer analyzer)
{
    private readonly DocumentStore _docs = new();
    private readonly CompletionProvider _completion = new(analyzer);
    private readonly HashSet<string> _dirty = [];
    private bool _shutdown;

    public async Task<int> RunAsync()
    {
        var ch = Channel.CreateUnbounded<JsonObject>();
        _ = Task.Run(async () =>
        {
            try { while (await rpc.ReadAsync() is { } m) await ch.Writer.WriteAsync(m); }
            catch (Exception ex) { Console.Error.WriteLine("reader: " + ex.Message); }
            finally { ch.Writer.TryComplete(); }
        });

        while (await ch.Reader.WaitToReadAsync())
        {
            while (ch.Reader.TryRead(out var msg))
            {
                if (Handle(msg)) return _shutdown ? 0 : 1;
            }
            PublishDirty();
        }
        return _shutdown ? 0 : 1;
    }

    private Analysis GetAnalysis(string uri)
    {
        var d = _docs.Get(uri) ?? throw new InvalidOperationException("Document not open: " + uri);
        return d.Analysis ??= analyzer.Analyze(uri, d.Text);
    }

    private List<Analysis> AllAnalyses() =>
        _docs.All.Select(d => d.Analysis ??= analyzer.Analyze(d.Uri, d.Text)).ToList();

    private void PublishDirty()
    {
        foreach (var uri in _dirty.ToList())
        {
            if (_docs.Get(uri) is null) continue;
            try
            {
                var a = GetAnalysis(uri);
                var arr = new JsonArray();
                foreach (var d in a.Diagnostics)
                    arr.Add(new JsonObject
                    {
                        ["range"] = Features.Range(a, d.Start, d.End),
                        ["severity"] = d.Severity,
                        ["source"] = "sabaka",
                        ["message"] = d.Message
                    });
                rpc.Notify("textDocument/publishDiagnostics", new JsonObject { ["uri"] = uri, ["diagnostics"] = arr });
            }
            catch (Exception ex) { Console.Error.WriteLine("diagnostics: " + ex); }
        }
        _dirty.Clear();
    }

    private static string Uri(JsonNode? p) => p?["textDocument"]?["uri"]?.GetValue<string>() ?? "";

    private int OffsetOf(Analysis a, JsonNode? p) =>
        a.Lines.ToOffset(p?["position"]?["line"]?.GetValue<int>() ?? 0, p?["position"]?["character"]?.GetValue<int>() ?? 0);

    private bool Handle(JsonObject msg)
    {
        var method = msg["method"]?.GetValue<string>();
        var id = msg["id"];
        var p = msg["params"];
        if (method is null) return false;

        try
        {
            switch (method)
            {
                case "initialize": rpc.Respond(id, InitializeResult()); break;
                case "initialized": break;
                case "shutdown": _shutdown = true; rpc.Respond(id, null); break;
                case "exit": return true;

                case "textDocument/didOpen":
                {
                    var td = p!["textDocument"]!;
                    var uri = td["uri"]!.GetValue<string>();
                    _docs.Set(uri, td["text"]!.GetValue<string>());
                    MarkAllDirty();
                    break;
                }
                case "textDocument/didChange":
                {
                    var uri = Uri(p);
                    var changes = p!["contentChanges"]!.AsArray();
                    if (changes.Count > 0) _docs.Set(uri, changes[^1]!["text"]!.GetValue<string>());
                    MarkAllDirty();
                    break;
                }
                case "textDocument/didClose":
                {
                    var uri = Uri(p);
                    _docs.Remove(uri);
                    rpc.Notify("textDocument/publishDiagnostics", new JsonObject { ["uri"] = uri, ["diagnostics"] = new JsonArray() });
                    MarkAllDirty();
                    break;
                }

                case "textDocument/hover":
                {
                    var a = GetAnalysis(Uri(p));
                    rpc.Respond(id, Features.Hover(a, OffsetOf(a, p)));
                    break;
                }
                case "textDocument/definition":
                {
                    var a = GetAnalysis(Uri(p));
                    rpc.Respond(id, Features.Definition(a, OffsetOf(a, p)));
                    break;
                }
                case "textDocument/references":
                {
                    var a = GetAnalysis(Uri(p));
                    var sym = a.RefAt(OffsetOf(a, p))?.Symbol;
                    var incl = p?["context"]?["includeDeclaration"]?.GetValue<bool>() ?? true;
                    rpc.Respond(id, sym is null
                        ? new JsonArray()
                        : Navigation.References(Navigation.Scope(analyzer, AllAnalyses(), sym), sym, incl));
                    break;
                }
                case "textDocument/documentHighlight":
                {
                    var a = GetAnalysis(Uri(p));
                    rpc.Respond(id, Navigation.Highlights(a, OffsetOf(a, p)));
                    break;
                }
                case "textDocument/prepareRename":
                {
                    var a = GetAnalysis(Uri(p));
                    rpc.Respond(id, Navigation.PrepareRename(a, OffsetOf(a, p)));
                    break;
                }
                case "textDocument/rename":
                {
                    var a = GetAnalysis(Uri(p));
                    var sym = a.RefAt(OffsetOf(a, p))?.Symbol ?? throw new InvalidOperationException("Nothing to rename here.");
                    var newName = p?["newName"]?.GetValue<string>() ?? "";
                    rpc.Respond(id, Navigation.Rename(Navigation.Scope(analyzer, AllAnalyses(), sym), sym, newName));
                    break;
                }
                case "textDocument/signatureHelp":
                {
                    var a = GetAnalysis(Uri(p));
                    rpc.Respond(id, Signature.Help(a, OffsetOf(a, p)));
                    break;
                }
                case "textDocument/documentSymbol":
                    rpc.Respond(id, Features.DocumentSymbols(GetAnalysis(Uri(p))));
                    break;
                case "textDocument/semanticTokens/full":
                {
                    var data = new JsonArray();
                    foreach (var n in Semantic.Build(GetAnalysis(Uri(p)))) data.Add(n);
                    rpc.Respond(id, new JsonObject { ["data"] = data });
                    break;
                }
                case "textDocument/completion":
                {
                    var uri = Uri(p);
                    var doc = _docs.Get(uri) ?? throw new InvalidOperationException("not open");
                    var a = GetAnalysis(uri);
                    rpc.Respond(id, _completion.Complete(uri, doc.Text, OffsetOf(a, p)));
                    break;
                }

                default:
                    if (id is not null) rpc.RespondError(id, -32601, "Method not found: " + method);
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{method}: {ex}");
            if (id is not null) rpc.RespondError(id, -32603, ex.Message);
        }
        return false;
    }

    private void MarkAllDirty()
    {
        foreach (var d in _docs.All) _dirty.Add(d.Uri);
    }

    private static JsonObject InitializeResult()
    {
        static JsonArray Arr(IEnumerable<string> xs)
        {
            var a = new JsonArray();
            foreach (var x in xs) a.Add(x);
            return a;
        }
        return new JsonObject
        {
            ["capabilities"] = new JsonObject
            {
                ["textDocumentSync"] = new JsonObject { ["openClose"] = true, ["change"] = 1 },
                ["hoverProvider"] = true,
                ["definitionProvider"] = true,
                ["documentSymbolProvider"] = true,
                ["referencesProvider"] = true,
                ["documentHighlightProvider"] = true,
                ["renameProvider"] = new JsonObject { ["prepareProvider"] = true },
                ["signatureHelpProvider"] = new JsonObject { ["triggerCharacters"] = Arr(["(", ","]) },
                ["completionProvider"] = new JsonObject { ["triggerCharacters"] = Arr(["."]) },
                ["semanticTokensProvider"] = new JsonObject
                {
                    ["legend"] = new JsonObject
                    {
                        ["tokenTypes"] = Arr(Semantic.Types),
                        ["tokenModifiers"] = Arr(Semantic.Modifiers)
                    },
                    ["full"] = true
                }
            },
            ["serverInfo"] = new JsonObject { ["name"] = "SabakaLang.LanguageServer", ["version"] = "0.1.0" }
        };
    }
}
