using SabakaLang.RuntimeEnvironment.Models;

namespace SabakaLang.RuntimeEnvironment;

using System.IO.Compression;
using System.Text.Json;

public sealed class SarLoader
{
    private const string CurrentSreVersion = "1.0";
    private const string ManifestFileName = "manifest.json";

    public SarArchive Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Invalid path");

        if (!File.Exists(path))
            throw new FileNotFoundException(path);

        if (!path.EndsWith(".sar", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Not a SAR file");

        using var zip = ZipFile.OpenRead(path);

        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in zip.Entries)
        {
            ValidateEntry(entry);

            using var stream = entry.Open();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);

            files[NormalizePath(entry.FullName)] = ms.ToArray();
        }

        if (!files.TryGetValue(ManifestFileName, out var manifestBytes))
            throw new InvalidOperationException("manifest.json missing");

        var manifest = ParseManifest(manifestBytes);

        ValidateManifest(manifest, files);

        return new SarArchive(manifest, files);
    }

    private static void ValidateEntry(ZipArchiveEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.FullName))
            throw new InvalidOperationException("Invalid entry name");

        if (entry.FullName.Contains(".."))
            throw new InvalidOperationException("Path traversal detected");

        if (Path.IsPathRooted(entry.FullName))
            throw new InvalidOperationException("Absolute paths not allowed");
    }

    private static string NormalizePath(string path)
    {
        return path.Replace("\\", "/");
    }

    private static SarManifest ParseManifest(byte[] json)
    {
        var manifest = JsonSerializer.Deserialize<SarManifest>(json);

        if (manifest == null)
            throw new InvalidOperationException("Invalid manifest.json");

        return manifest;
    }

    private static void ValidateManifest(SarManifest manifest, Dictionary<string, byte[]> files)
    {
        if (string.IsNullOrWhiteSpace(manifest.Name))
            throw new InvalidOperationException("Manifest: name missing");

        if (string.IsNullOrWhiteSpace(manifest.Version))
            throw new InvalidOperationException("Manifest: version missing");

        if (string.IsNullOrWhiteSpace(manifest.Entry))
            throw new InvalidOperationException("Manifest: entry missing");

        if (string.IsNullOrWhiteSpace(manifest.SreTarget))
            throw new InvalidOperationException("Manifest: sre_target missing");

        if (manifest.SreTarget != CurrentSreVersion)
            throw new InvalidOperationException(
                $"SRE version mismatch. Required: {manifest.SreTarget}, Current: {CurrentSreVersion}");

        var entryPath = manifest.Entry.Replace("\\", "/");

        if (!files.ContainsKey(entryPath))
            throw new InvalidOperationException($"Entry file '{entryPath}' not found");

        if (!entryPath.EndsWith(".sabakac", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Entry must be .sabakac file");
    }
}