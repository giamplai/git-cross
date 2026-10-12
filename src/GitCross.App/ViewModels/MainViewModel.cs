using System.Collections.ObjectModel;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCross.Core.Diff;
using GitCross.Core.Git;
using GitCross.Core.Graph;
using GitCross.Core.Models;
using GitCross.Core.Settings;
using GitCross.Core.Util;

namespace GitCross.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const int MaxLanesShown = 12;

    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private GitRepository? _repo;
    private List<HistoryItem> _allItems = [];
    private IReadOnlyList<GitRef> _refs = [];
    private int _busy;
    private int _version; // guards against out-of-order async results

    public MainViewModel(SettingsStore? store = null)
    {
        _store = store ?? new SettingsStore(SettingsStore.DefaultPath);
        _settings = _store.Load();
        foreach (var p in _settings.RecentRepositories)
        {
            Recent.Add(new RecentRepo(Path.GetFileName(p.TrimEnd('/', '\\')), p));
        }
    }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasRepo))] private string? _repoName;
    [ObservableProperty] private string _branchText = "";
    [ObservableProperty] private string _syncText = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _selectedTab;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private HistoryItem? _selectedItem;
    [ObservableProperty] private SidebarEntry? _selectedSidebar;
    [ObservableProperty] private FileChange? _selectedCommitFile;
    [ObservableProperty] private FileChange? _selectedUnstaged;
    [ObservableProperty] private FileChange? _selectedStaged;
    [ObservableProperty] private string _commitMessage = "";
    [ObservableProperty] private bool _amend;
    [ObservableProperty] private string _commitInfo = "";
    [ObservableProperty] private string _newBranchName = "";
    [ObservableProperty] private string _diffTitle = "";
    [ObservableProperty] private double _graphWidth = 40;
    [ObservableProperty] private ThemeVariant _themeVariant = ThemeVariant.Default;

    public bool HasRepo => RepoName is not null;
    public string UnstagedHeader => $"Unstaged changes ({Unstaged.Count})";
    public string StagedHeader => $"Staged changes ({Staged.Count})";
    public int SummaryLength => CommitMessage.Split('\n')[0].Length;
    public bool HasDiff => Diff.Count > 0;
    public string ThemeLabel => _settings.Theme switch { "Light" => "☀ Light", "Dark" => "🌙 Dark", _ => "◐ System" };

    public ObservableCollection<HistoryItem> Items { get; } = [];
    public ObservableCollection<SidebarEntry> Sidebar { get; } = [];
    public ObservableCollection<FileChange> CommitFiles { get; } = [];
    public ObservableCollection<FileChange> Unstaged { get; } = [];
    public ObservableCollection<FileChange> Staged { get; } = [];
    public ObservableCollection<DiffLine> Diff { get; } = [];
    public ObservableCollection<RecentRepo> Recent { get; } = [];

    public string? RepoRoot => _repo?.Root;

    public void ApplySavedTheme() => ThemeVariant = ToVariant(_settings.Theme);

    private static ThemeVariant ToVariant(string t) => t switch
    {
        "Light" => ThemeVariant.Light,
        "Dark" => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    public string? InitialRepoPath => Recent.FirstOrDefault()?.Path;

    [RelayCommand]
    private Task OpenRecentAsync(string? path) => string.IsNullOrEmpty(path) ? Task.CompletedTask : OpenAsync(path);

    [RelayCommand]
    private void CycleTheme()
    {
        _settings.Theme = _settings.Theme switch { "System" => "Light", "Light" => "Dark", _ => "System" };
        ThemeVariant = ToVariant(_settings.Theme);
        OnPropertyChanged(nameof(ThemeLabel));
        SaveSettings();
    }

    public string? LastCloneDirectory
    {
        get => _settings.LastCloneDirectory;
        set { _settings.LastCloneDirectory = value; SaveSettings(); }
    }

    private void SaveSettings()
    {
        try
        {
            _store.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience; never fail the app because they cannot be written.
        }
    }

    public async Task<bool> OpenAsync(string path)
    {
        var repo = await GitRepository.OpenAsync(path);
        if (repo is null)
        {
            Fail($"'{path}' is not a git repository");
            return false;
        }

        _repo = repo;
        RepoName = repo.Name;
        _settings.AddRecent(repo.Root);
        SaveSettings();
        Recent.Clear();
        foreach (var p in _settings.RecentRepositories)
        {
            Recent.Add(new RecentRepo(Path.GetFileName(p.TrimEnd('/', '\\')), p));
        }

        SearchText = "";
        await RefreshAsync();
        return true;
    }

    public async Task InitAsync(string path)
    {
        try
        {
            var repo = await GitRepository.InitAsync(path);
            await OpenAsync(repo.Root);
        }
        catch (GitException ex)
        {
            Fail(ex.Message);
        }
    }

    [RelayCommand]
    private void CloseRepo()
    {
        _repo = null;
        RepoName = null;
        Items.Clear();
        _allItems = [];
        Sidebar.Clear();
        ClearDetails();
        StatusText = "";
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (_repo is not { } repo)
        {
            return;
        }

        var keep = SelectedItem?.Commit?.Sha;
        await Guard(async () =>
        {
            var commitsTask = repo.GetLogAsync();
            var refsTask = repo.GetRefsAsync();
            var statusTask = repo.GetStatusAsync();
            await Task.WhenAll(commitsTask, refsTask, statusTask);
            var (commits, refs, status) = (commitsTask.Result, refsTask.Result, statusTask.Result);
            _refs = refs;

            Replace(Unstaged, status.Unstaged);
            Replace(Staged, status.Staged);
            NotifyCounts();

            BranchText = status.Branch ?? "detached HEAD";
            SyncText = status.Ahead + status.Behind > 0 ? $"↑{status.Ahead}  ↓{status.Behind}" : "";

            BuildHistory(commits, refs, status);
            BuildSidebar(refs);
            ApplyFilter(keep);

            IsError = false;
            StatusText = $"{commits.Count} commits  ·  {Staged.Count} staged  ·  {Unstaged.Count} unstaged";
        });
    }

    private void BuildHistory(IReadOnlyList<Commit> commits, IReadOnlyList<GitRef> refs, WorkingStatus status)
    {
        var rows = GraphLayout.Build(commits);
        var lanes = Math.Min(rows.Count == 0 ? 1 : rows.Max(r => r.LaneCount), MaxLanesShown);
        GraphWidth = lanes * Controls.GraphCell.LaneWidth + 8;

        var labels = refs
            .Where(r => r.Kind != RefKind.Stash)
            .GroupBy(r => r.Sha)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<RefLabel>)g
                .OrderByDescending(r => r.IsCurrent).ThenBy(r => r.Kind).ThenBy(r => r.Name)
                .Select(r => new RefLabel(r.Name, r.Kind, r.IsCurrent)).ToList());

        var now = DateTimeOffset.Now;
        _allItems = rows.Select(r => new HistoryItem
        {
            Commit = r.Commit,
            Row = r,
            Subject = r.Commit.Subject,
            Author = r.Commit.AuthorName,
            Initials = RelativeTime.Initials(r.Commit.AuthorName),
            When = RelativeTime.Format(r.Commit.AuthorDate, now),
            FullDate = r.Commit.AuthorDate.ToString("f"),
            ShortSha = r.Commit.ShortSha,
            Labels = labels.GetValueOrDefault(r.Commit.Sha) ?? [],
            GraphWidth = GraphWidth,
        }).ToList();

        var changes = status.Staged.Count + status.Unstaged.Count;
        if (changes > 0)
        {
            _allItems.Insert(0, new HistoryItem
            {
                IsWorkingDir = true,
                Subject = $"Uncommitted changes ({changes})",
                GraphWidth = GraphWidth,
            });
        }
    }

    private void BuildSidebar(IReadOnlyList<GitRef> refs)
    {
        Sidebar.Clear();
        void Group(string title, RefKind kind)
        {
            var items = refs.Where(r => r.Kind == kind).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (items.Count == 0)
            {
                return;
            }

            Sidebar.Add(new SidebarEntry(title, IsHeader: true, Count: items.Count.ToString()));
            foreach (var r in items)
            {
                Sidebar.Add(new SidebarEntry(r.Name, r));
            }
        }

        Group("BRANCHES", RefKind.LocalBranch);
        Group("REMOTES", RefKind.RemoteBranch);
        Group("TAGS", RefKind.Tag);
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter(SelectedItem?.Commit?.Sha);

    private void ApplyFilter(string? keepSha)
    {
        var q = SearchText.Trim();
        IEnumerable<HistoryItem> src = _allItems;
        if (q.Length > 0)
        {
            // A filtered list is no longer contiguous, so the graph is hidden while searching.
            src = _allItems.Where(i => !i.IsWorkingDir && Matches(i, q)).Select(i => new HistoryItem
            {
                Commit = i.Commit, Subject = i.Subject, Author = i.Author, Initials = i.Initials, When = i.When,
                FullDate = i.FullDate, ShortSha = i.ShortSha, Labels = i.Labels, GraphWidth = 0,
            });
        }

        Replace(Items, src);
        SelectedItem = keepSha is null ? null : Items.FirstOrDefault(i => i.Commit?.Sha == keepSha);
    }

    private static bool Matches(HistoryItem i, string q)
        => i.Subject.Contains(q, StringComparison.OrdinalIgnoreCase)
           || i.Author.Contains(q, StringComparison.OrdinalIgnoreCase)
           || i.Commit!.Sha.StartsWith(q, StringComparison.OrdinalIgnoreCase)
           || i.Labels.Any(l => l.Text.Contains(q, StringComparison.OrdinalIgnoreCase));

    partial void OnSelectedSidebarChanged(SidebarEntry? value)
    {
        if (value?.Ref is { } r && Items.FirstOrDefault(i => i.Commit?.Sha == r.Sha) is { } item)
        {
            SelectedItem = item;
        }
    }

    partial void OnSelectedItemChanged(HistoryItem? value)
    {
        if (value is null)
        {
            ClearDetails();
            return;
        }

        if (value.IsWorkingDir)
        {
            SelectedTab = 1;
            ClearDetails();
            return;
        }

        SelectedTab = 0;
        _ = LoadCommitAsync(value);
    }

    private void ClearDetails()
    {
        _version++;
        CommitFiles.Clear();
        Diff.Clear();
        CommitInfo = "";
        DiffTitle = "";
        OnPropertyChanged(nameof(HasDiff));
    }

    private async Task LoadCommitAsync(HistoryItem item)
    {
        if (_repo is not { } repo || item.Commit is not { } c)
        {
            return;
        }

        ClearDetails();
        var v = _version;
        await Guard(async () =>
        {
            var msgTask = repo.GetCommitMessageAsync(c.Sha);
            var filesTask = repo.GetCommitFilesAsync(c.Sha);
            await Task.WhenAll(msgTask, filesTask);
            if (v != _version)
            {
                return;
            }

            CommitInfo = $"{c.Sha}\n{c.AuthorName} <{c.AuthorEmail}> · {c.AuthorDate:f}\n\n{msgTask.Result}";
            Replace(CommitFiles, filesTask.Result);
            if (CommitFiles.Count > 0)
            {
                SelectedCommitFile = CommitFiles[0];
            }
        });
    }

    partial void OnSelectedCommitFileChanged(FileChange? value)
    {
        if (value is not null && SelectedItem?.Commit is { } c)
        {
            _ = ShowDiffAsync(value.Path, () => _repo!.GetCommitDiffAsync(c.Sha, value.Path));
        }
    }

    partial void OnSelectedUnstagedChanged(FileChange? value)
    {
        if (value is null)
        {
            return;
        }

        SelectedStaged = null;
        _ = ShowDiffAsync(value.Path, () => _repo!.GetWorkingDiffAsync(value.Path, false, value.Kind == ChangeKind.Untracked));
    }

    partial void OnSelectedStagedChanged(FileChange? value)
    {
        if (value is null)
        {
            return;
        }

        SelectedUnstaged = null;
        _ = ShowDiffAsync(value.Path, () => _repo!.GetWorkingDiffAsync(value.Path, true));
    }

    partial void OnCommitMessageChanged(string value) => OnPropertyChanged(nameof(SummaryLength));

    private async Task ShowDiffAsync(string title, Func<Task<string>> load)
    {
        var v = ++_version;
        Diff.Clear();
        DiffTitle = title;
        await Guard(async () =>
        {
            var text = await load();
            if (v != _version)
            {
                return;
            }

            Replace(Diff, DiffParser.Parse(text));
            OnPropertyChanged(nameof(HasDiff));
        });
    }

    [RelayCommand] private Task StageAsync(FileChange? f) => Mutate(() => _repo!.StageAsync([f!.Path]), f);

    [RelayCommand] private Task UnstageAsync(FileChange? f) => Mutate(() => _repo!.UnstageAsync([f!.Path]), f);

    [RelayCommand] private Task StageAllAsync() => Mutate(() => _repo!.StageAsync(Unstaged.Select(u => u.Path).ToList()), Unstaged.Count > 0);

    [RelayCommand] private Task UnstageAllAsync() => Mutate(() => _repo!.UnstageAsync(Staged.Select(u => u.Path).ToList()), Staged.Count > 0);

    [RelayCommand]
    private Task DiscardAsync(FileChange? f)
        => f is null || f.Kind == ChangeKind.Untracked
            ? Task.CompletedTask // never delete untracked files from a button click
            : Mutate(() => _repo!.DiscardAsync([f.Path]), f);

    [RelayCommand]
    private async Task CommitAsync()
    {
        if (string.IsNullOrWhiteSpace(CommitMessage) || (Staged.Count == 0 && !Amend))
        {
            Fail("Nothing to commit: stage files and enter a message");
            return;
        }

        var msg = CommitMessage;
        await Mutate(() => _repo!.CommitAsync(msg, Amend), true);
        if (!IsError)
        {
            CommitMessage = "";
            Amend = false;
            SelectedTab = 0;
        }
    }

    [RelayCommand] private Task FetchAsync() => Mutate(() => _repo!.FetchAsync(), true);

    [RelayCommand] private Task PullAsync() => Mutate(() => _repo!.PullAsync(), true);

    [RelayCommand] private Task PushAsync() => Mutate(() => _repo!.PushAsync(), true);

    [RelayCommand] private Task StashAsync() => Mutate(() => _repo!.StashPushAsync(), Unstaged.Count + Staged.Count > 0);

    [RelayCommand] private Task StashPopAsync() => Mutate(() => _repo!.StashPopAsync(), true);

    [RelayCommand]
    public Task CheckoutRefAsync(GitRef? r)
        => r is null
            ? Task.CompletedTask
            : Mutate(() => _repo!.CheckoutAsync(r.Kind == RefKind.RemoteBranch ? r.Name[(r.Name.IndexOf('/') + 1)..] : r.Name), r);

    [RelayCommand]
    private Task MergeRefAsync(GitRef? r) => r is null ? Task.CompletedTask : Mutate(() => _repo!.MergeAsync(r.Name), r);

    [RelayCommand]
    private Task DeleteBranchAsync(GitRef? r)
        => r is not { Kind: RefKind.LocalBranch, IsCurrent: false } ? Task.CompletedTask : Mutate(() => _repo!.DeleteBranchAsync(r.Name), r);

    [RelayCommand]
    private async Task CreateBranchAsync()
    {
        var name = NewBranchName.Trim();
        if (name.Length == 0)
        {
            return;
        }

        var start = SelectedItem?.Commit?.Sha;
        await Mutate(() => _repo!.CreateBranchAsync(name, start), true);
        if (!IsError)
        {
            NewBranchName = "";
        }
    }

    public Task CherryPickAsync(string sha) => Mutate(() => _repo!.CherryPickAsync(sha), sha);

    public Task RevertAsync(string sha) => Mutate(() => _repo!.RevertAsync(sha), sha);

    public Task ResetToAsync(string sha, ResetMode mode) => Mutate(() => _repo!.ResetAsync(sha, mode), sha);

    public Task TagAsync(string sha, string name) => Mutate(() => _repo!.CreateTagAsync(name, sha), sha);

    public Task CheckoutShaAsync(string sha) => Mutate(() => _repo!.CheckoutAsync(sha), sha);

    [RelayCommand]
    private Task CheckoutCommitAsync()
        => SelectedItem?.Commit is { } c ? Mutate(() => _repo!.CheckoutAsync(c.Sha), c) : Task.CompletedTask;

    private Task Mutate(Func<Task> action, object? guard)
        => guard is null or false || _repo is null
            ? Task.CompletedTask
            : Guard(async () =>
            {
                await action();
                await RefreshAsync();
            });

    private async Task Guard(Func<Task> work)
    {
        IsBusy = ++_busy > 0;
        try
        {
            await work();
        }
        catch (GitException ex)
        {
            Fail(ex.Message);
        }
        finally
        {
            IsBusy = --_busy > 0;
        }
    }

    private void Fail(string message)
    {
        IsError = true;
        StatusText = message.Replace('\n', ' ');
    }

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(UnstagedHeader));
        OnPropertyChanged(nameof(StagedHeader));
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var i in items)
        {
            target.Add(i);
        }
    }
}
