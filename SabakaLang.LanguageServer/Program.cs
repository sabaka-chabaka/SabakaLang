using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Server;
using SabakaLang.LanguageServer;
using SabakaLang.LanguageServer.Handlers;

var server = await LanguageServer.From(options =>
{
    options
        .WithInput(Console.OpenStandardInput())
        .WithOutput(Console.OpenStandardOutput())
        .ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Warning);
        })
        .WithServices(services =>
        {
            services.AddSingleton<DocumentStore>();
        })
        .WithHandler<TextDocumentSyncHandler>()
        .WithHandler<HoverHandler>()
        .WithHandler<CompletionHandler>()
        .WithHandler<DefinitionHandler>()
        .WithHandler<ReferencesHandler>()
        .WithHandler<RenameHandler>()
        .WithHandler<DocumentSymbolHandler>()
        .WithHandler<SignatureHelpHandler>()
        .WithHandler<SemanticTokensHandler>()
        .WithHandler<FoldingRangeHandler>()
        .WithHandler<CodeActionHandler>()
        .OnInitialize((srv, req, ct) =>
        {
            return Task.CompletedTask;
        })
        .OnInitialized((srv, req, res, ct) =>
        {
            return Task.CompletedTask;
        });
});

await server.WaitForExit;