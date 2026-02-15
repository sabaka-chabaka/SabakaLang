namespace SabakaLang.Types;

public struct Value
{
    public SabakaType Type;

    public int Int;
    public double Float;
    public bool Bool;
    public string String;

    public static Value FromInt(int v)
        => new Value { Type = SabakaType.Int, Int = v };

    public static Value FromFloat(double v)
        => new Value { Type = SabakaType.Float, Float = v };

    public static Value FromBool(bool v)
        => new Value { Type = SabakaType.Bool, Bool = v };
    
    public static Value FromString(string value) 
        => new Value { Type = SabakaType.String, String = value };
    
    public override string ToString()
    {
        return Type switch
        {
            SabakaType.Int => Int.ToString(),
            SabakaType.Float => Float.ToString(),
            SabakaType.Bool => Bool ? "true" : "false",
            SabakaType.String => String,
            _ => "null"
        };
    }

}
