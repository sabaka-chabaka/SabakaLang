namespace SabakaLang.Studio.Services;

public sealed class FileService : IDisposable
{
    private const int MaxRecent = 20;

    private string?            _projectRoot;
    private FileSystemWatcher? _watcher;
    private readonly List<string> _recentFiles = [];

    public event Action<string>?  FileExternallyChanged;
    public event Action<string>?  FileExternallyDeleted;

    public string? ProjectRoot => _projectRoot;

    public void OpenProject(string folder)
    {
        _projectRoot = folder;
        SetupWatcher(folder);
    }
    
    public async Task<string> ReadAsync(string path)
    {
        var text = await File.ReadAllTextAsync(path);
        AddRecent(path);
        return text;
    }

    public async Task WriteAsync(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(path, content);
        AddRecent(path);
    }

    public FileNode? GetTree(string root)
    {
        if (!Directory.Exists(root)) return null;
        return BuildNode(root, depth: 0);
    }

    private static FileNode BuildNode(string path, int depth)
    {
        var node = new FileNode
        {
            Name      = Path.GetFileName(path),
            FullPath  = path,
            IsFolder  = true,
            IsExpanded = depth == 0
        };

        try
        {
            foreach (var dir in Directory.GetDirectories(path)
                .Where(d => !IsIgnored(Path.GetFileName(d)))
                .OrderBy(d => d))
            {
                node.Children.Add(BuildNode(dir, depth + 1));
            }

            foreach (var file in Directory.GetFiles(path)
                .OrderBy(f => f))
            {
                node.Children.Add(new FileNode
                {
                    Name     = Path.GetFileName(file),
                    FullPath = file,
                    IsFolder = false
                });
            }
        }
        catch { /* access denied */ }

        return node;
    }

    private static bool IsIgnored(string name) =>
        name is ".git" or "bin" or "obj" or "node_modules" or ".vs" or "__pycache__";

    public IReadOnlyList<string> RecentFiles => _recentFiles;

    private void AddRecent(string path)
    {
        _recentFiles.Remove(path);
        _recentFiles.Insert(0, path);
        if (_recentFiles.Count > MaxRecent)
            _recentFiles.RemoveAt(_recentFiles.Count - 1);
    }

    private void SetupWatcher(string folder)
    {
        _watcher?.Dispose();
        _watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };
        _watcher.Changed += (_, e) => FileExternallyChanged?.Invoke(e.FullPath);
        _watcher.Deleted += (_, e) => FileExternallyDeleted?.Invoke(e.FullPath);
    }

    public void Dispose() => _watcher?.Dispose();
}

public sealed class FileNode
{
    public string Name       { get; set; } = "";
    public string FullPath   { get; set; } = "";
    public bool   IsFolder   { get; set; }
    public bool   IsExpanded { get; set; }
    public List<FileNode> Children { get; } = [];

    public string Icon => IsFolder
        ? (IsExpanded ? "▾ " : "▸ ")
        : ExtensionIcon(Path.GetExtension(Name));

    private static string ExtensionIcon(string ext) => ext switch
    {
        ".sabaka" => "⚡",
        ".json"   => "{}",
        ".md"     => "📝",
        ".txt"    => "📄",
        _         => "📄"
    };
}