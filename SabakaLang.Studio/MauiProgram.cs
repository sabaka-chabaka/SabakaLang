using Microsoft.Extensions.Logging;
using SabakaLang.LanguageServer;

namespace SabakaLang.Studio;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("JetBrainsMono-Medium.ttf", "JMM");
            });

        builder.Services.AddSingleton<DocumentStore>();
        builder.Services.AddTransient<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}