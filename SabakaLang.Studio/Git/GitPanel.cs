using SabakaLang.Studio.Editor.Highlighting;

namespace SabakaLang.Studio.Git;

public sealed class GitPanel : ContentView
{
    private readonly GitService  _git;
    private readonly Label       _branchLabel;
    private readonly Label       _syncLabel;
    private readonly Entry       _commitEntry;
    private readonly Button      _commitBtn;
    private readonly Button      _stageAllBtn;
    private readonly Button      _pushBtn;
    private readonly Button      _pullBtn;
    private readonly Button      _fetchBtn;
    private readonly ListView    _changesView;
    private List<GitFileEntry>   _changes = [];

    public event Action<GitFileEntry>? FileSelected;

    public GitPanel(GitService git)
    {
        _git = git;
        _git.StateChanged += () => MainThread.BeginInvokeOnMainThread(() => _ = RefreshAsync());

        BackgroundColor = StudioTheme.ExplorerBg;

        _branchLabel = new Label
        {
            FontSize  = 13,
            FontFamily= StudioTheme.FontFamily,
            TextColor = StudioTheme.GitAdded,
            Padding   = new Thickness(12, 6)
        };

        _syncLabel = new Label
        {
            FontSize  = 11,
            TextColor = Colors.Gray,
            Padding   = new Thickness(12, 0)
        };

        _fetchBtn  = MakeBtn("⟳ Fetch",  Colors.Gray,   async () => await git.FetchAsync());
        _pullBtn   = MakeBtn("↓ Pull",   StudioTheme.GitModified, async () => { await git.PullAsync(); await RefreshAsync(); });
        _pushBtn   = MakeBtn("↑ Push",   StudioTheme.GitAdded,    async () => { await git.PushAsync(); await RefreshAsync(); });

        var actionRow = new HorizontalStackLayout
        {
            Spacing  = 6,
            Padding  = new Thickness(8, 4),
            Children = { _fetchBtn, _pullBtn, _pushBtn }
        };

        _stageAllBtn = MakeBtn("+ Stage All", StudioTheme.GitAdded,
            async () => { await git.StageAllAsync(); await RefreshAsync(); });

        _commitEntry = new Entry
        {
            Placeholder          = "Commit message…",
            PlaceholderColor     = Colors.Gray,
            FontFamily           = StudioTheme.FontFamily,
            FontSize             = 13,
            TextColor            = StudioTheme.ExplorerText,
            BackgroundColor      = StudioTheme.Surface,
            Margin               = new Thickness(8, 4)
        };

        _commitBtn = MakeBtn("✔ Commit", StudioTheme.GitAdded, async () =>
        {
            if (string.IsNullOrWhiteSpace(_commitEntry.Text)) return;
            await git.CommitAsync(_commitEntry.Text);
            _commitEntry.Text = "";
            await RefreshAsync();
        });

        var commitRow = new HorizontalStackLayout
        {
            Spacing  = 6,
            Padding  = new Thickness(8, 4),
            Children = { _stageAllBtn, _commitBtn }
        };

        var changesHeader = new Label
        {
            Text      = "CHANGES",
            FontSize  = 11,
            TextColor = Colors.Gray,
            Padding   = new Thickness(12, 8, 0, 4)
        };

        _changesView = new ListView
        {
            BackgroundColor = Colors.Transparent,
            SeparatorVisibility = SeparatorVisibility.None,
            ItemTemplate = new DataTemplate(BuildFileCell)
        };
        _changesView.ItemSelected += (_, e) =>
        {
            if (e.SelectedItem is GitFileEntry entry)
                FileSelected?.Invoke(entry);
        };

        var ghBtn = MakeBtn("🔗 Connect GitHub", Colors.DimGray, ShowGitHubLogin);

        Content = new StackLayout
        {
            Spacing = 0,
            Children =
            {
                _branchLabel,
                _syncLabel,
                actionRow,
                changesHeader,
                _changesView,
                _commitEntry,
                commitRow,
                ghBtn
            }
        };

        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (!_git.IsRepo)
        {
            _branchLabel.Text = "⚠ Not a git repository";
            return;
        }

        _branchLabel.Text = $"⎇  {_git.CurrentBranch ?? "detached"}";

        var branches = await _git.GetBranchesAsync();
        var current  = branches.FirstOrDefault(b => b.IsCurrent);
        if (current is not null)
            _syncLabel.Text = current.AheadBy > 0 || current.BehindBy > 0
                ? $"↑{current.AheadBy} ↓{current.BehindBy}  {current.TrackingBranch}"
                : current.TrackingBranch ?? "";

        _changes = await _git.GetStatusAsync();
        _changesView.ItemsSource = _changes;
    }

    private static Cell BuildFileCell()
    {
        var statusLabel = new Label
        {
            FontSize = 12, WidthRequest = 16,
            VerticalOptions = LayoutOptions.Center
        };
        var fileLabel = new Label
        {
            FontFamily = StudioTheme.FontFamily,
            FontSize   = 12,
            TextColor  = StudioTheme.ExplorerText,
            VerticalOptions = LayoutOptions.Center
        };
        var stageBtn = new Label
        {
            Text = "+", FontSize = 14, TextColor = Colors.Gray,
            VerticalOptions = LayoutOptions.Center,
            Padding = new Thickness(8, 0)
        };
        var discardBtn = new Label
        {
            Text = "↺", FontSize = 13, TextColor = Colors.DimGray,
            VerticalOptions = LayoutOptions.Center,
            Padding = new Thickness(4, 0)
        };

        var row = new HorizontalStackLayout
        {
            Padding = new Thickness(8, 2),
            Spacing = 6,
            Children = { statusLabel, fileLabel, stageBtn, discardBtn }
        };

        var cell = new ViewCell { View = row };

        cell.BindingContextChanged += (_, _) =>
        {
            if (cell.BindingContext is not GitFileEntry e) return;
            statusLabel.Text = e.StatusChar;
            statusLabel.TextColor = e.Status switch
            {
                GitFileStatus.Added     => StudioTheme.GitAdded,
                GitFileStatus.Modified  => StudioTheme.GitModified,
                GitFileStatus.Deleted   => StudioTheme.GitDeleted,
                GitFileStatus.Untracked => Colors.Gray,
                _                       => Colors.Gray
            };
            fileLabel.Text = Path.GetFileName(e.FilePath);
        };

        return cell;
    }

    private async void ShowGitHubLogin()
    {
        if (_git.IsGitHubAuthenticated)
        {
            await Application.Current!.MainPage!.DisplayAlert("GitHub", "Already connected!", "OK");
            return;
        }

        await _git.GitHubLoginAsync(
            clientId: "Ov23liuKHoVSQ0cf626y",
            showCodeToUser: (code, uri) =>
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await Application.Current!.MainPage!.DisplayAlert(
                        "GitHub Login",
                        $"Go to: {uri}\nEnter code: {code}",
                        "OK");
                });
            });
    }


    private static Button MakeBtn(string text, Color color, Action action) => new()
    {
        Text            = text,
        FontSize        = 12,
        BackgroundColor = Colors.Transparent,
        TextColor       = color,
        BorderColor     = color,
        BorderWidth     = 1,
        CornerRadius    = 4,
        HeightRequest   = 28,
        Padding         = new Thickness(8, 0),
        Command         = new Command(action)
    };
}