using System.Collections.ObjectModel;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCross.Core.Conflicts;
using GitCross.Core.Diff;
using GitCross.Core.Git;
using GitCross.Core.Graph;
using GitCross.Core.Models;
using GitCross.Core.Rebase;
using GitCross.Core.Settings;
using GitCross.Core.Submodules;
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
    private IReadOnlyList<SubmoduleInfo> _submodules = [];
    private readonly Stack<string> _parents = new(); // superprojects we descended from; lets "Back" return
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
        GitRunner.Executable = string.IsNullOrWhiteSpace(_settings.GitExecutable) ? "git" : _settings.GitExecutable;
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
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasOperation)), NotifyPropertyChangedFor(nameof(OperationText))] private GitOperation _operation;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(OperationText)), NotifyPropertyChangedFor(nameof(CanContinue))] private int _conflictCount;
    [ObservableProperty] private string _blameTitle = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasFileFilter)), NotifyPropertyChangedFor(nameof(FileFilterText))] private string? _fileFilterPath;
    [ObservableProperty] private ThemeVariant _themeVariant = ThemeVariant.Default;

    public bool HasRepo => RepoName is not null;
    public bool HasParent => _parents.Count > 0;
    public string BackText => _parents.Count > 0 ? $"◀  Back to {Path.GetFileName(_parents.Peek().TrimEnd('/', '\\'))}" : "";
    public string UnstagedHeader => $"Unstaged changes ({Unstaged.Count})";
    public string StagedHeader => $"Staged changes ({Staged.Count})";
    public int SummaryLength => CommitMessage.Split('\n')[0].Length;
    public double HeaderGraphWidth => _fileFilter is null && SearchText.Trim().Length == 0 ? GraphWidth : 0;
    public bool HasOperation => Operation != GitOperation.None;
    public bool CanContinue => ConflictCount == 0;
    public string OperationText => Operation switch
    {
        GitOperation.None => "",
        _ => ConflictCount > 0
            ? $"{OperationName(Operation)} in progress — {ConflictCount} conflicted file{(ConflictCount == 1 ? "" : "s")} to resolve"
            : $"{OperationName(Operation)} in progress — all conflicts resolved, ready to continue",
    };

    private static string OperationName(GitOperation op) => op switch
    {
        GitOperation.CherryPick => "Cherry-pick",
        GitOperation.Revert => "Revert",
        GitOperation.Rebase => "Rebase",
        _ => "Merge",
    };

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
    public ObservableCollection<SplitRow> SplitRows { get; } = [];

    public bool IsSplitView
    {
        get => _settings.SplitDiff;
        set
        {
            if (_settings.SplitDiff == value) { return; }
            _settings.SplitDiff = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsUnifiedView));
            OnPropertyChanged(nameof(ShowSplit));
            OnPropertyChanged(nameof(ShowUnified));
            OnPropertyChanged(nameof(LineActionsVisible));
            RebuildSplit();
            SaveSettings();
        }
    }

    public bool IsUnifiedView => !IsSplitView;
    public bool ShowSplit => IsSplitView && HasDiff;
    public bool ShowUnified => !IsSplitView && HasDiff;

    private void RebuildSplit()
    {
        SplitRows.Clear();
        if (!IsSplitView)
        {
            return; // built lazily: only the visible mode pays for its layout
        }

        foreach (var r in SideBySide.Build(Diff.ToList()))
        {
            SplitRows.Add(r);
        }
    }
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

    public AppSettings Settings => _settings;

    /// <summary>Persist settings edited by the Settings dialog and apply them immediately.</summary>
    public async Task ApplySettingsAsync()
    {
        GitRunner.Executable = string.IsNullOrWhiteSpace(_settings.GitExecutable) ? "git" : _settings.GitExecutable;
        SaveSettings();
        await RefreshAsync(); // history size may have changed
    }

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
        var opened = await OpenCoreAsync(path, remember: true);
        if (opened)
        {
            _parents.Clear();
            NotifyParent();
        }

        return opened;
    }

    private void NotifyParent()
    {
        OnPropertyChanged(nameof(HasParent));
        OnPropertyChanged(nameof(BackText));
    }

    private async Task<bool> OpenCoreAsync(string path, bool remember)
    {
        var repo = await GitRepository.OpenAsync(path);
        if (repo is null)
        {
            Fail($"'{path}' is not a git repository");
            return false;
        }

        _repo = repo;
        RepoName = repo.Name;
        _fileFilter = null;
        FileFilterPath = null;
        if (remember)
        {
            _settings.AddRecent(repo.Root);
            SaveSettings();
            Recent.Clear();
            foreach (var p in _settings.RecentRepositories)
            {
                Recent.Add(new RecentRepo(Path.GetFileName(p.TrimEnd('/', '\\')), p));
            }
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
    public Task RefreshAsync() => RefreshCoreAsync(silent: false);

    /// <summary>Refresh triggered in the background (window activation): never replaces an error the user has not seen yet.</summary>
    public Task RefreshSilentAsync() => RefreshCoreAsync(silent: true);

    private async Task RefreshCoreAsync(bool silent)
    {
        if (_repo is not { } repo)
        {
            return;
        }

        var keep = SelectedItem?.Commit?.Sha;
        await Guard(async () =>
        {
            var commitsTask = _fileFilter is { } ff ? repo.GetFileLogAsync(ff, _settings.MaxCommits) : repo.GetLogAsync(_settings.MaxCommits);
            var refsTask = repo.GetRefsAsync();
            var statusTask = repo.GetStatusAsync();
            var stashTask = repo.GetStashesAsync();
            var subTask = repo.GetSubmodulesAsync();
            await Task.WhenAll(commitsTask, refsTask, statusTask, stashTask, subTask);
            _submodules = subTask.Result;
            var (commits, refs, status) = (commitsTask.Result, refsTask.Result, statusTask.Result);
            _stashes = stashTask.Result;
            var op = await repo.GetOperationAsync();
            _refs = refs;

            Replace(Unstaged, status.Unstaged);
            Replace(Staged, status.Staged);
            NotifyCounts();
            ConflictCount = status.Unstaged.Count(f => f.Kind == ChangeKind.Conflicted);
            if (op != GitOperation.None && Operation == GitOperation.None && ConflictCount > 0)
            {
                SelectedTab = 1; // jump to the files that need attention
            }

            Operation = op;

            BranchText = status.Branch ?? "detached HEAD";
            SyncText = status.Ahead + status.Behind > 0 ? $"↑{status.Ahead}  ↓{status.Behind}" : "";

            BuildHistory(commits, refs, status);
            BuildSidebar(refs);
            ApplyFilter(keep);

            if (silent && IsError)
            {
                return;
            }

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

        if (_submodules.Count > 0)
        {
            Sidebar.Add(new SidebarEntry("SUBMODULES", IsHeader: true, Count: _submodules.Count.ToString()));
            foreach (var m in _submodules)
            {
                Sidebar.Add(new SidebarEntry(m.Path, Submodule: m));
            }
        }

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
        NotifyDiffChanged();
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
        if (value.Kind == ChangeKind.Conflicted)
        {
            _ = ShowConflictAsync(value.Path);
            return;
        }

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

    private async Task ShowConflictAsync(string path)
    {
        var v = ++_version;
        Diff.Clear();
        DiffTitle = $"{path}  ·  conflict: red = ours (current branch), green = theirs (incoming)";
        _diffSource = DiffSource.None;
        _diffPath = path;
        NotifyHunkState();
        await Guard(async () =>
        {
            var text = await File.ReadAllTextAsync(_repo!.ResolvePath(path));
            if (v != _version)
            {
                return;
            }

            Replace(Diff, ConflictParser.Parse(text));
            NotifyDiffChanged();
        });
    }

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
            NotifyDiffChanged();
        });
    }

    private void NotifyDiffChanged()
    {
        RebuildSplit();
        OnPropertyChanged(nameof(HasDiff));
        OnPropertyChanged(nameof(ShowSplit));
        OnPropertyChanged(nameof(ShowUnified));
        OnPropertyChanged(nameof(LineActionsVisible));
    }

    private Dictionary<int, ISet<int>> _lineSelection = [];

    public int SelectedChangeCount => _lineSelection.Values.Sum(v => v.Count);
    public bool LineActionsVisible => HunkActionsEnabled && ShowUnified;
    public string LineSelectionText => SelectedChangeCount == 0
        ? "Click changed lines (Shift/Ctrl for several) to stage or discard just those lines"
        : $"{SelectedChangeCount} changed line{(SelectedChangeCount == 1 ? "" : "s")} selected";
    public string LinePrimaryText => _diffSource == DiffSource.Staged ? "Unstage selected lines" : "Stage selected lines";

    /// <summary>Called by the view whenever the highlighted rows of the unified diff change.</summary>
    public void SetDiffSelection(IEnumerable<DiffLine> selected)
    {
        var map = new Dictionary<int, ISet<int>>();
        foreach (var l in selected.Where(l => l.Kind is DiffLineKind.Added or DiffLineKind.Removed && l.BodyIndex >= 0))
        {
            if (!map.TryGetValue(l.HunkIndex, out var set))
            {
                map[l.HunkIndex] = set = new HashSet<int>();
            }

            set.Add(l.BodyIndex);
        }

        _lineSelection = map;
        OnPropertyChanged(nameof(SelectedChangeCount));
        OnPropertyChanged(nameof(LineSelectionText));
        LinePrimaryCommand.NotifyCanExecuteChanged();
        LineDiscardCommand.NotifyCanExecuteChanged();
    }

    private bool HasLineSelection => SelectedChangeCount > 0;

    [RelayCommand(CanExecute = nameof(HasLineSelection))]
    private Task LinePrimaryAsync()
    {
        var sel = _lineSelection;
        var unstage = _diffSource == DiffSource.Staged;
        return HunkActionAsync(r => r.StageLinesAsync(_diffPath!, sel, unstage));
    }

    [RelayCommand(CanExecute = nameof(HasLineSelection))]
    private async Task LineDiscardAsync()
    {
        var sel = _lineSelection;
        if (await ConfirmAsync("Discard lines", $"Permanently discard {SelectedChangeCount} selected line(s) in {_diffPath}?"))
        {
            await HunkActionAsync(r => r.DiscardLinesAsync(_diffPath!, sel));
        }
    }

    private void NotifyHunkState()
    {
        OnPropertyChanged(nameof(HunkActionsEnabled));
        OnPropertyChanged(nameof(CanDiscardHunk));
        OnPropertyChanged(nameof(HunkPrimaryText));
        OnPropertyChanged(nameof(LineActionsVisible));
        OnPropertyChanged(nameof(LinePrimaryText));
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

    public Task ResolveAsync(FileChange f, bool ours) => Mutate(() => _repo!.ResolveAsync(f.Path, ours), f);

    public string? FullPath(string relative) => _repo?.ResolvePath(relative);

    [RelayCommand]
    private Task ContinueOperationAsync()
    {
        var op = Operation;
        return op == GitOperation.None ? Task.CompletedTask : Mutate(() => _repo!.ContinueAsync(op), op);
    }

    [RelayCommand]
    private async Task AbortOperationAsync()
    {
        var op = Operation;
        if (op != GitOperation.None
            && await ConfirmAsync($"Abort {OperationName(op).ToLowerInvariant()}", "Abandon the operation and restore the state from before it started? Resolutions made so far are lost."))
        {
            await Mutate(() => _repo!.AbortAsync(op), op);
        }
    }

    [RelayCommand] private Task FetchAsync() => Mutate(() => _repo!.FetchAsync(), true);

    [RelayCommand] private Task PullRebaseAsync() => Mutate(() => _repo!.PullAsync(rebase: true), true);

    public async Task MergeWithAsync(GitRef r, MergeMode mode)
    {
        await Mutate(() => _repo!.MergeAsync(r.Name, mode), r);
        if (mode == MergeMode.Squash && !IsError && Staged.Count > 0)
        {
            SelectedTab = 1;
            StatusText = $"Squash merge of {r.Name} is staged. Write a commit message and commit.";
        }
    }

    /// <summary>Builds the plan for an interactive rebase onto a commit, or null (with a status message) when there is nothing to replay.</summary>
    public async Task<RebaseViewModel?> PrepareInteractiveRebaseAsync(string onto, string label)
    {
        if (_repo is not { } repo)
        {
            return null;
        }

        IReadOnlyList<Commit> commits = [];
        await Guard(async () => commits = await repo.GetRebaseCandidatesAsync(onto));
        if (commits.Count == 0)
        {
            IsError = false;
            StatusText = "No commits to rebase after that commit on the current branch.";
            return null;
        }

        return new RebaseViewModel(label, commits, sha => repo.GetCommitMessageAsync(sha));
    }

    public Task InteractiveRebaseAsync(string onto, IReadOnlyList<RebaseTodoItem> todo) => Mutate(() => _repo!.InteractiveRebaseAsync(onto, todo), todo);

    public Task RebaseOntoAsync(GitRef r) => Mutate(() => _repo!.RebaseAsync(r.Name), r);

    public string CurrentBranch => BranchText;

    [RelayCommand] private Task PullAsync() => Mutate(() => _repo!.PullAsync(rebase: _settings.PullRebase), true);

    [RelayCommand] private Task PullMergeAsync() => Mutate(() => _repo!.PullAsync(rebase: false), true);

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

    /// <summary>Opens an initialized submodule as its own repository, remembering the superproject for "Back".</summary>
    public async Task OpenSubmoduleAsync(SubmoduleInfo sub)
    {
        if (_repo is not { } parent)
        {
            return;
        }

        if (!sub.IsInitialized)
        {
            IsError = false;
            StatusText = $"{sub.Path} is not initialized yet: right-click → Initialize / update.";
            return;
        }

        var parentRoot = parent.Root;
        if (await OpenCoreAsync(parent.SubmoduleFullPath(sub), remember: false))
        {
            _parents.Push(parentRoot);
            NotifyParent();
        }
    }

    [RelayCommand]
    private async Task BackToParentAsync()
    {
        if (_parents.Count == 0)
        {
            return;
        }

        var target = _parents.Pop();
        NotifyParent();
        await OpenCoreAsync(target, remember: false);
    }

    public Task UpdateSubmoduleAsync(SubmoduleInfo sub) => Mutate(() => _repo!.SubmoduleUpdateAsync(sub.Path), sub);

    [RelayCommand]
    private Task UpdateAllSubmodulesAsync() => Mutate(() => _repo!.SubmoduleUpdateAsync(), _submodules.Count > 0);

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
                try
                {
                    await action();
                    await RefreshAsync();
                }
                catch (InvalidOperationException ex)
                {
                    Fail(ex.Message);
                }
                catch (GitException ex)
                {
                    // A failed merge/cherry-pick leaves the repo mid-operation: show that state instead of just an error.
                    await RefreshAsync();
                    Fail(Operation != GitOperation.None && ConflictCount > 0
                        ? $"{OperationName(Operation)} stopped: {ConflictCount} file(s) have conflicts. Resolve them, then Continue (or Abort)."
                        : ex.Message);
                }
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
