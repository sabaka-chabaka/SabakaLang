namespace SabakaLang.Engine;

/// <summary>A script module found by an <see cref="IModuleSource"/>.</summary>
/// <param name="Id">Unique identity (full path); used to detect cycles and to resolve nested relative imports.</param>
/// <param name="Name">Short module name (file name without extension).</param>
public sealed record ModuleText(string Id, string Name, string Source);

/// <summary>Finds the source of <c>import "path"</c> when the path is not a native module.</summary>
public interface IModuleSource
{
    ModuleText? Load(string importPath, string? importerId);
}

/// <summary>Resolves imports relative to the importing file (or to <see cref="BaseDirectory"/>), adding ".sabaka" when needed.</summary>
public sealed class FileModuleSource(string? baseDirectory = null) : IModuleSource
{
    public string BaseDirectory { get; } = baseDirectory ?? Directory.GetCurrentDirectory();

    public ModuleText? Load(string importPath, string? importerId)
    {
        var dir = importerId is not null && File.Exists(importerId) ? Path.GetDirectoryName(Path.GetFullPath(importerId))! : BaseDirectory;
        foreach (var candidate in Candidates(importPath))
        {
            var full = Path.GetFullPath(Path.Combine(dir, candidate));
            if (!File.Exists(full)) continue;
            return new ModuleText(full, Path.GetFileNameWithoutExtension(full), File.ReadAllText(full));
        }
        return null;
    }

    private static IEnumerable<string> Candidates(string path)
    {
        yield return path;
        if (!path.EndsWith(".sabaka", StringComparison.OrdinalIgnoreCase)) yield return path + ".sabaka";
    }
}

/// <summary>In-memory modules, handy for tests and embedding: key = import path.</summary>
public sealed class DictionaryModuleSource(IReadOnlyDictionary<string, string> files) : IModuleSource
{
    public ModuleText? Load(string importPath, string? importerId)
    {
        var key = files.ContainsKey(importPath) ? importPath
            : files.ContainsKey(importPath + ".sabaka") ? importPath + ".sabaka" : null;
        return key is null ? null : new ModuleText(key, Path.GetFileNameWithoutExtension(key), files[key]);
    }
}
