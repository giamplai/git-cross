using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCross.Core.Git;
using GitCross.Core.Graph;
using GitCross.Core.Models;

namespace GitCross.App.ViewModels;

public sealed record DiffLine(string Text, string Kind);

public sealed record RefGroup(string Title, IReadOnlyList<GitRef> Items);

public partial class MainViewModel : ObservableObject
{
    private GitRepository? _repo;

    [ObservableProperty] private string _title = "git-cross";
    [ObservableProperty] private string _statusText = "Open a repository to begin (Ctrl+O)";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private GraphRow? _selectedRow;
    [ObservableProperty] private FileChange? _selectedCommitFile;
    [ObservableProperty] private FileChange? _selectedUnstaged;
    [ObservableProperty] private FileChange? _selectedStaged;
    [ObservableProperty] private string _commitMessage = "";
    [ObservableProperty] private bool _amend;
    [ObservableProperty] private string _commitBody = "";
    [ObservableProperty] private string _newBranchName = "";

    public ObservableCollection<GraphRow> Rows { get; } = [];
    public ObservableCollection<RefGroup> RefGroups { get; } = [];
    public ObservableCollection<FileChange> CommitFiles { get; } = [];
    public ObservableCollection<FileChange> Unstaged { get; } = [];
    public ObservableCollection<FileChange> Staged { get; } = [];
    public ObservableCollection<DiffLine> Diff { get; } = [];
    public ObservableCollection<string> Recent { get; } = [];

    public bool HasRepo => _repo is not null;

    public async Task OpenAsync(string path)
    {
        var repo = await GitRepository.OpenAsync(path);
        if (repo is null)
        {
            StatusText = $"'{path}' is not a git repository";
            return;
        }

        _repo = repo;
        Title = $"{repo.Name} - git-cross";
        Recent.Remove(repo.Root);
        Recent.Insert(0, repo.Root);
        OnPropertyChanged(nameof(HasRepo));
        await RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (_repo is null)
        {
            return;
        }

        await Guard(async () =>
        {
            var commits = await _repo.GetLogAsync();
            var refs = await _repo.GetRefsAsync();
            var status = await _repo.GetStatusAsync();

            Rows.Clear();
            foreach (var r in GraphLayout.Build(commits))
            {
                Rows.Add(r);
            }

            RefGroups.Clear();
            RefGroups.Add(new("Branches", refs.Where(r => r.Kind == RefKind.LocalBranch).ToList()));
            RefGroups.Add(new("Remotes", refs.Where(r => r.Kind == RefKind.RemoteBranch).ToList()));
            RefGroups.Add(new("Tags", refs.Where(r => r.Kind == RefKind.Tag).ToList()));

            Replace(Unstaged, status.Unstaged);
            Replace(Staged, status.Staged);
            var sync = status.Ahead + status.Behind > 0 ? $"  ↑{status.Ahead} ↓{status.Behind}" : "";
            StatusText = $"{status.Branch ?? "(detached HEAD)"}{sync}  •  {commits.Count} commits  •  {Staged.Count} staged, {Unstaged.Count} unstaged";
        });
    }

    partial void OnSelectedRowChanged(GraphRow? value) => _ = LoadCommitAsync(value);

    private async Task LoadCommitAsync(GraphRow? row)
    {
        CommitFiles.Clear();
        Diff.Clear();
        if (_repo is null || row is null)
        {
            CommitBody = "";
            return;
        }

        await Guard(async () =>
        {
            var c = row.Commit;
            var msg = await _repo.GetCommitMessageAsync(c.Sha);
            CommitBody = $"{c.Sha}\n{c.AuthorName} <{c.AuthorEmail}>  {c.AuthorDate:g}\n\n{msg}";
            Replace(CommitFiles, await _repo.GetCommitFilesAsync(c.Sha));
        });
    }

    partial void OnSelectedCommitFileChanged(FileChange? value)
        => _ = ShowDiffAsync(() => value is null || SelectedRow is null ? Task.FromResult("") : _repo!.GetCommitDiffAsync(SelectedRow.Commit.Sha, value.Path));

    partial void OnSelectedUnstagedChanged(FileChange? value)
    {
        if (value is not null)
        {
            _ = ShowDiffAsync(() => _repo!.GetWorkingDiffAsync(value.Path, false, value.Kind == ChangeKind.Untracked));
        }
    }

    partial void OnSelectedStagedChanged(FileChange? value)
    {
        if (value is not null)
        {
            _ = ShowDiffAsync(() => _repo!.GetWorkingDiffAsync(value.Path, true));
        }
    }

    private async Task ShowDiffAsync(Func<Task<string>> load)
    {
        Diff.Clear();
        if (_repo is null)
        {
            return;
        }

        await Guard(async () =>
        {
            var text = await load();
            foreach (var line in text.Split('\n'))
            {
                Diff.Add(new DiffLine(line, ClassifyDiffLine(line)));
            }
        });
    }

    public static string ClassifyDiffLine(string l) => l switch
    {
        _ when l.StartsWith("+++") || l.StartsWith("---") => "meta",
        _ when l.StartsWith('+') => "add",
        _ when l.StartsWith('-') => "del",
        _ when l.StartsWith("@@") => "hunk",
        _ when l.StartsWith("diff ") || l.StartsWith("index ") => "meta",
        _ => "ctx",
    };

    [RelayCommand]
    private Task StageAsync(FileChange? f) => Mutate(() => _repo!.StageAsync([f!.Path]), f);

    [RelayCommand]
    private Task UnstageAsync(FileChange? f) => Mutate(() => _repo!.UnstageAsync([f!.Path]), f);

    [RelayCommand]
    private Task StageAllAsync() => Mutate(() => _repo!.StageAsync(Unstaged.Select(u => u.Path)), Unstaged.Count > 0);

    [RelayCommand]
    private Task UnstageAllAsync() => Mutate(() => _repo!.UnstageAsync(Staged.Select(u => u.Path)), Staged.Count > 0);

    [RelayCommand]
    private async Task CommitAsync()
    {
        if (string.IsNullOrWhiteSpace(CommitMessage) || (Staged.Count == 0 && !Amend))
        {
            StatusText = "Nothing to commit: stage files and enter a message";
            return;
        }

        await Mutate(() => _repo!.CommitAsync(CommitMessage, Amend), true);
        if (!StatusText.StartsWith("Error"))
        {
            CommitMessage = "";
            Amend = false;
        }
    }

    [RelayCommand] private Task FetchAsync() => Mutate(() => _repo!.FetchAsync(), true);

    [RelayCommand] private Task PullAsync() => Mutate(() => _repo!.PullAsync(), true);

    [RelayCommand] private Task PushAsync() => Mutate(() => _repo!.PushAsync(), true);

    [RelayCommand] private Task StashAsync() => Mutate(() => _repo!.StashPushAsync(), true);

    [RelayCommand] private Task StashPopAsync() => Mutate(() => _repo!.StashPopAsync(), true);

    [RelayCommand]
    private Task CheckoutRefAsync(GitRef? r)
        => Mutate(() => _repo!.CheckoutAsync(r!.Kind == RefKind.RemoteBranch ? r.Name[(r.Name.IndexOf('/') + 1)..] : r.Name), r);

    [RelayCommand]
    private async Task CreateBranchAsync()
    {
        if (string.IsNullOrWhiteSpace(NewBranchName))
        {
            return;
        }

        var start = SelectedRow?.Commit.Sha;
        await Mutate(() => _repo!.CreateBranchAsync(NewBranchName.Trim(), start), true);
        NewBranchName = "";
    }

    [RelayCommand]
    private Task CheckoutCommitAsync() => Mutate(() => _repo!.CheckoutAsync(SelectedRow!.Commit.Sha), SelectedRow);

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
        IsBusy = true;
        try
        {
            await work();
        }
        catch (GitException ex)
        {
            StatusText = "Error: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
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
