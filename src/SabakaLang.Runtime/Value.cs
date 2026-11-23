using System.Globalization;

namespace SabakaLang.Runtime;

public enum SabakaType { Null, Int, Float, Bool, String, Array, Object, Char, Enum }

/// <summary>A member of a script (or native) enum, carried inside an enum <see cref="Value"/>.</summary>
public sealed class EnumMember(string enumName, string name, int ordinal)
{
    public string EnumName { get; } = enumName;
    public string Name { get; } = name;
    public int Ordinal { get; } = ordinal;
    public override string ToString() => Name;
}

/// <summary>
/// A single SabakaLang runtime value. 16 bytes: a type tag, 8 bits of payload and an optional reference.
/// Arrays have reference semantics, strings are immutable, objects are <see cref="SabakaObject"/>.
/// </summary>
public readonly struct Value : IEquatable<Value>
{
    private readonly long _bits;
    private readonly object? _ref;

    public SabakaType Type { get; }

    private Value(SabakaType type, long bits = 0, object? reference = null)
    {
        Type = type;
        _bits = bits;
        _ref = reference;
    }

    public static readonly Value Null = default;

    public static Value FromInt(int v) => new(SabakaType.Int, v);
    public static Value FromFloat(double v) => new(SabakaType.Float, BitConverter.DoubleToInt64Bits(v));
    public static Value FromBool(bool v) => new(SabakaType.Bool, v ? 1 : 0);
    public static Value FromChar(char v) => new(SabakaType.Char, v);
    public static Value FromString(string? v) => v is null ? Null : new(SabakaType.String, 0, v);
    public static Value FromArray(List<Value> v) => new(SabakaType.Array, 0, v);
    public static Value FromObject(SabakaObject v) => new(SabakaType.Object, 0, v);
    public static Value FromEnum(EnumMember m) => new(SabakaType.Enum, m.Ordinal, m);

    /// <summary>Converts a plain C# value (int, double, bool, string, char, list, array, ...) to a <see cref="Value"/>.</summary>
    public static Value From(object? o) => o switch
    {
        null => Null,
        Value v => v,
        bool b => FromBool(b),
        int i => FromInt(i),
        short s => FromInt(s),
        byte by => FromInt(by),
        long l => l is >= int.MinValue and <= int.MaxValue
            ? FromInt((int)l)
            : throw new RuntimeException($"Integer {l} does not fit into int"),
        double d => FromFloat(d),
        float f => FromFloat(f),
        char c => FromChar(c),
        string str => FromString(str),
        SabakaObject obj => FromObject(obj),
        EnumMember em => FromEnum(em),
        List<Value> list => FromArray(list),
        System.Collections.IEnumerable e => FromArray(e.Cast<object?>().Select(From).ToList()),
        _ => throw new RuntimeException($"Cannot convert C# type '{o.GetType().Name}' to a SabakaLang value")
    };

    public bool IsNull => Type == SabakaType.Null;
    public bool IsNumber => Type is SabakaType.Int or SabakaType.Float;

    public int Int => Type switch
    {
        SabakaType.Int or SabakaType.Char or SabakaType.Enum => (int)_bits,
        _ => 0
    };

    public double Float => Type switch
    {
        SabakaType.Float => BitConverter.Int64BitsToDouble(_bits),
        SabakaType.Int => (int)_bits,
        _ => 0
    };

    public bool Bool => Type == SabakaType.Bool && _bits != 0;
    public char Char => Type == SabakaType.Char ? (char)_bits : '\0';
    public string String => _ref as string ?? (Type == SabakaType.Null ? "" : "");
    public List<Value>? Array => Type == SabakaType.Array ? (List<Value>?)_ref : null;
    public SabakaObject? Object => Type == SabakaType.Object ? (SabakaObject?)_ref : null;
    public EnumMember? Enum => Type == SabakaType.Enum ? (EnumMember?)_ref : null;

    public double ToDouble() => Type switch
    {
        SabakaType.Int => (int)_bits,
        SabakaType.Float => BitConverter.Int64BitsToDouble(_bits),
        _ => throw new RuntimeException($"Expected number, got {Type}")
    };

    /// <summary>Strict reference/value equality, matching the semantics of the <c>==</c> operator.</summary>
    public bool Equals(Value other)
    {
        if (IsNumber && other.IsNumber)
        {
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            return Type == SabakaType.Int && other.Type == SabakaType.Int
                ? _bits == other._bits
                : ToDouble() == other.ToDouble();
        }

        if (Type != other.Type) return false;

        return Type switch
        {
            SabakaType.Null => true,
            SabakaType.Bool or SabakaType.Char => _bits == other._bits,
            SabakaType.String => string.Equals((string?)_ref, (string?)other._ref, StringComparison.Ordinal),
            SabakaType.Enum => ((EnumMember)_ref!).EnumName == ((EnumMember)other._ref!).EnumName && _bits == other._bits,
            _ => ReferenceEquals(_ref, other._ref)
        };
    }

    public override bool Equals(object? obj) => obj is Value v && Equals(v);

    public override int GetHashCode() => Type switch
    {
        SabakaType.Null => 0,
        SabakaType.Int => ((double)(int)_bits).GetHashCode(),
        SabakaType.Float => Float.GetHashCode(),
        SabakaType.Bool or SabakaType.Char => HashCode.Combine(Type, _bits),
        SabakaType.String => _ref!.GetHashCode(),
        SabakaType.Enum => HashCode.Combine(((EnumMember)_ref!).EnumName, _bits),
        _ => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_ref)
    };

    public static bool operator ==(Value a, Value b) => a.Equals(b);
    public static bool operator !=(Value a, Value b) => !a.Equals(b);

    /// <summary>Name of the SabakaLang type of this value, for diagnostics (<c>int</c>, <c>string</c>, <c>Dog</c>...).</summary>
    public string TypeName => Type switch
    {
        SabakaType.Null => "null",
        SabakaType.Int => "int",
        SabakaType.Float => "float",
        SabakaType.Bool => "bool",
        SabakaType.String => "string",
        SabakaType.Char => "char",
        SabakaType.Array => "array",
        SabakaType.Object => SabakaObject.DisplayName(Object!.ClassName),
        SabakaType.Enum => Enum!.EnumName,
        _ => "?"
    };

    /// <summary>Default stringification (does not call user-defined <c>toString</c>; use <see cref="NativeContext.Stringify"/> for that).</summary>
    public override string ToString() => Type switch
    {
        SabakaType.Null => "null",
        SabakaType.Int => ((int)_bits).ToString(CultureInfo.InvariantCulture),
        SabakaType.Float => Float.ToString(CultureInfo.InvariantCulture),
        SabakaType.Bool => Bool ? "true" : "false",
        SabakaType.String => String,
        SabakaType.Char => Char.ToString(),
        SabakaType.Array => "[" + string.Join(", ", Array!.Select(v => v.ToString())) + "]",
        SabakaType.Object => Object!.ToString(),
        SabakaType.Enum => Enum!.Name,
        _ => "?"
    };
}
