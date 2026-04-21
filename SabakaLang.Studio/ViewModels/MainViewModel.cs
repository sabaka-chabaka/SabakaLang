using System.ComponentModel;
using System.Runtime.CompilerServices;
using SabakaLang.LanguageServer;
using SabakaLang.Studio.Editor.Controls;
using SabakaLang.Studio.Explorer;
using SabakaLang.Studio.Git;
using SabakaLang.Studio.Panels;
using SabakaLang.Studio.Search;
using SabakaLang.Studio.Services;

namespace SabakaLang.Studio.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    public readonly DocumentStore   DocumentStore;
    public readonly FileService     FileService;
    public readonly GitService      GitService;
    
    public readonly EditorTabBar     TabBar;
    public readonly ProjectExplorer  Explorer;
    public readonly GitPanel         GitPanel;
    public readonly FindReplacePanel FindReplace;
    public readonly CommandPalette   Palette;
    public readonly BottomPanelHost  BottomPanel;
    
    private readonly Dictionary<string, EditorView> _editors = new();
    private EditorView? _activeEditor;

    public EditorView? ActiveEditor
    {
        get => _activeEditor;
        private set { _activeEditor = value; OnPropertyChanged(); }
    }

    private string _statusLine = "Ln 1, Col 1";
    private string _statusBranch = "";
    private int    _errorCount;
    private int    _warnCount;

    public string StatusLine   { get => _statusLine;   private set { _statusLine   = value; OnPropertyChanged(); } }
    public string StatusBranch { get => _statusBranch; private set { _statusBranch = value; OnPropertyChanged(); } }
    public int    ErrorCount   { get => _errorCount;   private set { _errorCount   = value; OnPropertyChanged(); } }
    public int    WarnCount    { get => _warnCount;    private set { _warnCount    = value; OnPropertyChanged(); } }

    public MainViewModel()
    {
        DocumentStore = new DocumentStore();
        FileService   = new FileService();
        GitService    = new GitService();

        TabBar     = new EditorTabBar();
        Explorer   = new ProjectExplorer(FileService, GitService);
        GitPanel   = new GitPanel(GitService);
        FindReplace= new FindReplacePanel();
        Palette    = new CommandPalette();
        BottomPanel= new BottomPanelHost();

        TabBar.TabSelected   += OnTabSelected;
        TabBar.TabClosed     += OnTabClosed;
        TabBar.NewTabRequested+= NewUntitled;

        Explorer.FileOpened += path => _ = OpenFileAsync(path);

        GitService.StateChanged += () =>
            MainThread.BeginInvokeOnMainThread(() =>
                StatusBranch = GitService.CurrentBranch is not null
                    ? $"⎇  {GitService.CurrentBranch}" : "");

        RegisterPaletteCommands();
    }

    public async Task OpenFileAsync(string path)
    {
        if (_editors.TryGetValue(path, out var existing))
        {
            TabBar.ActivateByPath(path);
            ActiveEditor = existing;
            return;
        }

        string source;
        try   { source = await FileService.ReadAsync(path); }
        catch { source = ""; }

        var editor = CreateEditor(path);
        editor.Text = source;
        editor.MarkClean();

        _editors[path] = editor;

        var title = Path.GetFileName(path);
        TabBar.AddTab(path, title);
        ActiveEditor = editor;

        _ = LoadGitMarkersAsync(path, editor);
    }

    public async Task SaveActiveAsync()
    {
        if (ActiveEditor is null || TabBar.ActiveTab is null) return;
        var path = TabBar.ActiveTab.FilePath;
        await FileService.WriteAsync(path, ActiveEditor.Text);
        ActiveEditor.MarkClean();
        TabBar.SetDirty(path, false);
        BottomPanel.Output.AppendSuccess($"Saved: {path}");
    }

    public void NewUntitled()
    {
        var name  = $"untitled-{_editors.Count + 1}.sabaka";
        var path  = $"untitled://{name}";
        var editor = CreateEditor(path);
        _editors[path] = editor;
        TabBar.AddTab(path, name);
        ActiveEditor = editor;
    }

    public async Task OpenFolderAsync(string folderPath)
    {
        Explorer.OpenFolder(folderPath);
        await Explorer.RebuildTreeAsync();
        GitService.OpenRepo(folderPath);
    }

    private EditorView CreateEditor(string path)
    {
        var editor = new EditorView(DocumentStore) { FileUri = path };

        editor.FileDirty += fp => TabBar.SetDirty(fp, true);

        editor.StatusUpdated += pos =>
        {
            if (editor == ActiveEditor)
                StatusLine = $"Ln {pos.Line + 1}, Col {pos.Column + 1}";
        };

        editor.DiagnosticsUpdated += diags =>
        {
            if (editor == ActiveEditor)
            {
                BottomPanel.Diagnostics.Update(diags);
                ErrorCount = diags.Count(d => d.IsError);
                WarnCount  = diags.Count(d => !d.IsError);
            }
        };

        editor.FindRequested     += () => FindReplace.Open(editor);
        editor.FindAllRequested  += () => Palette.Open();
        editor.SaveRequested     += () => _ = SaveActiveAsync();

        BottomPanel.Diagnostics.DiagnosticSelected += d =>
        {
            editor.ScrollToLine(d.Line);
        };

        return editor;
    }
    
    private void OnTabSelected(EditorTab tab)
    {
        if (_editors.TryGetValue(tab.FilePath, out var editor))
            ActiveEditor = editor;
    }

    private void OnTabClosed(EditorTab tab)
    {
        if (_editors.Remove(tab.FilePath, out var editor))
        {
            if (ActiveEditor == editor)
                ActiveEditor = _editors.Values.LastOrDefault();
        }
    }

    private async Task LoadGitMarkersAsync(string path, EditorView editor)
    {
        if (!GitService.IsRepo) return;
    }

    private void RegisterPaletteCommands()
    {
        Palette.Register(new[]
        {
            new PaletteCommand { Title = "New File",        Icon = "📄", Shortcut = "Ctrl+N",       Execute = NewUntitled, Category = "File" },
            new PaletteCommand { Title = "Save",            Icon = "💾", Shortcut = "Ctrl+S",       Execute = () => _ = SaveActiveAsync(), Category = "File" },
            new PaletteCommand { Title = "Open Folder",     Icon = "📁",                            Execute = () => _ = PromptOpenFolder(), Category = "File" },
            new PaletteCommand { Title = "Find in File",    Icon = "🔍", Shortcut = "Ctrl+F",       Execute = () => { if(ActiveEditor is not null) FindReplace.Open(ActiveEditor); }, Category = "Edit" },
            new PaletteCommand { Title = "Toggle Comment",  Icon = "//", Shortcut = "Ctrl+/",       Execute = () => ActiveEditor?.OnKeyDown("/", ctrl: true, shift: false, alt: false), Category = "Edit" },
            new PaletteCommand { Title = "Duplicate Line",  Icon = "⧉",  Shortcut = "Ctrl+D",       Execute = () => ActiveEditor?.OnKeyDown("D", ctrl: true, shift: false, alt: false), Category = "Edit" },
            new PaletteCommand { Title = "Undo",            Icon = "↩", Shortcut = "Ctrl+Z",       Execute = () => ActiveEditor?.OnKeyDown("Z", ctrl: true, shift: false, alt: false), Category = "Edit" },
            new PaletteCommand { Title = "Redo",            Icon = "↪", Shortcut = "Ctrl+Y",       Execute = () => ActiveEditor?.OnKeyDown("Y", ctrl: true, shift: false, alt: false), Category = "Edit" },
            new PaletteCommand { Title = "Git: Stage All",  Icon = "+",  Execute = async () => await GitService.StageAllAsync(), Category = "Git" },
            new PaletteCommand { Title = "Git: Pull",       Icon = "↓",  Execute = async () => await GitService.PullAsync(), Category = "Git" },
            new PaletteCommand { Title = "Git: Push",       Icon = "↑",  Execute = async () => await GitService.PushAsync(), Category = "Git" },
            new PaletteCommand { Title = "Git: Fetch",      Icon = "⟳",  Execute = async () => await GitService.FetchAsync(), Category = "Git" },
        });
    }

    private async Task PromptOpenFolder()
    {
        var result = await FilePicker.PickAsync();
        if (result is not null)
            await OpenFolderAsync(Path.GetDirectoryName(result.FullPath)!);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        FileService.Dispose();
        GitService.Dispose();
    }
}