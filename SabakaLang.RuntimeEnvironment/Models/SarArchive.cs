namespace SabakaLang.RuntimeEnvironment.Models;

public sealed class SarArchive
{
    public SarManifest Manifest { get; }
    public IReadOnlyDictionary<string, byte[]> Files { get; }

    public SarArchive(SarManifest manifest, Dictionary<string, byte[]> files)
    {
        Manifest = manifest;
        Files = files;
    }

    public byte[] GetEntryBytes()
    {
        return Files[Manifest.Entry];
    }

    public byte[] GetFile(string path)
    {
        return Files[path];
    }
}