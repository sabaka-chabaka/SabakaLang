using System.Text.Encodings.Web;
using System.Text.Json;
using SabakaLang.Extensions;
using SabakaLang.Runtime;

namespace SabakaLang.AspNet;

/// <summary>JSON helpers for scripts: <c>Json.quote(s)</c>, <c>Json.stringify(value)</c>, <c>Json.get(text, "a.b.0")</c>.</summary>
[SabakaClass("Json", Module = "web", Description = "JSON helpers")]
public sealed class Json
{
    private Json() { }

    private static readonly JavaScriptEncoder Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    /// <summary>A properly escaped JSON string literal, including the quotes.</summary>
    [SabakaMethod("quote")]
    public static string Quote(string s) => JsonSerializer.Serialize(s, new JsonSerializerOptions { Encoder = Encoder });

    /// <summary>Numbers, bool, strings, enums, arrays and script objects (all their fields). Native objects are not supported.</summary>
    [SabakaMethod("stringify")]
    public static string Stringify(Value value)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Encoder = Encoder }))
            Write(w, value, 0);
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>Reads a value by dotted path ("user.tags.0"). Strings/numbers/bools come back as text, objects/arrays as raw JSON, missing paths as null.</summary>
    [SabakaMethod("get")]
    public static string? Get(string json, string path)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { throw new RuntimeException("Json.get: invalid JSON"); }

        using (doc)
        {
            var cur = doc.RootElement;
            foreach (var seg in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (cur.ValueKind == JsonValueKind.Object && cur.TryGetProperty(seg, out var p)) cur = p;
                else if (cur.ValueKind == JsonValueKind.Array && int.TryParse(seg, out var i) && i >= 0 && i < cur.GetArrayLength()) cur = cur[i];
                else return null;
            }
            return cur.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                JsonValueKind.String => cur.GetString(),
                _ => cur.GetRawText()
            };
        }
    }

    private static void Write(Utf8JsonWriter w, Value v, int depth)
    {
        if (depth > 64) throw new RuntimeException("Json: nesting is too deep");
        switch (v.Type)
        {
            case SabakaType.Null: w.WriteNullValue(); break;
            case SabakaType.Int: w.WriteNumberValue(v.Int); break;
            case SabakaType.Float: w.WriteNumberValue(v.Float); break;
            case SabakaType.Bool: w.WriteBooleanValue(v.Bool); break;
            case SabakaType.String: w.WriteStringValue(v.String); break;
            case SabakaType.Char: w.WriteStringValue(v.Char.ToString()); break;
            case SabakaType.Enum: w.WriteStringValue(v.Enum!.Name); break;
            case SabakaType.Array:
                w.WriteStartArray();
                foreach (var x in v.Array!) Write(w, x, depth + 1);
                w.WriteEndArray();
                break;
            case SabakaType.Object:
            {
                var o = v.Object!;
                if (o.Native is not null && o.Fields.Count == 0)
                    throw new RuntimeException($"Json: cannot serialize native object '{o.ShortName}'");
                w.WriteStartObject();
                foreach (var (k, f) in o.Fields) { w.WritePropertyName(k); Write(w, f, depth + 1); }
                w.WriteEndObject();
                break;
            }
            default: w.WriteNullValue(); break;
        }
    }
}
