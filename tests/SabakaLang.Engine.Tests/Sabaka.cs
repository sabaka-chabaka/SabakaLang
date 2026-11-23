using SabakaLang.Engine;
using SabakaLang.Extensions;
using SabakaLang.Runtime;

namespace SabakaLang.Engine.Tests;

/// <summary>Test helpers: run a script and capture its output.</summary>
public static class Sabaka
{
    public static SabakaEngine NewEngine(Action<SabakaEngine>? configure = null, Dictionary<string, string>? modules = null)
    {
        var engine = new SabakaEngine { Out = new StringWriter() };
        if (modules is not null) engine.ModuleSource = new DictionaryModuleSource(modules);
        configure?.Invoke(engine);
        return engine;
    }

    /// <summary>Runs the script, returns trimmed stdout (lines joined by '\n'). Fails the test on compile or runtime errors.</summary>
    public static string Run(string source, Action<SabakaEngine>? configure = null, Dictionary<string, string>? modules = null)
    {
        var engine = NewEngine(configure, modules);
        var result = engine.Run(source);
        if (result.Errors.Count > 0) Assert.Fail("Compile errors:\n" + string.Join("\n", result.Errors));
        if (result.RuntimeError is { } e) Assert.Fail("Runtime error: " + e.Message);
        return engine.Out.ToString()!.Replace("\r\n", "\n").TrimEnd();
    }

    public static IReadOnlyList<string> CompileErrors(string source, Action<SabakaEngine>? configure = null, Dictionary<string, string>? modules = null) =>
        NewEngine(configure, modules).Compile(source).Errors.Select(e => e.ToString()).ToList();

    public static IReadOnlyList<string> Warnings(string source) =>
        NewEngine().Compile(source).Warnings.Select(e => e.ToString()).ToList();

    public static string RuntimeError(string source, Action<SabakaEngine>? configure = null)
    {
        var result = NewEngine(configure).Run(source);
        Assert.Empty(result.Errors);
        if (result.RuntimeError is null) Assert.Fail("Expected a runtime error");
        return result.RuntimeError!.Message;
    }
}
