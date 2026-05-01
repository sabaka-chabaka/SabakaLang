namespace SabakaLang.Compiler.Runtime;

public sealed class SabakaObject(string className)
{
    public string ClassName { get; } = className;
    public Dictionary<string, Value> Fields { get; } = new();

    public SabakaObject Clone()
    {
        var c = new SabakaObject(ClassName);
        foreach (var kv in Fields) c.Fields[kv.Key] = kv.Value;
        return c;
    }
 
    public override string ToString() =>
        $"{ClassName} {{ {string.Join(", ", Fields.Select(kv => $"{kv.Key}: {kv.Value}"))} }}";
}
