using SabakaLang.Engine;
using SabakaLang.LanguageServer;

var engine = new SabakaEngine();
for (var i = 0; i < args.Length - 1; i++)
{
    try
    {
        if (args[i] == "--ext") engine.Extensions.RegisterAssemblyFile(args[++i]);
        else if (args[i] == "--ext-dir") engine.Extensions.RegisterDirectory(args[++i]);
    }
    catch (Exception ex) { Console.Error.WriteLine($"extension '{args[i]}' failed: {ex.Message}"); }
}

var input = new BufferedStream(Console.OpenStandardInput());
var output = Console.OpenStandardOutput();
var rpc = new JsonRpc(input, output);
var docs = new DocumentStore();
var server = new Server(rpc, new Analyzer(engine.Extensions, docs));
return await server.RunAsync();
