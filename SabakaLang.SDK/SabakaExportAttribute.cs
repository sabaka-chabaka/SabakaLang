namespace SabakaLang.SDK;

/// <summary>
/// Marks a method, property, or field as exported to SabakaLang scripts.
/// The exported name is case-insensitive in scripts.
///
/// Supported return/parameter types:
///   int, double/float, bool, string, void
///   (Lists and Dicts are not yet supported — use JSON strings instead)
/// </summary>
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field,
    Inherited = false,
    AllowMultiple = false)]
public sealed class SabakaExportAttribute : Attribute
{
    /// <summary>The name exposed to SabakaLang scripts.</summary>
    public string Name { get; }

    public SabakaExportAttribute(string name)
    {
        Name = name;
    }
}