using System.Collections;
using System.Reflection;

namespace SabakaLang.Extensions;

/// <summary>Maps C# types to SabakaLang type names and converts values in both directions.</summary>
public static class Marshaller
{
    private static readonly NullabilityInfoContext Nullability = new();

    public static string Camel(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];

    public static string ClassName(Type t) =>
        t.GetCustomAttribute<SabakaClassAttribute>()?.Name ?? t.Name;

    public static string EnumName(Type t) =>
        t.GetCustomAttribute<SabakaEnumAttribute>()?.Name ?? t.Name;

    private static Type? ElementOf(Type t)
    {
        if (t.IsArray) return t.GetElementType();
        if (t == typeof(string)) return null;
        foreach (var i in new[] { t }.Concat(t.GetInterfaces()))
            if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return i.GetGenericArguments()[0];
        return null;
    }

    /// <summary>SabakaLang type name for a C# type, e.g. <c>double → float</c>, <c>List&lt;string&gt; → string[]</c>.</summary>
    public static string MapType(Type t, NullabilityInfo? info = null)
    {
        var nullable = info?.ReadState == NullabilityState.Nullable;
        if (Nullable.GetUnderlyingType(t) is { } under) return MapType(under) + "?";
        string name;
        if (t == typeof(void)) return "void";
        else if (t == typeof(bool)) name = "bool";
        else if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte) ||
                 t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort) || t == typeof(sbyte)) name = "int";
        else if (t == typeof(double) || t == typeof(float) || t == typeof(decimal)) name = "float";
        else if (t == typeof(char)) name = "char";
        else if (t == typeof(string)) name = "string";
        else if (t == typeof(Value) || t == typeof(object) || t == typeof(SabakaObject)) return "dynamic";
        else if (t.IsEnum && t.GetCustomAttribute<SabakaEnumAttribute>() is not null) name = EnumName(t);
        else if (t.GetCustomAttribute<SabakaClassAttribute>() is not null) name = ClassName(t);
        else if (ElementOf(t) is { } el)
            name = MapType(el, info?.ElementType ?? info?.GenericTypeArguments.FirstOrDefault()) + "[]";
        else
            throw new NotSupportedException(
                $"Cannot map C# type '{t.FullName}' to a SabakaLang type. Use Value/object (→ dynamic), a primitive, or tag the class with [SabakaClass].");
        return nullable && !name.EndsWith('?') ? name + "?" : name;
    }

    public static string MapParameter(ParameterInfo p) =>
        MapType(p.ParameterType, SafeInfo(() => Nullability.Create(p)));

    public static string MapReturn(MethodInfo m) =>
        MapType(m.ReturnType, SafeInfo(() => Nullability.Create(m.ReturnParameter)));

    private static NullabilityInfo? SafeInfo(Func<NullabilityInfo> f)
    {
        try { return f(); } catch { return null; }
    }

    // ---------------------------------------------------------------- Value -> C#

    public static object? ToHost(Value v, Type target, NativeContext ctx)
    {
        if (target == typeof(Value)) return v;
        if (target == typeof(object)) return ToPlain(v);
        var under = Nullable.GetUnderlyingType(target);
        if (v.IsNull)
        {
            if (!target.IsValueType || under is not null) return null;
            throw new RuntimeException($"Expected {MapType(target)}, got null");
        }
        target = under ?? target;

        if (target == typeof(int)) return Need(v, SabakaType.Int, "int").Int;
        if (target == typeof(long)) return (long)Need(v, SabakaType.Int, "int").Int;
        if (target == typeof(short)) return checked((short)Need(v, SabakaType.Int, "int").Int);
        if (target == typeof(byte)) return checked((byte)Need(v, SabakaType.Int, "int").Int);
        if (target == typeof(uint)) return checked((uint)Need(v, SabakaType.Int, "int").Int);
        if (target == typeof(ulong)) return checked((ulong)Need(v, SabakaType.Int, "int").Int);
        if (target == typeof(double)) return v.IsNumber ? v.ToDouble() : throw Mismatch("float", v);
        if (target == typeof(float)) return v.IsNumber ? (float)v.ToDouble() : throw Mismatch("float", v);
        if (target == typeof(decimal)) return v.IsNumber ? (decimal)v.ToDouble() : throw Mismatch("float", v);
        if (target == typeof(bool)) return Need(v, SabakaType.Bool, "bool").Bool;
        if (target == typeof(char))
            return v.Type == SabakaType.Char ? v.Char
                : v.Type == SabakaType.String && v.String.Length == 1 ? v.String[0]
                : throw Mismatch("char", v);
        if (target == typeof(string))
            return v.Type == SabakaType.String ? v.String
                : v.Type == SabakaType.Char ? v.Char.ToString()
                : throw Mismatch("string", v);
        if (target == typeof(SabakaObject)) return Need(v, SabakaType.Object, "object").Object;

        if (target.IsEnum)
        {
            if (v.Type == SabakaType.Enum) return Enum.Parse(target, v.Enum!.Name);
            if (v.Type == SabakaType.Int) return Enum.ToObject(target, v.Int);
            throw Mismatch(EnumName(target), v);
        }

        if (target.GetCustomAttribute<SabakaClassAttribute>() is not null || ctx.Registry.FindClassByClrType(target) is not null)
        {
            var obj = Need(v, SabakaType.Object, ClassName(target)).Object!;
            return target.IsInstanceOfType(obj.Native)
                ? obj.Native
                : throw new RuntimeException($"Expected {ClassName(target)}, got {obj.ShortName}");
        }

        if (target.IsArray)
        {
            var el = target.GetElementType()!;
            var src = Need(v, SabakaType.Array, MapType(target)).Array!;
            var arr = Array.CreateInstance(el, src.Count);
            for (var i = 0; i < src.Count; i++) arr.SetValue(ToHost(src[i], el, ctx), i);
            return arr;
        }

        if (ElementOf(target) is { } elem && target.IsAssignableFrom(typeof(List<>).MakeGenericType(elem)))
        {
            var src = Need(v, SabakaType.Array, MapType(target)).Array!;
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elem))!;
            foreach (var x in src) list.Add(ToHost(x, elem, ctx));
            return list;
        }

        throw new RuntimeException($"Cannot convert value to C# type '{target.Name}'");
    }

    private static Value Need(Value v, SabakaType t, string name) =>
        v.Type == t ? v : throw Mismatch(name, v);

    private static RuntimeException Mismatch(string expected, Value v) =>
        new($"Expected {expected}, got {v.TypeName}");

    /// <summary>Value → "natural" C# object (int, double, string, ..., native state, object?[] for arrays).</summary>
    public static object? ToPlain(Value v) => v.Type switch
    {
        SabakaType.Null => null,
        SabakaType.Int => v.Int,
        SabakaType.Float => v.Float,
        SabakaType.Bool => v.Bool,
        SabakaType.Char => v.Char,
        SabakaType.String => v.String,
        SabakaType.Enum => v.Enum!.Name,
        SabakaType.Array => v.Array!.Select(ToPlain).ToArray(),
        SabakaType.Object => v.Object!.Native ?? v.Object,
        _ => null
    };

    // ---------------------------------------------------------------- C# -> Value

    public static Value ToValue(object? o, NativeContext ctx)
    {
        switch (o)
        {
            case null: return Value.Null;
            case Value v: return v;
            case bool or int or short or byte or long or double or float or char or string or SabakaObject or EnumMember:
                return Value.From(o);
            case uint ui: return ui <= int.MaxValue ? Value.FromInt((int)ui) : throw new RuntimeException($"Integer {ui} does not fit into int");
            case ulong ul: return ul <= int.MaxValue ? Value.FromInt((int)ul) : throw new RuntimeException($"Integer {ul} does not fit into int");
            case decimal d: return Value.FromFloat((double)d);
            case Enum e:
            {
                var t = e.GetType();
                var names = Enum.GetNames(t);
                var idx = Array.IndexOf(names, e.ToString());
                return Value.FromEnum(new EnumMember(EnumName(t), names[Math.Max(idx, 0)], Math.Max(idx, 0)));
            }
        }

        var type = o.GetType();
        if (ctx.Registry.FindClassByClrType(type) is not null || type.GetCustomAttribute<SabakaClassAttribute>() is not null)
            return Value.FromObject(ctx.Wrap(o));

        if (o is IEnumerable seq)
            return Value.FromArray(seq.Cast<object?>().Select(x => ToValue(x, ctx)).ToList());

        throw new RuntimeException(
            $"Cannot convert C# type '{type.Name}' to a SabakaLang value (tag it with [SabakaClass] or return Value).");
    }
}
