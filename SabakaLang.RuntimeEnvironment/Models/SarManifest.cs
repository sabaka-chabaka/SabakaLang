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
}