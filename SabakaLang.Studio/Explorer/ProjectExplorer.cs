using SabakaLang.Studio.Editor.Highlighting;
using SabakaLang.Studio.Git;
using SabakaLang.Studio.Services;

namespace SabakaLang.Studio.Explorer;

public sealed class ProjectExplorer : ContentView
{
    private readonly FileService _fs;
    private readonly GitService  _git;
    private readonly StackLayout _tree;
    private readonly Label       _rootLabel;
    private Dictionary<string, GitFileStatus> _gitStatus = new();

    public event Action<string>? FileOpened;

    public ProjectExplorer(FileService fs, GitService git)
    {
        _fs  = fs;
        _git = git;
        _git.StateChanged += () => MainThread.BeginInvokeOnMainThread(() => _ = RefreshGitStatusAsync());

        BackgroundColor = StudioTheme.ExplorerBg;

        _rootLabel = new Label
        {
            FontSize  = 11,
            TextColor = Colors.Gray,
            Padding   = new Thickness(12, 8),
            Text      = "EXPLORER"
        };

        var openFolderBtn = new Button
        {
            Text            = "Open Folder…",
            FontSize        = 12,
            BackgroundColor = Colors.Transparent,
            TextColor       = StudioTheme.ExplorerText,
            BorderColor     = StudioTheme.Border,
            BorderWidth     = 1,
            CornerRadius    = 4,
            Margin          = new Thickness(8, 4),
            HeightRequest   = 28
        };
        openFolderBtn.Clicked += async (_, _) => await PickFolderAsync();

        _tree = new StackLayout { Spacing = 0 };

        var scroll = new ScrollView { Content = _tree };

        Content = new StackLayout
        {
            Spacing  = 0,
            Children = { _rootLabel, openFolderBtn, scroll }
        };
    }

    public void OpenFolder(string path)
    {
        _fs.OpenProject(path);
        _git.OpenRepo(path);
        _rootLabel.Text = Path.GetFileName(path).ToUpperInvariant();
        _ = RebuildTreeAsync();
    }

    public async Task RebuildTreeAsync()
    {
        await RefreshGitStatusAsync();
        var root = _fs.ProjectRoot;
        if (root is null) return;
        var node = _fs.GetTree(root);
        if (node is null) return;

        _tree.Children.Clear();
        foreach (var child in node.Children)
            _tree.Children.Add(BuildNodeView(child, depth: 0));
    }

    private async Task RefreshGitStatusAsync()
    {
        var entries = await _git.GetStatusAsync();
        _gitStatus  = entries.ToDictionary(e => e.FilePath, e => e.Status);
        await RebuildTreeAsync();
    }

    private View BuildNodeView(FileNode node, int depth)
    {
        var gitColor = GetGitColor(node.FullPath, node.IsFolder);

        var icon = new Label
        {
            Text     = node.Icon,
            FontSize = 12,
            TextColor= StudioTheme.ExplorerIcon,
            VerticalOptions = LayoutOptions.Center
        };

        var name = new Label
        {
            Text     = node.Name,
            FontSize = 13,
            FontFamily = StudioTheme.FontFamily,
            TextColor= gitColor ?? StudioTheme.ExplorerText,
            VerticalOptions = LayoutOptions.Center
        };

        var row = new HorizontalStackLayout
        {
            Padding = new Thickness(depth * 16 + 8, 2),
            Spacing = 6,
            HeightRequest = 24,
            Children = { icon, name }
        };

        var longPress = new TapGestureRecognizer { NumberOfTapsRequired = 1 };
        longPress.Tapped += async (_, _) =>
        {
            if (node.IsFolder)
            {
                node.IsExpanded = !node.IsExpanded;
                icon.Text = node.Icon;
                await RebuildTreeAsync();
            }
            else
            {
                FileOpened?.Invoke(node.FullPath);
            }
        };
        row.GestureRecognizers.Add(longPress);

        var cell = new Grid { Children = { row }, HeightRequest = 24 };
        AddContextMenu(cell, node);

        if (!node.IsFolder || !node.IsExpanded)
            return cell;

        var childStack = new StackLayout { Spacing = 0 };
        foreach (var child in node.Children)
            childStack.Children.Add(BuildNodeView(child, depth + 1));

        return new StackLayout { Spacing = 0, Children = { cell, childStack } };
    }

    private Microsoft.Maui.Graphics.Color? GetGitColor(string path, bool isFolder)
    {
        if (!_git.IsRepo) return null;
        if (isFolder)
        {
            var rel = RelPath(path);
            if (_gitStatus.Any(kv => kv.Key.StartsWith(rel, StringComparison.OrdinalIgnoreCase)))
                return StudioTheme.GitModified;
            return null;
        }

        var relFile = RelPath(path);
        if (!_gitStatus.TryGetValue(relFile, out var status)) return null;

        return status switch
        {
            GitFileStatus.Added    => StudioTheme.GitAdded,
            GitFileStatus.Modified => StudioTheme.GitModified,
            GitFileStatus.Deleted  => StudioTheme.GitDeleted,
            GitFileStatus.Untracked=> Colors.Gray,
            _                      => null
        };
    }

    private string RelPath(string full)
    {
        var root = _fs.ProjectRoot ?? "";
        return full.StartsWith(root) ? full[root.Length..].TrimStart('/', '\\') : full;
    }
    
    private void AddContextMenu(View view, FileNode node)
    {
        var menu = new MenuFlyout();

        if (node.IsFolder)
        {
            menu.Add(new MenuFlyoutItem { Text = "New File",   Command = new Command(async () => await NewFileAsync(node)) });
            menu.Add(new MenuFlyoutItem { Text = "New Folder", Command = new Command(async () => await NewFolderAsync(node)) });
            menu.Add(new MenuFlyoutSeparator());
        }

        menu.Add(new MenuFlyoutItem { Text = "Rename",        Command = new Command(async () => await RenameAsync(node)) });
        menu.Add(new MenuFlyoutItem { Text = "Delete",        Command = new Command(async () => await DeleteAsync(node)) });

        if (!node.IsFolder)
        {
            menu.Add(new MenuFlyoutSeparator());
            menu.Add(new MenuFlyoutItem { Text = "Copy Path",  Command = new Command(() => Clipboard.SetTextAsync(node.FullPath)) });
        }

        FlyoutBase.SetContextFlyout(view, menu);
    }

    private async Task NewFileAsync(FileNode parent)
    {
        var name = await Prompt("New File", "Enter file name:", "untitled.sabaka");
        if (string.IsNullOrWhiteSpace(name)) return;
        var path = Path.Combine(parent.FullPath, name);
        await _fs.WriteAsync(path, "");
        FileOpened?.Invoke(path);
        await RebuildTreeAsync();
    }

    private async Task NewFolderAsync(FileNode parent)
    {
        var name = await Prompt("New Folder", "Enter folder name:", "NewFolder");
        if (string.IsNullOrWhiteSpace(name)) return;
        Directory.CreateDirectory(Path.Combine(parent.FullPath, name));
        await RebuildTreeAsync();
    }

    private async Task RenameAsync(FileNode node)
    {
        var newName = await Prompt("Rename", "New name:", node.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == node.Name) return;
        var dest = Path.Combine(Path.GetDirectoryName(node.FullPath)!, newName);
        if (node.IsFolder) Directory.Move(node.FullPath, dest);
        else               File.Move(node.FullPath, dest);
        await RebuildTreeAsync();
    }

    private async Task DeleteAsync(FileNode node)
    {
        var ok = await Application.Current!.MainPage!
            .DisplayAlert("Delete", $"Delete {node.Name}?", "Delete", "Cancel");
        if (!ok) return;

        if (node.IsFolder) Directory.Delete(node.FullPath, recursive: true);
        else               File.Delete(node.FullPath);
        await RebuildTreeAsync();
    }

    private async Task PickFolderAsync()
    {
        var result = await FilePicker.PickAsync(new PickOptions { PickerTitle = "Select folder" });
        if (result is not null)
            OpenFolder(Path.GetDirectoryName(result.FullPath)!);
    }

    private static async Task<string?> Prompt(string title, string message, string defaultValue)
        => await Application.Current!.MainPage!.DisplayPromptAsync(title, message, initialValue: defaultValue);
}