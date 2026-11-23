using SabakaLang.Extensions;
using SabakaLang.Runtime;

namespace SabakaLang.SampleExtension;

/// <summary>
/// Example extension. Build it, then:  sabaka run demo.sabaka --ext SabakaLang.SampleExtension.dll
/// </summary>
[SabakaModule("texttools", Description = "Sample extension: string helpers, a Stopwatch class and a method on arrays")]
public static class TextTools
{
    // 1. constants
    [SabakaConstant("VERSION")] public const string Version = "1.0";

    // 2. global function (import "texttools"; wordCount("a b c"))
    [SabakaFunction("wordCount")]
    public static int WordCount(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    // 3. extension method on a built-in type:  "hello".reversed()
    [SabakaExtension("string")]
    public static string Reversed(string self) => new(self.Reverse().ToArray());

    // 4. extension on every array, generic through the T placeholder:  [1,2,3].sum()  (needs numbers)
    [SabakaExtension("int[]")]
    public static int Sum(int[] self) => self.Sum();

    // 5. a C# class exposed to scripts:  Stopwatch sw = new Stopwatch(); sw.start(); ... sw.elapsedMs
    [SabakaClass("Stopwatch")]
    public sealed class StopwatchWrapper
    {
        private readonly System.Diagnostics.Stopwatch _sw = new();

        [SabakaMethod("start")] public void Start() => _sw.Restart();
        [SabakaMethod("stop")] public void Stop() => _sw.Stop();
        [SabakaProperty("elapsedMs")] public int ElapsedMs => (int)_sw.ElapsedMilliseconds;
    }
}
