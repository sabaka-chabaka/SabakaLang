namespace SabakaLang.Compiler.Runtime;

public readonly struct Value
{
    public readonly SabakaType Type;
 
    public readonly int    Int;
    public readonly double Float;
    public readonly bool   Bool;
    public readonly string String;
    public readonly char Char;
 
    public readonly List<Value>?           Array;
    public readonly SabakaObject?          Object;
 
    private Value(SabakaType t, int i = 0, double f = 0, bool b = false,
                  string s = "", char c = '\0', List<Value>? arr = null, SabakaObject? obj = null)
    {
        Type   = t; Int = i; Float = f; Bool = b;
        String = s;
        Char = c; Array = arr; Object = obj;
    }
 
    public static readonly Value Null    = new(SabakaType.Null);
    public static Value FromInt(int v)           => new(SabakaType.Int,   i: v);
    public static Value FromFloat(double v)      => new(SabakaType.Float, f: v);
    public static Value FromBool(bool v)         => new(SabakaType.Bool,  b: v);
    public static Value FromString(string? v)     => new(SabakaType.String, s: v ?? "");
    public static Value FromChar(char? v)     => new(SabakaType.Char, c: v ?? '\0');
    public static Value FromArray(List<Value> v) => new(SabakaType.Array,  arr: v);
    public static Value FromObject(SabakaObject v) => new(SabakaType.Object, obj: v);
 
    public bool IsNull   => Type == SabakaType.Null;
    public bool IsNumber => Type is SabakaType.Int or SabakaType.Float;
 
    public double ToDouble() => Type switch
    {
        SabakaType.Int   => Int,
        SabakaType.Float => Float,
        _ => throw new RuntimeException($"Expected number, got {Type}")
    };
 
    public override string ToString() => Type switch
    {
        SabakaType.Null   => "null",
        SabakaType.Int    => Int.ToString(System.Globalization.CultureInfo.InvariantCulture),
        SabakaType.Float  => Float.ToString(System.Globalization.CultureInfo.InvariantCulture),
        SabakaType.Bool   => Bool ? "true" : "false",
        SabakaType.String => String,
        SabakaType.Array  => "[" + string.Join(", ", Array!.Select(v => v.ToString())) + "]",
        SabakaType.Object => Object!.ToString(),
        SabakaType.Char   => Char.ToString(),
        _ => "?"
    };
}
