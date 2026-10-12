using GitCross.Core.Diff;
using GitCross.Core.Models;

namespace GitCross.Core.Git;

/// <summary>High level operations on one repository. Every method maps to plain git CLI calls.</summary>
public sealed class GitRepository
{
    private readonly GitRunner _git;

    private GitRepository(string root)
    {
        Root = root;
        _git = new GitRunner(root);
    }

    public string Root { get; }

    public string Name => Path.GetFileName(Root.TrimEnd(Path.DirectorySeparatorChar, '/'));

    /// <summary>Returns the repository for <paramref name="path"/> (any folder inside a work tree), or null.</summary>
    public static async Task<GitRepository?> OpenAsync(string path)
    {
        if (!Directory.Exists(path))
        {
            return null;
        }

        var probe = await new GitRunner(path).RunAsync("rev-parse", "--show-toplevel");
        return probe.Success ? new GitRepository(Path.GetFullPath(probe.StdOut.Trim())) : null;
    }

    public static async Task<GitRepository> InitAsync(string path)
    {
        Directory.CreateDirectory(path);
        await new GitRunner(path).OutAsync("init");
        return (await OpenAsync(path))!;
    }

    /// <summary>Folder name git would pick for a clone URL ("https://x/y/repo.git" → "repo").</summary>
    public static string SuggestFolderName(string url)
    {
        var u = url.Trim().TrimEnd('/', '\\');
        if (u.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            u = u[..^4];
        }

        var i = u.LastIndexOfAny(['/', '\\', ':']);
        return i >= 0 ? u[(i + 1)..] : u;
    }

    public static async Task<GitRepository> CloneAsync(string url, string target)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(target))!;
        Directory.CreateDirectory(parent);
        await new GitRunner(parent).OutAsync("clone", url, target);
        return (await OpenAsync(target))!;
    }

    public async Task<IReadOnlyList<Commit>> GetLogAsync(int max = 2000, string? skipSha = null, bool all = true)
    {
        List<string> a = ["log", "--topo-order", $"--max-count={max}", $"--format={Parsers.LogFormat}"];
        if (all)
        {
            a.Add("--all");
        }

        var r = await _git.RunAsync(a, null, default);
        return r.Success ? Parsers.ParseLog(r.StdOut).ToList() : []; // empty repo => no commits
    }

    public async Task<IReadOnlyList<GitRef>> GetRefsAsync()
        => Parsers.ParseRefs(await _git.OutAsync("for-each-ref", $"--format={Parsers.RefFormat}")).ToList();

    public async Task<WorkingStatus> GetStatusAsync()
        => Parsers.ParseStatus(await _git.OutAsync("status", "--porcelain=v1", "-z", "--branch", "--untracked-files=all"));

    public async Task<IReadOnlyList<FileChange>> GetCommitFilesAsync(string sha)
        => Parsers.ParseNameStatus(await _git.OutAsync("diff-tree", "--no-commit-id", "--name-status", "-r", "-z", "-m", "--first-parent", "--root", sha)).ToList();

    public async Task<string> GetCommitMessageAsync(string sha)
        => (await _git.OutAsync("show", "-s", "--format=%B", sha)).TrimEnd();

    public Task<string> GetCommitDiffAsync(string sha, string path)
        => _git.OutAsync("show", "--format=", "--first-parent", "-m", sha, "--", path);

    public async Task<string> GetWorkingDiffAsync(string path, bool staged, bool untracked = false)
    {
        if (untracked)
        {
            // --no-index exits with 1 when files differ; that is expected here.
            var r = await _git.RunAsync(["diff", "--no-index", "--", NullDevice, path], null, default);
            return r.StdOut;
        }

        return staged
            ? await _git.OutAsync("diff", "--cached", "--", path)
            : await _git.OutAsync("diff", "--", path);
    }

    private static string NullDevice => OperatingSystem.IsWindows() ? "NUL" : "/dev/null";

    public Task StageAsync(IEnumerable<string> paths) => _git.OutAsync(["add", "--", .. paths]);

    public Task UnstageAsync(IEnumerable<string> paths) => _git.OutAsync(["reset", "-q", "HEAD", "--", .. paths]);

    public Task DiscardAsync(IEnumerable<string> paths) => _git.OutAsync(["checkout", "--", .. paths]);

    public Task CommitAsync(string message, bool amend = false)
        => _git.OutAsync(amend ? ["commit", "--amend", "-m", message] : ["commit", "-m", message]);

    public Task CheckoutAsync(string target) => _git.OutAsync("checkout", target);

    public Task CreateBranchAsync(string name, string? startPoint = null, bool checkout = true)
        => checkout
            ? _git.OutAsync(startPoint is null ? ["checkout", "-b", name] : ["checkout", "-b", name, startPoint])
            : _git.OutAsync(startPoint is null ? ["branch", name] : ["branch", name, startPoint]);

    public Task DeleteBranchAsync(string name, bool force = false) => _git.OutAsync("branch", force ? "-D" : "-d", name);

    public Task CreateTagAsync(string name, string? sha = null) => _git.OutAsync(sha is null ? ["tag", name] : ["tag", name, sha]);

    public Task MergeAsync(string branch) => _git.OutAsync("merge", branch);

    public Task FetchAsync() => _git.OutAsync("fetch", "--all", "--prune");

    public Task PullAsync(bool rebase = false) => _git.OutAsync(rebase ? ["pull", "--rebase"] : ["pull"]);

    public Task PushAsync(bool setUpstream = false)
        => _git.OutAsync(setUpstream ? ["push", "-u", "origin", "HEAD"] : ["push"]);

    public Task StashPushAsync(string? message = null)
        => _git.OutAsync(message is null ? ["stash", "push", "-u"] : ["stash", "push", "-u", "-m", message]);

    public Task StashPopAsync() => _git.OutAsync("stash", "pop");

    /// <summary>Applies a patch from memory. <paramref name="toIndex"/> targets the index (staging), otherwise the work tree.</summary>
    public async Task ApplyPatchAsync(string patch, bool toIndex, bool reverse)
    {
        List<string> a = ["apply", "--whitespace=nowarn", "--recount"];
        if (toIndex) { a.Add("--cached"); }
        if (reverse) { a.Add("--reverse"); }
        (await _git.RunAsync(a, patch, default)).OrThrow(string.Join(' ', a));
    }

    /// <summary>Stage (or, with <paramref name="unstage"/>, unstage) one hunk of a tracked file.</summary>
    public async Task StageHunkAsync(string path, int hunkIndex, bool unstage)
    {
        var diff = await GetWorkingDiffAsync(path, staged: unstage);
        var patch = PatchBuilder.BuildHunkPatch(diff, hunkIndex) ?? throw new InvalidOperationException("Hunk no longer exists; refresh and retry.");
        await ApplyPatchAsync(patch, toIndex: true, reverse: unstage);
    }

    public async Task DiscardHunkAsync(string path, int hunkIndex)
    {
        var diff = await GetWorkingDiffAsync(path, staged: false);
        var patch = PatchBuilder.BuildHunkPatch(diff, hunkIndex) ?? throw new InvalidOperationException("Hunk no longer exists; refresh and retry.");
        await ApplyPatchAsync(patch, toIndex: false, reverse: true);
    }

    public Task CherryPickAsync(string sha) => _git.OutAsync("cherry-pick", sha);

    /// <summary>Reverts a commit; merge commits are reverted against their first parent.</summary>
    public async Task RevertAsync(string sha)
    {
        var parents = (await _git.OutAsync("rev-list", "--parents", "-n", "1", sha)).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length - 1;
        await (parents > 1 ? _git.OutAsync("revert", "--no-edit", "-m", "1", sha) : _git.OutAsync("revert", "--no-edit", sha));
    }

    public Task ResetAsync(string sha, ResetMode mode)
        => _git.OutAsync("reset", mode switch { ResetMode.Soft => "--soft", ResetMode.Hard => "--hard", _ => "--mixed" }, sha);

    public Task AbortAsync(string operation) => _git.OutAsync(operation, "--abort"); // merge | cherry-pick | revert | rebase
}

public enum ResetMode { Soft, Mixed, Hard }
