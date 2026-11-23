using SabakaLang.Extensions;

namespace SabakaLang.WebSample;

/// <summary>An ordinary Sabaka extension, available to every controller.</summary>
[SabakaModule("app", AutoImport = true)]
public static class AppModule
{
    private static int _visits;

    [SabakaFunction("visits")]
    public static int Visits() => Interlocked.Increment(ref _visits);

    // method on a built-in type: "<b>".htmlEscape()
    [SabakaExtension("string")]
    public static string HtmlEscape(string self) => System.Net.WebUtility.HtmlEncode(self);
}
