using System.Text.Json;
using System.Text.Json.Serialization;

namespace SabakaLang.RuntimeEnvironment.Models;

public sealed class SarManifest
{
    [JsonPropertyName("name")]
    public string Name { get; init; }

    [JsonPropertyName("version")]
    public string Version { get; init; }

    [JsonPropertyName("entry")]
    public string Entry { get; init; }

    [JsonPropertyName("sre_target")]
    public string SreTarget { get; init; }

    /// <summary>
    /// DLL-зависимости: путь внутри архива + alias (как в import "X.dll" as alias).
    /// </summary>
    [JsonPropertyName("dlls")]
    public List<SarDllEntry> Dlls { get; init; } = new();
}

public sealed class SarDllEntry
{
    /// <summary>Путь внутри .sar архива, например "dlls/SabakaUI.dll"</summary>
    [JsonPropertyName("path")]
    public string Path { get; init; }

    /// <summary>Alias из import "X.dll" as alias. Null = не namespaced.</summary>
    [JsonPropertyName("alias")]
    public string? Alias { get; init; }
}

/// <summary>
/// Входной manifest.json из srcDir — dlls это просто List&lt;string&gt; имён файлов.
/// SarPacker читает этот формат, компилирует, и создаёт SarManifest с SarDllEntry.
/// </summary>
public sealed class InputManifest
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    [JsonPropertyName("entry")]
    public string Entry { get; init; } = "";

    [JsonPropertyName("sre_target")]
    public string SreTarget { get; init; } = "";

    /// <summary>
    /// DLL-зависимости с alias. Два формата поддерживаются:
    ///   "dlls": ["SabakaUI.dll"]                       — старый, alias = null
    ///   "dlls": [{"file":"SabakaUI.dll","alias":"ui"}] — новый, с alias
    /// </summary>
    [JsonPropertyName("dlls")]
    [System.Text.Json.Serialization.JsonConverter(typeof(InputDllListConverter))]
    public List<InputDllEntry> Dlls { get; init; } = new();
}

/// <summary>
/// Запись DLL во входном manifest.json с явным alias.
/// Формат: { "file": "SabakaUI.dll", "alias": "ui" }
/// </summary>
public sealed class InputDllEntry
{
    [JsonPropertyName("file")]
    public string File { get; init; } = "";

    [JsonPropertyName("alias")]
    public string? Alias { get; init; }
}

/// <summary>
/// Конвертер который понимает оба формата dlls:
///   ["SabakaUI.dll"]                        -> InputDllEntry { File="SabakaUI.dll", Alias=null }
///   [{"file":"SabakaUI.dll","alias":"ui"}]  -> InputDllEntry { File="SabakaUI.dll", Alias="ui" }
/// </summary>
public sealed class InputDllListConverter : System.Text.Json.Serialization.JsonConverter<List<InputDllEntry>>
{
    public override List<InputDllEntry> Read(ref Utf8JsonReader reader,
        Type typeToConvert, JsonSerializerOptions options)
    {
        var list = new List<InputDllEntry>();
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected array");

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                // Старый формат: просто строка
                list.Add(new InputDllEntry { File = reader.GetString()! });
            }
            else if (reader.TokenType == JsonTokenType.StartObject)
            {
                // Новый формат: объект с file + alias
                string file = "";
                string? alias = null;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    string prop = reader.GetString()!;
                    reader.Read();
                    if (prop == "file")  file  = reader.GetString()!;
                    if (prop == "alias") alias = reader.GetString();
                }
                list.Add(new InputDllEntry { File = file, Alias = alias });
            }
        }
        return list;
    }

    public override void Write(Utf8JsonWriter writer,
        List<InputDllEntry> value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, options);
}