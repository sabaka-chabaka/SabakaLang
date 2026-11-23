namespace SabakaLang.Extensions;

public sealed record NativeParameter(string Type, string Name);

/// <summary>
/// Signature of a native member, written in SabakaLang syntax:
/// <c>int add(int a, int b)</c>, <c>void print(dynamic... values)</c>, <c>T first()</c>, constructor form <c>(string name)</c>.
/// Single-letter names that are not types (<c>T</c>, <c>K</c>) are generic type variables inferred at the call site.
/// </summary>
public sealed class NativeSignature
{
    public string Name { get; }
    public string ReturnType { get; }
    public IReadOnlyList<NativeParameter> Parameters { get; }

    /// <summary>The last parameter accepts any number of arguments (<c>type... name</c>).</summary>
    public bool IsVariadic { get; }

    public NativeSignature(string name, string returnType, IReadOnlyList<NativeParameter> parameters, bool isVariadic = false)
    {
        Name = name;
        ReturnType = Normalize(returnType);
        Parameters = parameters.Select(p => p with { Type = Normalize(p.Type) }).ToList();
        IsVariadic = isVariadic;
    }

    public string ParameterKey => string.Join(",", Parameters.Select((p, i) => IsVariadic && i == Parameters.Count - 1 ? p.Type + "..." : p.Type));

    public int MinArgs => IsVariadic ? Parameters.Count - 1 : Parameters.Count;

    public override string ToString() =>
        $"{ReturnType} {Name}({string.Join(", ", Parameters.Select((p, i) => (IsVariadic && i == Parameters.Count - 1 ? p.Type + "..." : p.Type) + " " + p.Name))})";

    private static string Normalize(string type)
    {
        var t = TypeSyntax.TryParse(type) ?? throw new ArgumentException($"Invalid type '{type}' in native signature.");
        return t.ToString();
    }

    /// <summary>Parses <c>"returnType name(type a, type b)"</c>. For constructors pass <c>"(type a)"</c>.</summary>
    public static NativeSignature Parse(string text)
    {
        var open = text.IndexOf('(');
        var close = text.LastIndexOf(')');
        if (open < 0 || close < open)
            throw new ArgumentException($"Invalid native signature '{text}': expected 'type name(params)'.", nameof(text));

        var head = text[..open].Trim();
        var name = "";
        var ret = "void";
        if (head.Length > 0)
        {
            var i = head.Length;
            while (i > 0 && (char.IsLetterOrDigit(head[i - 1]) || head[i - 1] == '_')) i--;
            name = head[i..];
            ret = head[..i].Trim();
            if (name.Length == 0)
                throw new ArgumentException($"Invalid native signature '{text}': missing name.", nameof(text));
            if (ret.Length == 0) ret = "void";
        }

        var parms = new List<NativeParameter>();
        var variadic = false;
        var inner = text[(open + 1)..close];
        var idx = 0;
        foreach (var raw in SplitTopLevel(inner))
        {
            var p = raw.Trim();
            if (p.Length == 0) continue;
            if (variadic) throw new ArgumentException($"Invalid native signature '{text}': variadic parameter must be last.");
            if (p.Contains("..."))
            {
                variadic = true;
                p = p.Replace("...", " ").Trim();
            }

            var cut = p.Length;
            while (cut > 0 && (char.IsLetterOrDigit(p[cut - 1]) || p[cut - 1] == '_')) cut--;
            string pType, pName;
            // "type name" – or just "type" (name omitted)
            if (cut > 0 && cut < p.Length && char.IsWhiteSpace(p[cut - 1]))
            {
                pName = p[cut..];
                pType = p[..cut].Trim();
            }
            else
            {
                pType = p;
                pName = $"arg{idx}";
            }
            parms.Add(new NativeParameter(pType, pName));
            idx++;
        }

        return new NativeSignature(name, ret, parms, variadic);
    }

    private static IEnumerable<string> SplitTopLevel(string s)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '<') depth++;
            else if (s[i] == '>') depth--;
            else if (s[i] == ',' && depth == 0)
            {
                yield return s[start..i];
                start = i + 1;
            }
        }
        yield return s[start..];
    }
}
