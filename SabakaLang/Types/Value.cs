using System.Globalization;

namespace SabakaLang.Types;

public struct Value
{
    public SabakaType Type;

    public int Int;
    public double Float;
    public bool Bool;
    public string String;
    public List<Value>? Array;
    public Dictionary<string, Value>? Struct;
    public Dictionary<string, Value>? ObjectFields;
    public string? ClassName;

    public static Value FromInt(int v)
        => new Value { Type = SabakaType.Int, Int = v };

    public static Value FromFloat(double v)
        => new Value { Type = SabakaType.Float, Float = v };

    public static Value FromBool(bool v)
        => new Value { Type = SabakaType.Bool, Bool = v };

    public static Value FromString(string value)
        => new Value { Type = SabakaType.String, String = value };

    public static Value FromArray(List<Value> values) => new Value{ Type = SabakaType.Array, Array = values};
    public static Value FromStruct(Dictionary<string, Value> fields)
    {
        return new Value
        {
            Type = SabakaType.Struct,
            Struct = fields
        };
    }



public override string ToString()
    {
        return Type switch
        {
            SabakaType.Int => Int.ToString(CultureInfo.InvariantCulture),
            SabakaType.Float => Float.ToString(CultureInfo.InvariantCulture),
            SabakaType.Bool => Bool ? "true" : "false",
            SabakaType.String => String,
            SabakaType.Array => $"[{string.Join(", ", Array!.Select(v => v.ToString()))}]",
            SabakaType.Struct => "struct { " + string.Join(", ", Struct!.Select(kv => $"{kv.Key}: {kv.Value}")) + " }",
            _ => "null"
        };
    }

}
