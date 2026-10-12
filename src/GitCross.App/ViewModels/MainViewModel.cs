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

public enum DiffSource { None, Commit, Unstaged, Staged }

public partial class MainViewModel : ObservableObject
{
    private const int MaxLanesShown = 12;

    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private GitRepository? _repo;
    private List<HistoryItem> _allItems = [];
    private IReadOnlyList<GitRef> _refs = [];
    private IReadOnlyList<StashEntry> _stashes = [];
    private string? _fileFilter; // when set, history lists only commits touching this file
    private string? _detailSha; // commit or stash whose files are listed
    private int _busy;
    private DiffSource _diffSource;
    private string? _diffPath;
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
    [ObservableProperty] private string _blameTitle = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasFileFilter)), NotifyPropertyChangedFor(nameof(FileFilterText))] private string? _fileFilterPath;
    [ObservableProperty] private ThemeVariant _themeVariant = ThemeVariant.Default;

    public bool HasRepo => RepoName is not null;
    public string UnstagedHeader => $"Unstaged changes ({Unstaged.Count})";
    public string StagedHeader => $"Staged changes ({Staged.Count})";
    public int SummaryLength => CommitMessage.Split('\n')[0].Length;
    public double HeaderGraphWidth => _fileFilter is null && SearchText.Trim().Length == 0 ? GraphWidth : 0;
    public bool HasFileFilter => FileFilterPath is not null;
    public string FileFilterText => $"History of {FileFilterPath}";
    public bool HasDiff => Diff.Count > 0;
    public bool HunkActionsEnabled => _diffSource is DiffSource.Unstaged or DiffSource.Staged;
    public bool CanDiscardHunk => _diffSource == DiffSource.Unstaged;
    public string HunkPrimaryText => _diffSource == DiffSource.Staged ? "Unstage hunk" : "Stage hunk";
    public string ThemeLabel => _settings.Theme switch { "Light" => "☀ Light", "Dark" => "🌙 Dark", _ => "◐ System" };

    public ObservableCollection<HistoryItem> Items { get; } = [];
    public ObservableCollection<BlameRow> BlameRows { get; } = [];
    public ObservableCollection<SidebarEntry> Sidebar { get; } = [];
    public ObservableCollection<FileChange> CommitFiles { get; } = [];
    public ObservableCollection<FileChange> Unstaged { get; } = [];
    public ObservableCollection<FileChange> Staged { get; } = [];
    public ObservableCollection<DiffLine> Diff { get; } = [];
    public ObservableCollection<RecentRepo> Recent { get; } = [];

    /// <summary>Supplied by the view to ask before destructive actions (title, message → confirmed). Unset = proceed.</summary>
    public Func<string, string, Task<bool>>? Confirm { get; set; }

    private async Task<bool> ConfirmAsync(string title, string message)
        => Confirm is null || await Confirm(title, message);

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

    public async Task ShowFileHistoryAsync(string path)
    {
        _fileFilter = path;
        FileFilterPath = path;
        SelectedTab = 0;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ClearFileHistoryAsync()
    {
        _fileFilter = null;
        FileFilterPath = null;
        await RefreshAsync();
    }

    /// <summary>Opens the Blame tab for a file at a revision (null = work tree).</summary>
    public async Task ShowBlameAsync(string path, string? rev)
    {
        if (_repo is not { } repo)
        {
            return;
        }

        await Guard(async () =>
        {
            var lines = await repo.GetBlameAsync(path, rev);
            var now = DateTimeOffset.Now;
            BlameRows.Clear();
            string? last = null;
            var stripe = false;
            foreach (var l in lines)
            {
                var first = l.Sha != last;
                if (first) { stripe = !stripe; last = l.Sha; }
                BlameRows.Add(new BlameRow(l, first, stripe, RelativeTime.Format(l.Date, now)));
            }

            BlameTitle = rev is null ? path : $"{path} @ {rev[..Math.Min(8, rev.Length)]}";
            SelectedTab = 2;
        });
    }

    /// <summary>Jump from a blame line (or anywhere) to a commit in the history tab.</summary>
    public async Task ShowCommitAsync(string sha)
    {
        if (_fileFilter is not null)
        {
            await ClearFileHistoryAsync();
        }

        if (!string.IsNullOrEmpty(SearchText))
        {
            SearchText = "";
        }

        SelectedTab = 0;
        SelectedItem = Items.FirstOrDefault(i => i.Commit?.Sha == sha);
    }

    public string? DetailSha => _detailSha;

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
            var commitsTask = _fileFilter is { } ff ? repo.GetFileLogAsync(ff) : repo.GetLogAsync();
            var refsTask = repo.GetRefsAsync();
            var statusTask = repo.GetStatusAsync();
            var stashTask = repo.GetStashesAsync();
            await Task.WhenAll(commitsTask, refsTask, statusTask, stashTask);
            var (commits, refs, status) = (commitsTask.Result, refsTask.Result, statusTask.Result);
            _stashes = stashTask.Result;
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
        // A single file's history is not a connected graph, so draw plain rows.
        var rows = _fileFilter is null ? GraphLayout.Build(commits) : commits.Select(c => new GraphRow(c, 0, 0, [], 1, false)).ToList();
        var lanes = Math.Min(rows.Count == 0 ? 1 : rows.Max(r => r.LaneCount), MaxLanesShown);
        GraphWidth = lanes * Controls.GraphCell.LaneWidth + 8;
        OnPropertyChanged(nameof(HeaderGraphWidth));

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
            Row = _fileFilter is null ? r : null,
            Subject = r.Commit.Subject,
            Author = r.Commit.AuthorName,
            Initials = RelativeTime.Initials(r.Commit.AuthorName),
            When = RelativeTime.Format(r.Commit.AuthorDate, now),
            FullDate = r.Commit.AuthorDate.ToString("f"),
            ShortSha = r.Commit.ShortSha,
            Labels = labels.GetValueOrDefault(r.Commit.Sha) ?? [],
            GraphWidth = _fileFilter is null ? GraphWidth : 0,
        }).ToList();

        var changes = status.Staged.Count + status.Unstaged.Count;
        if (changes > 0 && _fileFilter is null)
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

        if (_stashes.Count > 0)
        {
            Sidebar.Add(new SidebarEntry("STASHES", IsHeader: true, Count: _stashes.Count.ToString()));
            foreach (var st in _stashes)
            {
                Sidebar.Add(new SidebarEntry(st.Message, Stash: st));
            }
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter(SelectedItem?.Commit?.Sha);

    private void ApplyFilter(string? keepSha)
    {
        OnPropertyChanged(nameof(HeaderGraphWidth));
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
        if (value?.Stash is { } stash)
        {
            SelectedItem = null;
            _ = LoadDetailsAsync(stash.Sha, $"{stash.Ref}  ·  {stash.Message}");
            return;
        }

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
        if (item.Commit is not { } c)
        {
            return;
        }

        await LoadDetailsAsync(c.Sha, $"{c.Sha}\n{c.AuthorName} <{c.AuthorEmail}> · {c.AuthorDate:f}");
    }

    /// <summary>Loads message + changed files of a commit or stash into the details pane.</summary>
    private async Task LoadDetailsAsync(string sha, string header)
    {
        if (_repo is not { } repo)
        {
            return;
        }

        ClearDetails();
        var v = _version;
        _detailSha = sha;
        await Guard(async () =>
        {
            var msgTask = repo.GetCommitMessageAsync(sha);
            var filesTask = repo.GetCommitFilesAsync(sha);
            await Task.WhenAll(msgTask, filesTask);
            if (v != _version)
            {
                return;
            }

            CommitInfo = $"{header}\n\n{msgTask.Result}";
            Replace(CommitFiles, filesTask.Result);
            if (CommitFiles.Count > 0)
            {
                SelectedCommitFile = CommitFiles[0];
            }
        });
    }

    partial void OnSelectedCommitFileChanged(FileChange? value)
    {
        if (value is not null && _detailSha is { } sha)
        {
            _ = ShowDiffAsync(DiffSource.Commit, value.Path, () => _repo!.GetCommitDiffAsync(sha, value.Path));
        }
    }

    partial void OnSelectedUnstagedChanged(FileChange? value)
    {
        if (value is null)
        {
            return;
        }

        SelectedStaged = null;
        var untracked = value.Kind == ChangeKind.Untracked;
        _ = ShowDiffAsync(untracked ? DiffSource.Commit : DiffSource.Unstaged, value.Path, () => _repo!.GetWorkingDiffAsync(value.Path, false, untracked));
    }

    partial void OnSelectedStagedChanged(FileChange? value)
    {
        if (value is null)
        {
            return;
        }

        SelectedUnstaged = null;
        _ = ShowDiffAsync(DiffSource.Staged, value.Path, () => _repo!.GetWorkingDiffAsync(value.Path, true));
    }

    partial void OnCommitMessageChanged(string value) => OnPropertyChanged(nameof(SummaryLength));

    private async Task ShowDiffAsync(DiffSource source, string title, Func<Task<string>> load)
    {
        var v = ++_version;
        Diff.Clear();
        DiffTitle = title;
        _diffSource = source;
        _diffPath = title;
        NotifyHunkState();
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

    private void NotifyHunkState()
    {
        OnPropertyChanged(nameof(HunkActionsEnabled));
        OnPropertyChanged(nameof(CanDiscardHunk));
        OnPropertyChanged(nameof(HunkPrimaryText));
    }

    [RelayCommand]
    private Task HunkPrimaryAsync(int hunk)
        => HunkActionAsync(r => r.StageHunkAsync(_diffPath!, hunk, unstage: _diffSource == DiffSource.Staged));

    [RelayCommand]
    private async Task HunkDiscardAsync(int hunk)
    {
        if (await ConfirmAsync("Discard hunk", $"Permanently discard this change in {_diffPath}?"))
        {
            await HunkActionAsync(r => r.DiscardHunkAsync(_diffPath!, hunk));
        }
    }

    private async Task HunkActionAsync(Func<GitRepository, Task> action)
    {
        if (_repo is not { } repo || _diffPath is not { } path || !HunkActionsEnabled)
        {
            return;
        }

        var source = _diffSource;
        await Mutate(() => action(repo), true);

        // Reselect the same file so its (shorter) diff reappears; fall back to the other list.
        var inFirst = source == DiffSource.Staged ? Staged : Unstaged;
        var inOther = source == DiffSource.Staged ? Unstaged : Staged;
        if (inFirst.FirstOrDefault(f => f.Path == path) is { } f1)
        {
            if (source == DiffSource.Staged) { SelectedStaged = f1; } else { SelectedUnstaged = f1; }
        }
        else if (inOther.FirstOrDefault(f => f.Path == path) is { } f2)
        {
            if (source == DiffSource.Staged) { SelectedUnstaged = f2; } else { SelectedStaged = f2; }
        }
    }

    [RelayCommand] private Task StageAsync(FileChange? f) => Mutate(() => _repo!.StageAsync([f!.Path]), f);

    [RelayCommand] private Task UnstageAsync(FileChange? f) => Mutate(() => _repo!.UnstageAsync([f!.Path]), f);

    [RelayCommand] private Task StageAllAsync() => Mutate(() => _repo!.StageAsync(Unstaged.Select(u => u.Path).ToList()), Unstaged.Count > 0);

    [RelayCommand] private Task UnstageAllAsync() => Mutate(() => _repo!.UnstageAsync(Staged.Select(u => u.Path).ToList()), Staged.Count > 0);

    [RelayCommand]
    private async Task DiscardAsync(FileChange? f)
    {
        // Never delete untracked files from a button click.
        if (f is not null && f.Kind != ChangeKind.Untracked
            && await ConfirmAsync("Discard changes", $"Permanently discard all unstaged changes in {f.Path}?"))
        {
            await Mutate(() => _repo!.DiscardAsync([f.Path]), f);
        }
    }

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

    public Task StashApplyAsync(StashEntry s) => Mutate(() => _repo!.StashApplyAsync(s), s);

    public Task StashPopEntryAsync(StashEntry s) => Mutate(() => _repo!.StashPopAsync(s), s);

    public Task StashDropAsync(StashEntry s) => Mutate(() => _repo!.StashDropAsync(s), s);

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
