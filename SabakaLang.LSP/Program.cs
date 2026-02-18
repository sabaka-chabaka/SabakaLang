using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Server;
using SabakaLang.LSP;

var server = await LanguageServer.From(options =>
    options
        .WithInput(Console.OpenStandardInput())
        .WithOutput(Console.OpenStandardOutput())
        .WithLoggerFactory(new LoggerFactory())
        .AddDefaultLoggingProvider()
        .WithServices(services => {
            services.AddSingleton<DocumentStore>();
            services.AddSingleton<SymbolIndex>();
        })
        .WithHandler<TextDocumentHandler>()
        .WithHandler<CompletionHandler>()
        .WithHandler<SemanticTokensHandler>()
        .WithHandler<HoverHandler>()
        .WithHandler<OnTypeFormattingHandler>()
);

await server.WaitForExit;