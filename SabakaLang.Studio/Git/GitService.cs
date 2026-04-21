using System.Text.Json;

namespace SabakaLang.Studio.Git;

public enum GitFileStatus { Untracked, Added, Modified, Deleted, Renamed, Ignored, Unchanged }

public sealed class GitFileEntry
{
    public string FilePath { get; init; } = "";
    public GitFileStatus Status { get; init; }
    public string StatusChar => Status switch
    {
        GitFileStatus.Added     => "A",
        GitFileStatus.Modified  => "M",
        GitFileStatus.Deleted   => "D",
        GitFileStatus.Untracked => "?",
        GitFileStatus.Renamed   => "R",
        _ => " "
    };
}

public sealed class GitCommit
{
    public string Sha     { get; init; } = "";
    public string Message { get; init; } = "";
    public string Author  { get; init; } = "";
    public DateTimeOffset When { get; init; }
    public string ShortSha => Sha.Length >= 7 ? Sha[..7] : Sha;
}

public sealed class GitBranch
{
    public string Name     { get; init; } = "";
    public bool   IsLocal  { get; init; }
    public bool   IsCurrent { get; init; }
    public string? TrackingBranch { get; init; }
    public int AheadBy  { get; init; }
    public int BehindBy { get; init; }
}

public sealed class GitDiffHunk
{
    public int    OldStart { get; init; }
    public int    NewStart { get; init; }
    public string Patch    { get; init; } = "";
    public List<GitDiffLine> Lines { get; init; } = [];
}

public sealed class GitDiffLine
{
    public char   Origin  { get; init; } // '+' '-' ' '
    public string Content { get; init; } = "";
    public int?   OldLineNumber { get; init; }
    public int?   NewLineNumber { get; init; }
}

public sealed class GitService : IDisposable
{
    private string? _repoPath;
    private string? _gitHubToken;
    private readonly HttpClient _http = new();

    public bool IsRepo => _repoPath is not null;
    public string? CurrentBranch { get; private set; }
    public string? RemoteUrl { get; private set; }

    public event Action? StateChanged;

    public void OpenRepo(string path)
    {
        _repoPath = FindGitRoot(path);
        if (_repoPath is null) return;
        RefreshState();
    }

    private static string? FindGitRoot(string path)
    {
        var dir = new DirectoryInfo(path);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    public async Task<List<GitFileEntry>> GetStatusAsync()
    {
        if (_repoPath is null) return [];
        var output = await RunGitAsync("status --porcelain=v1");
        var result = new List<GitFileEntry>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 3) continue;
            var x    = line[0];
            var y    = line[1];
            var file = line[3..].Trim().Trim('"');

            var status = (x, y) switch
            {
                ('?', '?') => GitFileStatus.Untracked,
                ('A', _)   => GitFileStatus.Added,
                ('M', _) or (_, 'M') => GitFileStatus.Modified,
                ('D', _) or (_, 'D') => GitFileStatus.Deleted,
                ('R', _)   => GitFileStatus.Renamed,
                _          => GitFileStatus.Modified
            };

            result.Add(new GitFileEntry { FilePath = file, Status = status });
        }

        return result;
    }

    public async Task<List<GitCommit>> GetLogAsync(int limit = 50)
    {
        if (_repoPath is null) return [];
        var sep = "|||";
        var fmt = $"%H{sep}%s{sep}%an{sep}%aI";
        var output = await RunGitAsync($"log --format={fmt} -n {limit}");
        var commits = new List<GitCommit>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(sep);
            if (parts.Length < 4) continue;
            commits.Add(new GitCommit
            {
                Sha     = parts[0].Trim(),
                Message = parts[1].Trim(),
                Author  = parts[2].Trim(),
                When    = DateTimeOffset.Parse(parts[3].Trim())
            });
        }

        return commits;
    }

    public async Task<List<GitBranch>> GetBranchesAsync()
    {
        if (_repoPath is null) return [];
        var output = await RunGitAsync("branch -vv --format=%(refname:short)|%(HEAD)|%(upstream:short)|%(upstream:track)");
        var branches = new List<GitBranch>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('|');
            if (parts.Length < 2) continue;
            var name      = parts[0].Trim();
            var isCurrent = parts.Length > 1 && parts[1].Trim() == "*";
            var upstream  = parts.Length > 2 ? parts[2].Trim() : null;
            var track     = parts.Length > 3 ? parts[3].Trim() : "";

            int ahead = 0, behind = 0;
            if (!string.IsNullOrEmpty(track))
            {
                var ahead_match = System.Text.RegularExpressions.Regex.Match(track, @"ahead (\d+)");
                var behind_match = System.Text.RegularExpressions.Regex.Match(track, @"behind (\d+)");
                if (ahead_match.Success)  int.TryParse(ahead_match.Groups[1].Value, out ahead);
                if (behind_match.Success) int.TryParse(behind_match.Groups[1].Value, out behind);
            }

            branches.Add(new GitBranch
            {
                Name           = name,
                IsLocal        = true,
                IsCurrent      = isCurrent,
                TrackingBranch = string.IsNullOrEmpty(upstream) ? null : upstream,
                AheadBy        = ahead,
                BehindBy       = behind
            });
        }

        return branches;
    }

    public async Task<List<GitDiffHunk>> GetDiffAsync(string filePath, bool staged = false)
    {
        if (_repoPath is null) return [];
        var arg = staged ? "--staged" : "";
        var output = await RunGitAsync($"diff {arg} -- \"{filePath}\"");
        return ParseDiff(output);
    }

    public async Task<Dictionary<int, Editor.Gutter.GutterMarker>> GetLineMarkersAsync(string filePath)
    {
        var hunks  = await GetDiffAsync(filePath);
        var result = new Dictionary<int, Editor.Gutter.GutterMarker>();

        foreach (var hunk in hunks)
        {
            var newLine = hunk.NewStart - 1;
            foreach (var line in hunk.Lines)
            {
                switch (line.Origin)
                {
                    case '+':
                        result[newLine] = Editor.Gutter.GutterMarker.Added;
                        newLine++;
                        break;
                    case '-':
                        result[newLine] = Editor.Gutter.GutterMarker.Deleted;
                        break;
                    case ' ':
                        newLine++;
                        break;
                }
            }
        }

        return result;
    }

    public async Task StageAsync(string filePath)
    {
        await RunGitAsync($"add -- \"{filePath}\"");
        StateChanged?.Invoke();
    }

    public async Task StageAllAsync()
    {
        await RunGitAsync("add -A");
        StateChanged?.Invoke();
    }

    public async Task UnstageAsync(string filePath)
    {
        await RunGitAsync($"restore --staged -- \"{filePath}\"");
        StateChanged?.Invoke();
    }

    public async Task DiscardAsync(string filePath)
    {
        await RunGitAsync($"restore -- \"{filePath}\"");
        StateChanged?.Invoke();
    }

    public async Task<bool> CommitAsync(string message, bool stageAll = false)
    {
        if (_repoPath is null) return false;
        if (stageAll) await StageAllAsync();
        var result = await RunGitAsync($"commit -m \"{EscapeMessage(message)}\"");
        StateChanged?.Invoke();
        return !result.Contains("nothing to commit");
    }

    public async Task<string> PushAsync(string? remote = null, string? branch = null)
    {
        var args = "push";
        if (!string.IsNullOrEmpty(remote)) args += $" {remote}";
        if (!string.IsNullOrEmpty(branch)) args += $" {branch}";
        return await RunGitAsync(args);
    }

    public async Task<string> PullAsync()
        => await RunGitAsync("pull --rebase");

    public async Task<string> FetchAsync()
        => await RunGitAsync("fetch --all --prune");

    public async Task CheckoutAsync(string branch)
    {
        await RunGitAsync($"checkout {branch}");
        await RefreshState();
    }

    public async Task CreateBranchAsync(string name, bool checkout = true)
    {
        await RunGitAsync($"checkout -b {name}");
        await RefreshState();
    }

    public async Task DeleteBranchAsync(string name, bool force = false)
    {
        var flag = force ? "-D" : "-d";
        await RunGitAsync($"branch {flag} {name}");
        await RefreshState();
    }

    public async Task<bool> GitHubLoginAsync(
        string clientId,
        Action<string, string> showCodeToUser,
        CancellationToken ct = default)
    {
        var req = await _http.PostAsync(
            "https://github.com/login/device/code",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["scope"]     = "repo,read:user"
            }), ct);

        var body = await req.Content.ReadAsStringAsync(ct);
        var deviceCode = ExtractParam(body, "device_code");
        var userCode   = ExtractParam(body, "user_code");
        var verifyUri  = ExtractParam(body, "verification_uri");
        var interval   = int.TryParse(ExtractParam(body, "interval"), out var iv) ? iv : 5;

        showCodeToUser(userCode, verifyUri);

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(interval * 1000, ct);
            var poll = await _http.PostAsync(
                "https://github.com/login/oauth/access_token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"]   = clientId,
                    ["device_code"] = deviceCode,
                    ["grant_type"]  = "urn:ietf:params:oauth:grant-type:device_code"
                }), ct);

            var resp = await poll.Content.ReadAsStringAsync(ct);
            var token = ExtractParam(resp, "access_token");
            if (!string.IsNullOrEmpty(token))
            {
                _gitHubToken = token;
                _http.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                return true;
            }
            if (resp.Contains("authorization_pending")) continue;
            break; // slow_down / expired / access_denied
        }
        return false;
    }

    public bool IsGitHubAuthenticated => !string.IsNullOrEmpty(_gitHubToken);

    public async Task<List<GitHubRepo>> GetGitHubReposAsync()
    {
        if (!IsGitHubAuthenticated) return [];
        var resp = await _http.GetStringAsync("https://api.github.com/user/repos?per_page=100&sort=pushed");
        using var doc = JsonDocument.Parse(resp);
        var result = new List<GitHubRepo>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            result.Add(new GitHubRepo
            {
                FullName    = el.GetProperty("full_name").GetString() ?? "",
                CloneUrl    = el.GetProperty("clone_url").GetString() ?? "",
                Description = el.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                IsPrivate   = el.GetProperty("private").GetBoolean(),
                Stars       = el.GetProperty("stargazers_count").GetInt32(),
                UpdatedAt   = el.GetProperty("updated_at").GetString() ?? ""
            });
        }
        return result;
    }

    public async Task CloneAsync(string url, string destPath)
    {
        if (!string.IsNullOrEmpty(_gitHubToken) && url.StartsWith("https://github.com"))
        {
            var uri = new Uri(url);
            url = $"https://{_gitHubToken}@{uri.Host}{uri.PathAndQuery}";
        }
        await RunGitAsync($"clone \"{url}\" \"{destPath}\"", workDir: null);
    }

    private async Task RefreshState()
    {
        if (_repoPath is null) return;
        CurrentBranch = (await RunGitAsync("branch --show-current")).Trim();
        RemoteUrl     = (await RunGitAsync("remote get-url origin")).Trim();
        StateChanged?.Invoke();
    }

    private async Task<string> RunGitAsync(string args, string? workDir = null)
    {
        workDir ??= _repoPath;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git", args)
            {
                WorkingDirectory       = workDir ?? "",
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };
            using var proc = System.Diagnostics.Process.Start(psi)!;
            var stdout = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return stdout;
        }
        catch (Exception ex)
        {
            return $"error: {ex.Message}";
        }
    }

    private static string ExtractParam(string body, string key)
    {
        foreach (var part in body.Split('&'))
        {
            var kv = part.Split('=');
            if (kv.Length == 2 && kv[0].Trim() == key)
                return Uri.UnescapeDataString(kv[1].Trim());
        }
        return "";
    }

    private static string EscapeMessage(string msg) =>
        msg.Replace("\"", "\\\"").Replace("\n", " ");

    private static List<GitDiffHunk> ParseDiff(string patch)
    {
        var hunks = new List<GitDiffHunk>();
        GitDiffHunk? current = null;

        foreach (var raw in patch.Split('\n'))
        {
            if (raw.StartsWith("@@"))
            {
                var m = System.Text.RegularExpressions.Regex.Match(raw, @"@@ -(\d+)(?:,\d+)? \+(\d+)");
                current = new GitDiffHunk
                {
                    OldStart = m.Success ? int.Parse(m.Groups[1].Value) : 0,
                    NewStart = m.Success ? int.Parse(m.Groups[2].Value) : 0
                };
                hunks.Add(current);
                continue;
            }
            if (current is null) continue;
            if (raw.Length == 0) continue;

            current.Lines.Add(new GitDiffLine
            {
                Origin  = raw[0],
                Content = raw[1..]
            });
        }

        return hunks;
    }

    public void Dispose() => _http.Dispose();
}

public sealed class GitHubRepo
{
    public string FullName    { get; init; } = "";
    public string CloneUrl    { get; init; } = "";
    public string Description { get; init; } = "";
    public bool   IsPrivate   { get; init; }
    public int    Stars       { get; init; }
    public string UpdatedAt   { get; init; } = "";
}