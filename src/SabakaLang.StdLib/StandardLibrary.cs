using System.Diagnostics;
using System.Text;
using SabakaLang.Extensions;
using SabakaLang.Runtime;

namespace SabakaLang.StdLib;

/// <summary>
/// The standard library. It is written with the very same extension API that user extensions use:
/// every module below is a plain C# class tagged with Sabaka* attributes.
/// </summary>
public static class StandardLibrary
{
    /// <summary>Registers all standard modules. Pass <paramref name="include"/> to install only some (e.g. leave out "io" and "http" for sandboxed scripts).</summary>
    public static ExtensionRegistry Install(ExtensionRegistry registry, Func<string, bool>? include = null)
    {
        foreach (var type in new[]
                 {
                     typeof(CoreModule), typeof(MathModule), typeof(IoModule), typeof(HttpModule),
                     typeof(StringExtensions), typeof(ArrayExtensions), typeof(ObjectExtensions)
                 })
        {
            var name = type.GetCustomAttributes(typeof(SabakaModuleAttribute), false).Cast<SabakaModuleAttribute>().First().Name;
            if (include is null || include(name)) registry.RegisterType(type);
        }
        return registry;
    }

    public static ExtensionRegistry CreateRegistry(Func<string, bool>? include = null) => Install(new ExtensionRegistry(), include);
}

[SabakaModule("core", AutoImport = true, Description = "print, input, conversions, time")]
public static class CoreModule
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    [SabakaFunction("print")]
    public static void Print(NativeContext ctx, params Value[] values)
    {
        ctx.Out.WriteLine(string.Join(" ", values.Select(ctx.Stringify)));
    }

    [SabakaFunction("input")]
    public static string? Input(NativeContext ctx) => ctx.In.ReadLine();

    [SabakaFunction("input")]
    public static string? Input(NativeContext ctx, string prompt)
    {
        ctx.Out.Write(prompt);
        ctx.Out.Flush();
        return ctx.In.ReadLine();
    }

    [SabakaFunction("exit")]
    public static void Exit() => throw new ScriptExitException(0);

    [SabakaFunction("exit")]
    public static void Exit(int code) => throw new ScriptExitException(code);

    [SabakaFunction("sleep")]
    public static void Sleep(int milliseconds) => Thread.Sleep(Math.Max(0, milliseconds));

    /// <summary>Unix time in seconds.</summary>
    [SabakaFunction("time")]
    public static int Time() => (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>Milliseconds since the script started.</summary>
    [SabakaFunction("timeMs")]
    public static int TimeMs() => (int)Clock.ElapsedMilliseconds;

    [SabakaFunction("ord")]
    public static int Ord(string s) => s.Length == 0 ? throw new RuntimeException("ord: empty string") : s[0];

    [SabakaFunction("ord")]
    public static int Ord(char c) => c;

    [SabakaFunction("chr")]
    public static string Chr(int code) => char.ConvertFromUtf32(code);

    [SabakaFunction("str")]
    public static string Str(NativeContext ctx, Value value) => ctx.Stringify(value);

    [SabakaFunction("parseInt")]
    public static int ParseInt(string s) =>
        int.TryParse(s.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v : throw new RuntimeException($"parseInt: '{s}' is not an integer");

    [SabakaFunction("parseFloat")]
    public static double ParseFloat(string s) =>
        double.TryParse(s.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v : throw new RuntimeException($"parseFloat: '{s}' is not a number");

    [SabakaFunction("typeOf")]
    public static string TypeOf(Value v) => v.TypeName;
}

[SabakaModule("math", AutoImport = true, Description = "Mathematical functions")]
public static class MathModule
{
    private static readonly Random Rng = new();

    [SabakaConstant("PI")] public static readonly double Pi = Math.PI;
    [SabakaConstant("E")] public static readonly double E = Math.E;

    [SabakaFunction("sin")] public static double Sin(double x) => Math.Sin(x);
    [SabakaFunction("cos")] public static double Cos(double x) => Math.Cos(x);
    [SabakaFunction("tan")] public static double Tan(double x) => Math.Tan(x);
    [SabakaFunction("sqrt")] public static double Sqrt(double x) => Math.Sqrt(x);
    [SabakaFunction("log")] public static double Log(double x) => Math.Log(x);
    [SabakaFunction("pow")] public static double Pow(double x, double y) => Math.Pow(x, y);

    [SabakaFunction("abs")] public static int Abs(int x) => x == int.MinValue ? throw new RuntimeException("abs: integer overflow") : Math.Abs(x);
    [SabakaFunction("abs")] public static double Abs(double x) => Math.Abs(x);

    [SabakaFunction("max")] public static int Max(int a, int b) => Math.Max(a, b);
    [SabakaFunction("max")] public static double Max(double a, double b) => Math.Max(a, b);
    [SabakaFunction("min")] public static int Min(int a, int b) => Math.Min(a, b);
    [SabakaFunction("min")] public static double Min(double a, double b) => Math.Min(a, b);

    [SabakaFunction("floor")] public static int Floor(double x) => ToInt(Math.Floor(x), "floor");
    [SabakaFunction("ceil")] public static int Ceil(double x) => ToInt(Math.Ceiling(x), "ceil");
    [SabakaFunction("round")] public static int Round(double x) => ToInt(Math.Round(x, MidpointRounding.AwayFromZero), "round");

    /// <summary>Random float in [0, 1).</summary>
    [SabakaFunction("rand")] public static double Rand() => Rng.NextDouble();

    /// <summary>Random int in [0, max).</summary>
    [SabakaFunction("random")] public static int Random(int max) => max <= 0 ? throw new RuntimeException("random: max must be positive") : Rng.Next(max);

    /// <summary>Random int in [min, max].</summary>
    [SabakaFunction("random")] public static int Random(int min, int max) => min > max ? throw new RuntimeException("random: min must not exceed max") : Rng.Next(min, max + 1);

    private static int ToInt(double d, string fn) =>
        double.IsNaN(d) || d < int.MinValue || d > int.MaxValue ? throw new RuntimeException($"{fn}: result does not fit into int") : (int)d;
}

[SabakaModule("io", AutoImport = true, Description = "File access")]
public static class IoModule
{
    [SabakaFunction("readFile")] public static string ReadFile(string path) => File.ReadAllText(path);
    [SabakaFunction("readLines")] public static string[] ReadLines(string path) => File.ReadAllLines(path);
    [SabakaFunction("writeFile")] public static void WriteFile(string path, string text) => File.WriteAllText(path, text);
    [SabakaFunction("appendFile")] public static void AppendFile(string path, string text) => File.AppendAllText(path, text);
    [SabakaFunction("fileExists")] public static bool FileExists(string path) => File.Exists(path);
    [SabakaFunction("deleteFile")] public static void DeleteFile(string path) => File.Delete(path);
}

[SabakaModule("http", AutoImport = true, Description = "Blocking HTTP client")]
public static class HttpModule
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(30) };

    [SabakaFunction("httpGet")]
    public static string HttpGet(string url) => Client.GetStringAsync(url).GetAwaiter().GetResult();

    [SabakaFunction("httpPost")]
    public static string HttpPost(string url, string body) => Send(url, body, "text/plain");

    [SabakaFunction("httpPostJson")]
    public static string HttpPostJson(string url, string json) => Send(url, json, "application/json");

    private static string Send(string url, string body, string contentType)
    {
        using var content = new StringContent(body, Encoding.UTF8, contentType);
        using var response = Client.PostAsync(url, content).GetAwaiter().GetResult();
        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }
}

/// <summary>Methods added to the built-in <c>string</c> type: <c>"abc".upper()</c>.</summary>
[SabakaModule("strings", AutoImport = true, Description = "Methods of string")]
public static class StringExtensions
{
    [SabakaExtension("string")] public static string Upper(string self) => self.ToUpperInvariant();
    [SabakaExtension("string")] public static string Lower(string self) => self.ToLowerInvariant();
    [SabakaExtension("string")] public static string Trim(string self) => self.Trim();
    [SabakaExtension("string")] public static string[] Split(string self, string separator) =>
        separator.Length == 0 ? self.Select(c => c.ToString()).ToArray() : self.Split(separator);
    [SabakaExtension("string")] public static bool Contains(string self, string part) => self.Contains(part, StringComparison.Ordinal);
    [SabakaExtension("string")] public static bool StartsWith(string self, string part) => self.StartsWith(part, StringComparison.Ordinal);
    [SabakaExtension("string")] public static bool EndsWith(string self, string part) => self.EndsWith(part, StringComparison.Ordinal);
    [SabakaExtension("string")] public static string Replace(string self, string from, string to) => from.Length == 0 ? self : self.Replace(from, to);
    [SabakaExtension("string")] public static int IndexOf(string self, string part) => self.IndexOf(part, StringComparison.Ordinal);
    [SabakaExtension("string")] public static string Repeat(string self, int count) => count < 0 ? throw new RuntimeException("repeat: negative count") : string.Concat(Enumerable.Repeat(self, count));
    [SabakaExtension("string")] public static string Substring(string self, int start) => Slice(self, start, self.Length);
    [SabakaExtension("string")] public static string Substring(string self, int start, int length) => Slice(self, start, start + length);
    [SabakaExtension("string")] public static int ToInt(string self) => CoreModule.ParseInt(self);
    [SabakaExtension("string")] public static double ToFloat(string self) => CoreModule.ParseFloat(self);
    [SabakaExtension("string", IsProperty = true)] public static bool IsEmpty(string self) => self.Length == 0;

    private static string Slice(string s, int start, int end)
    {
        if (start < 0 || end > s.Length || start > end)
            throw new RuntimeException($"substring: range {start}..{end} is outside of a string of length {s.Length}");
        return s[start..end];
    }
}

/// <summary>Methods added to arrays (<c>T[]</c> matches every array type and infers T from the receiver).</summary>
[SabakaModule("arrays", AutoImport = true, Description = "Methods of arrays")]
public static class ArrayExtensions
{
    private static List<Value> List(Value self) => self.Array ?? throw new RuntimeException("Expected an array");

    [SabakaExtension("T[]", Signature = "void push(T item)")]
    public static void Push(Value self, Value item) => List(self).Add(item);

    [SabakaExtension("T[]", Signature = "T pop()")]
    public static Value Pop(Value self)
    {
        var l = List(self);
        if (l.Count == 0) throw new RuntimeException("pop: array is empty");
        var v = l[^1];
        l.RemoveAt(l.Count - 1);
        return v;
    }

    [SabakaExtension("T[]", Signature = "T first()")]
    public static Value First(Value self) => List(self) is { Count: > 0 } l ? l[0] : throw new RuntimeException("first: array is empty");

    [SabakaExtension("T[]", Signature = "T last()")]
    public static Value Last(Value self) => List(self) is { Count: > 0 } l ? l[^1] : throw new RuntimeException("last: array is empty");

    [SabakaExtension("T[]", Signature = "bool contains(T item)")]
    public static bool Contains(Value self, Value item) => List(self).Contains(item);

    [SabakaExtension("T[]", Signature = "int indexOf(T item)")]
    public static int IndexOf(Value self, Value item) => List(self).IndexOf(item);

    [SabakaExtension("T[]", Signature = "void insert(int index, T item)")]
    public static void Insert(Value self, int index, Value item)
    {
        var l = List(self);
        if (index < 0 || index > l.Count) throw new RuntimeException($"insert: index {index} is out of bounds (size {l.Count})");
        l.Insert(index, item);
    }

    [SabakaExtension("T[]", Signature = "T removeAt(int index)")]
    public static Value RemoveAt(Value self, int index)
    {
        var l = List(self);
        if ((uint)index >= (uint)l.Count) throw new RuntimeException($"removeAt: index {index} is out of bounds (size {l.Count})");
        var v = l[index];
        l.RemoveAt(index);
        return v;
    }

    [SabakaExtension("T[]", Signature = "void clear()")]
    public static void Clear(Value self) => List(self).Clear();

    [SabakaExtension("T[]", Signature = "void reverse()")]
    public static void Reverse(Value self) => List(self).Reverse();

    [SabakaExtension("T[]", Signature = "T[] slice(int start, int end)")]
    public static Value Slice(Value self, int start, int end)
    {
        var l = List(self);
        if (start < 0 || end > l.Count || start > end)
            throw new RuntimeException($"slice: range {start}..{end} is outside of an array of size {l.Count}");
        return Value.FromArray(l.GetRange(start, end - start));
    }

    [SabakaExtension("T[]", Signature = "string join(string separator)")]
    public static string Join(NativeContext ctx, Value self, string separator) =>
        string.Join(separator, List(self).Select(ctx.Stringify));

    [SabakaExtension("T[]", IsProperty = true, Signature = "bool isEmpty()")]
    public static bool IsEmpty(Value self) => List(self).Count == 0;
}

/// <summary>Methods available on every value.</summary>
[SabakaModule("objects", AutoImport = true, Description = "Methods of all values")]
public static class ObjectExtensions
{
    [SabakaExtension("dynamic", Signature = "string toString()")]
    public static string ToString(NativeContext ctx, Value self) => ctx.Stringify(self);

    [SabakaExtension("float")] public static int ToInt(double self) =>
        double.IsNaN(self) || self < int.MinValue || self > int.MaxValue ? throw new RuntimeException("toInt: value does not fit into int") : (int)self;

    [SabakaExtension("int")] public static double ToFloat(int self) => self;
}
