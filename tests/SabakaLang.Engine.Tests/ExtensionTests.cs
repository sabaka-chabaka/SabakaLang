using SabakaLang.Extensions;
using SabakaLang.Runtime;

namespace SabakaLang.Engine.Tests;

// ------------------------------------------------------------------ sample extensions (attribute based)

[SabakaModule("geometry", Description = "Vectors and helpers")]
public static class GeometryModule
{
    [SabakaConstant("ORIGIN_NAME")] public static readonly string OriginName = "origin";
    [SabakaConstant("SCALE")] public static double Scale => 2.5;

    [SabakaEnum("Axis")] public enum AxisKind { X, Y }

    [SabakaClass("Vec2", Description = "2D vector")]
    public sealed class Vec2
    {
        [SabakaConstructor]
        public Vec2(double x, double y) { X = x; Y = y; }

        [SabakaProperty] public double X { get; set; }
        [SabakaProperty] public double Y { get; set; }
        [SabakaProperty] public double Length => Math.Sqrt(X * X + Y * Y);

        [SabakaMethod] public Vec2 Add(Vec2 other) => new(X + other.X, Y + other.Y);
        [SabakaMethod] public Vec2 Scale(double k) => new(X * k, Y * k);
        [SabakaMethod] public double Get(AxisKind axis) => axis == AxisKind.X ? X : Y;
        [SabakaMethod("toString")] public string Describe() => $"({X}, {Y})";
        [SabakaMethod] public static Vec2 Parse(string text)
        {
            var p = text.Split(',');
            return new Vec2(double.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture), double.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [SabakaFunction("dot")]
    public static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

    [SabakaFunction("sum")]
    public static int Sum(params int[] numbers) => numbers.Sum();

    [SabakaFunction("sumAll")]
    public static double SumAll(double[] values) => values.Sum();

    [SabakaFunction("names")]
    public static List<string> Names() => ["a", "b"];

    [SabakaFunction("fail")]
    public static void Fail(string message) => throw new RuntimeException("boom: " + message);

    [SabakaFunction("crash")]
    public static void Crash() => throw new InvalidOperationException("bad state");

    [SabakaFunction("applyTwice")]
    public static int ApplyTwice(NativeContext ctx, string functionName, int value) =>
        ctx.CallFunction(functionName, Value.FromInt(ctx.CallFunction(functionName, Value.FromInt(value)).Int)).Int;

    [SabakaFunction("describe")]
    public static string Describe(NativeContext ctx, Value any) => "<" + ctx.Stringify(any) + ">";
}

[SabakaModule("shout", AutoImport = true)]
public static class ShoutExtensions
{
    [SabakaExtension("string")] public static string Shout(string self) => self.ToUpperInvariant() + "!";
    [SabakaExtension("int", IsProperty = true)] public static bool IsEven(int self) => self % 2 == 0;
    [SabakaExtension("T[]", Signature = "T middle()")] public static Value Middle(Value self) => self.Array![self.Array.Count / 2];
}

/// <summary>Extension that does not use attributes at all.</summary>
public sealed class CounterExtension : ISabakaExtension
{
    public string Name => "counter";

    public void Configure(ModuleBuilder module) => module
        .Class("Counter", c => c
            .Field("int count", Value.FromInt(0))
            .Constructor("(int start)", (ctx, self, args) => self.Fields["count"] = args[0])
            .Method("int next()", (ctx, self, _) =>
            {
                var o = self.Object!;
                o.Fields["count"] = Value.FromInt(o.Fields["count"].Int + 1);
                return o.Fields["count"];
            })
            .Property("string label", (ctx, self) => Value.FromString("counter#" + self.Object!.Fields["count"].Int))
            .StaticMethod("Counter create()", (ctx, _, _) => ctx.NewObject("Counter", Value.FromInt(100))))
        .Function("int square(int x)", (ctx, a) => Value.FromInt(a[0].Int * a[0].Int))
        .Function("string pick(string a)", (ctx, a) => a[0])
        .Function("int pick(int a)", (ctx, a) => Value.FromInt(a[0].Int + 1000));
}

public class ExtensionTests
{
    private static void Setup(SabakaEngine e)
    {
        // finds every [SabakaModule] type and ISabakaExtension implementation in the assembly
        e.Extensions.RegisterAssembly(typeof(CounterExtension).Assembly);
    }

    private static string Run(string code) => Sabaka.Run(code, Setup);

    [Fact]
    public void NativeFunction_WithImport()
    {
        Assert.Equal("11", Run("import \"geometry\"; print(dot(new Vec2(1.0, 2.0), new Vec2(3.0, 4.0)));"));
        Assert.Contains("Undefined symbol 'dot'", Sabaka.CompileErrors("print(dot(1, 2));", Setup)[0]);
    }

    [Fact]
    public void NativeFunction_ViaModuleAlias() =>
        Assert.Equal("11", Run("import \"geometry\" as g; print(g.dot(new g.Vec2(1.0, 2.0), new g.Vec2(3.0, 4.0)));"));

    [Fact]
    public void NativeFunction_SelectiveImport() =>
        Assert.Equal("6", Run("import \"geometry\" from sum; print(sum(1, 2, 3));"));

    [Fact]
    public void NativeClass_ConstructorPropertiesMethods()
    {
        Assert.Equal("5 3 4", Run("import \"geometry\"; Vec2 v = new Vec2(3.0, 4.0); print(v.length, v.x, v.y);"));
        Assert.Equal("(4, 6)", Run("import \"geometry\"; Vec2 a = new Vec2(1.0, 2.0); Vec2 b = a.add(new Vec2(3.0, 4.0)); print(b);"));
        Assert.Equal("(2, 4)", Run("import \"geometry\"; Vec2 a = new Vec2(1.0, 2.0); print(a.scale(2));"));
    }

    [Fact]
    public void NativeClass_PropertySetter_AndCompoundAssignment() =>
        Assert.Equal("10 6", Run("import \"geometry\"; Vec2 v = new Vec2(1.0, 2.0); v.x = 10; v.y += 4; print(v.x, v.y);"));

    [Fact]
    public void NativeClass_StaticMethod() =>
        Assert.Equal("(1.5, 2)", Run("import \"geometry\"; Vec2 v = Vec2.parse(\"1.5,2\"); print(v);"));

    [Fact]
    public void NativeClass_Interpolation_UsesToString() =>
        Assert.Equal("v=(1, 2)", Run("import \"geometry\"; Vec2 v = new Vec2(1.0, 2.0); print($\"v={v}\");"));

    [Fact]
    public void NativeClass_ReadOnlyProperty_IsCompileError() =>
        Assert.Contains("read-only", Sabaka.CompileErrors("import \"geometry\"; Vec2 v = new Vec2(1.0, 2.0); v.length = 3;", Setup)[0]);

    [Fact]
    public void NativeClass_WrongArguments_AreReported()
    {
        Assert.Contains("Vec2", Sabaka.CompileErrors("import \"geometry\"; Vec2 v = new Vec2(1.0);", Setup)[0]);
        Assert.Contains("Cannot convert", Sabaka.CompileErrors("import \"geometry\"; Vec2 v = new Vec2(\"a\", 2.0);", Setup)[0]);
    }

    [Fact]
    public void NativeEnum_AndConstants() =>
        Assert.Equal("3 origin 2.5", Run("import \"geometry\"; Vec2 v = new Vec2(3.0, 4.0); Axis a = Axis.X; print(v.get(a), ORIGIN_NAME, SCALE);"));

    [Fact]
    public void NativeFunction_VariadicAndArrays()
    {
        Assert.Equal("6", Run("import \"geometry\"; print(sum(1, 2, 3));"));
        Assert.Equal("0", Run("import \"geometry\"; print(sum());"));
        Assert.Equal("4", Run("import \"geometry\"; print(sumAll([1.5, 2.5]));"));
        Assert.Equal("[a, b]", Run("import \"geometry\"; print(names());"));
    }

    [Fact]
    public void NativeFunction_CallsBackIntoScript() =>
        Assert.Equal("36", Run("import \"geometry\"; int triple(int x) { return x * 3; } print(applyTwice(\"triple\", 4));"));

    [Fact]
    public void NativeFunction_UsesUserToString() =>
        Assert.Equal("<P!>", Run("import \"geometry\"; class P { string toString() { return \"P!\"; } } print(describe(new P()));"));

    [Fact]
    public void NativeErrors_BecomeRuntimeErrors()
    {
        Assert.Contains("boom: x", Sabaka.RuntimeError("import \"geometry\"; fail(\"x\");", Setup));
        Assert.Contains("bad state", Sabaka.RuntimeError("import \"geometry\"; crash();", Setup));
    }

    [Fact]
    public void ExtensionMethods_OnBuiltinTypes()
    {
        Assert.Equal("HI!", Run("print(\"hi\".shout());"));
        Assert.Equal("true false", Run("print(4.isEven, 3.isEven);"));
        Assert.Equal("2 b", Run("print([1, 2, 3].middle(), [\"a\", \"b\", \"c\"].middle());"));
    }

    [Fact]
    public void ExtensionMethods_AreTyped()
    {
        Assert.Equal("int", Run("int m = [1, 2, 3].middle(); print(typeOf(m));"));
        Assert.Contains("Cannot convert", Sabaka.CompileErrors("string m = [1, 2, 3].middle();", Setup)[0]);
        Assert.Contains("has no method 'shout'", Sabaka.CompileErrors("print(5.shout());", Setup)[0]);
    }

    [Fact]
    public void ExtensionMethods_OnDynamicValues_AreResolvedAtRuntime() =>
        Assert.Equal("HELLO!", Run("dynamic d = \"hello\"; print(d.shout());"));

    [Fact]
    public void FluentBuilder_Class_AndOverloads()
    {
        Assert.Equal("1 2 counter#2", Run("import \"counter\"; Counter c = new Counter(0); print(c.next(), c.next(), c.label);"));
        Assert.Equal("101", Run("import \"counter\"; Counter c = Counter.create(); print(c.next());"));
        Assert.Equal("49 7 1007", Run("import \"counter\"; print(square(7), pick(\"7\") == \"7\" ? 7 : 0, pick(7));").Replace("49 7 1007", "49 7 1007"));
    }

    [Fact]
    public void Registry_RejectsDuplicates()
    {
        var engine = new SabakaEngine();
        engine.Extensions.RegisterType(typeof(GeometryModule));
        Assert.Throws<InvalidOperationException>(() => engine.Extensions.RegisterType(typeof(GeometryModule)));
    }

    [Fact]
    public void Signature_Parser()
    {
        var s = NativeSignature.Parse("Map<string, int> get(string key, int... rest)");
        Assert.Equal("get", s.Name);
        Assert.Equal("Map<string,int>", s.ReturnType);
        Assert.True(s.IsVariadic);
        Assert.Equal(2, s.Parameters.Count);
        Assert.Equal("void", NativeSignature.Parse("(int a)").ReturnType);
    }

    [Fact]
    public void Sandbox_StandardLibraryCanBeTrimmed()
    {
        var engine = new SabakaEngine(standardLibrary: false) { Out = new StringWriter() };
        SabakaLang.StdLib.StandardLibrary.Install(engine.Extensions, name => name is "core" or "math");
        var errors = engine.Compile("string s = readFile(\"x\");").Errors;
        Assert.Contains("Undefined symbol 'readFile'", errors[0].ToString());
    }

    [Fact]
    public void Host_IsAvailableToNatives()
    {
        var engine = new SabakaEngine { Out = new StringWriter(), Host = "secret" };
        engine.Extensions.Register("host", m => m
            .AutoImport()
            .Function("string hostValue()", (ctx, _) => Value.FromString((string?)ctx.Host)));
        var result = engine.Run("print(hostValue());");
        Assert.Equal("secret", engine.Out.ToString()!.Trim());
        Assert.Null(result.RuntimeError);
    }
}
