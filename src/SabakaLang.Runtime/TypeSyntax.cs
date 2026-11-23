using System.Text;

namespace SabakaLang.Runtime;

/// <summary>
/// Textual type such as <c>int</c>, <c>string[]</c>, <c>Box&lt;int&gt;</c>, <c>T[][]?</c> or <c>math.Vec2</c>.
/// Used for native signatures and for generic type arguments.
/// </summary>
public sealed record TypeSyntax(string Name, IReadOnlyList<TypeSyntax> Args, int ArrayDepth, bool IsNullable)
{
    public static TypeSyntax? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var pos = 0;
        var t = ParseType(text, ref pos);
        SkipWs(text, ref pos);
        return t is not null && pos == text.Length ? t : null;
    }

    public static TypeSyntax Parse(string text) =>
        TryParse(text) ?? throw new ArgumentException($"Invalid type '{text}'.", nameof(text));

    private static void SkipWs(string s, ref int p)
    {
        while (p < s.Length && char.IsWhiteSpace(s[p])) p++;
    }

    private static TypeSyntax? ParseType(string s, ref int p)
    {
        SkipWs(s, ref p);
        var start = p;
        if (p >= s.Length || !(char.IsLetter(s[p]) || s[p] == '_')) return null;
        while (p < s.Length && (char.IsLetterOrDigit(s[p]) || s[p] == '_' ||
                                s[p] == '.' && p + 1 < s.Length && (char.IsLetter(s[p + 1]) || s[p + 1] == '_')))
            p++;
        var name = s[start..p];

        var args = new List<TypeSyntax>();
        SkipWs(s, ref p);
        if (p < s.Length && s[p] == '<')
        {
            p++;
            while (true)
            {
                var a = ParseType(s, ref p);
                if (a is null) return null;
                args.Add(a);
                SkipWs(s, ref p);
                if (p < s.Length && s[p] == ',') { p++; continue; }
                if (p < s.Length && s[p] == '>') { p++; break; }
                return null;
            }
        }

        var depth = 0;
        while (true)
        {
            SkipWs(s, ref p);
            if (p + 1 < s.Length && s[p] == '[' && s[p + 1] == ']') { p += 2; depth++; }
            else break;
        }

        var nullable = false;
        SkipWs(s, ref p);
        if (p < s.Length && s[p] == '?') { p++; nullable = true; }

        return new TypeSyntax(name, args, depth, nullable);
    }

    public override string ToString()
    {
        var sb = new StringBuilder(Name);
        if (Args.Count > 0) sb.Append('<').Append(string.Join(",", Args)).Append('>');
        for (var i = 0; i < ArrayDepth; i++) sb.Append("[]");
        if (IsNullable) sb.Append('?');
        return sb.ToString();
    }

    public bool Equals(TypeSyntax? other) => other is not null && ToString() == other.ToString();
    public override int GetHashCode() => ToString().GetHashCode();
}
