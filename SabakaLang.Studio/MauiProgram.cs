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
                fonts.AddFont("JetBrainsMono-Regular.ttf", "JetMonoRegular");
            });

        builder.Services.AddSingleton<DocumentStore>();
        builder.Services.AddSingleton<Services.FileService>();
        builder.Services.AddSingleton<Git.GitService>();
        builder.Services.AddTransient<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}