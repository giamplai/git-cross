using System.Text.Json;
using GitCross.Core.Models;

namespace GitCross.Core.Git;

/// <summary>Result of <see cref="GitRepository.SmartPullAsync"/> / <see cref="GitRepository.RestoreWipAsync"/>.</summary>
public enum SmartPullOutcome
{
    /// <summary>Pulled and the work in progress is back in the work tree.</summary>
    Clean,

    /// <summary>Pulled, but re-applying the work in progress produced conflicts to resolve.</summary>
    WipConflicts,

    /// <summary>The pull itself stopped on conflicts; the work in progress is parked until it is finished.</summary>
    PullConflicts,
}

/// <summary>
/// Snapshot taken before a smart pull. <see cref="PostHead"/> is null while the pull is still unfinished;
/// <see cref="WipRestored"/> flips once the parked work in progress has been re-applied.
/// </summary>
public sealed record SmartPullState(string Branch, string PreHead, string? WipSha, string? PostHead, bool WipRestored)
{
    public bool IsPending => WipSha is not null && !WipRestored;
}

public sealed partial class GitRepository
{
    private const string SnapshotHeadRef = "refs/gitcross/smartpull/head";
    private const string SnapshotWipRef = "refs/gitcross/smartpull/wip";

    /// <summary>The undo snapshot of the last smart pull, or null when there is none.</summary>
    public async Task<SmartPullState?> GetSmartPullStateAsync()
    {
        var file = await SmartPullFileAsync();
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SmartPullState>(await File.ReadAllTextAsync(file));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Pull while keeping uncommitted work: parks changes (staged, unstaged, untracked) in a stash commit, pulls,
    /// then re-applies them. The pre-pull HEAD and the parked work stay recorded (and protected from gc) until
    /// <see cref="UndoSmartPullAsync"/> or <see cref="DiscardSmartPullAsync"/>.
    /// </summary>
    public async Task<SmartPullOutcome> SmartPullAsync(bool rebase = false)
    {
        if (await GetSmartPullStateAsync() is not null)
        {
            throw new InvalidOperationException("The previous sync can still be undone. Keep it or undo it before syncing again.");
        }

        if (await GetOperationAsync() != GitOperation.None)
        {
            throw new InvalidOperationException("Finish or abort the operation in progress before pulling.");
        }

        var head = await _git.RunAsync(["symbolic-ref", "--short", "-q", "HEAD"], null, default);
        if (!head.Success)
        {
            throw new InvalidOperationException("Pull needs a checked out branch (HEAD is detached).");
        }

        var branch = head.StdOut.Trim();
        var pre = await HeadShaAsync();
        string? wip = null;
        if ((await _git.OutAsync("status", "--porcelain")).Trim().Length > 0)
        {
            var before = await StashTopAsync();
            await _git.OutAsync("stash", "push", "-u", "-m", "git-cross: work in progress before pull");
            var after = await StashTopAsync();
            if (after != before)
            {
                wip = after;
                await _git.OutAsync("update-ref", SnapshotWipRef, wip!);
                await _git.OutAsync("stash", "drop"); // kept alive by our own ref; the stash list stays tidy
            }
        }

        await _git.OutAsync("update-ref", SnapshotHeadRef, pre);
        await SaveSmartPullAsync(new SmartPullState(branch, pre, wip, null, false));

        try
        {
            await PullAsync(rebase);
        }
        catch (GitException)
        {
            if (await GetOperationAsync() != GitOperation.None)
            {
                return SmartPullOutcome.PullConflicts;
            }

            if (await HeadShaAsync() == pre)
            {
                // Nothing happened (offline, auth, no upstream…): put everything back as it was and report the error.
                await UndoSmartPullAsync();
                throw;
            }
        }

        return await RestoreWipAsync();
    }

    /// <summary>Re-applies the parked work in progress after the pull is finished (also used once pull conflicts are resolved).</summary>
    public async Task<SmartPullOutcome> RestoreWipAsync()
    {
        var state = await GetSmartPullStateAsync() ?? throw new InvalidOperationException("There is no sync to restore.");
        if (await GetOperationAsync() != GitOperation.None)
        {
            throw new InvalidOperationException("Finish the operation in progress first.");
        }

        state = state with { PostHead = await HeadShaAsync() };
        if (state.WipSha is null || state.WipRestored)
        {
            await SaveSmartPullAsync(state);
            return SmartPullOutcome.Clean;
        }

        var applied = await _git.RunAsync(["stash", "apply", "--index", state.WipSha], null, default);
        if (!applied.Success && !await HasConflictsAsync())
        {
            applied = await _git.RunAsync(["stash", "apply", state.WipSha], null, default); // index state could not be kept
        }

        if (applied.Success)
        {
            await SaveSmartPullAsync(state with { WipRestored = true });
            return SmartPullOutcome.Clean;
        }

        if (await HasConflictsAsync())
        {
            await SaveSmartPullAsync(state with { WipRestored = true });
            return SmartPullOutcome.WipConflicts;
        }

        await SaveSmartPullAsync(state);
        applied.OrThrow("stash apply");
        return SmartPullOutcome.Clean;
    }

    /// <summary>
    /// Goes back to exactly the state before the smart pull: branch at its old commit, work in progress (with its
    /// staged/unstaged split) back in place. Refused when the branch received new commits since the sync.
    /// </summary>
    public async Task UndoSmartPullAsync()
    {
        var state = await GetSmartPullStateAsync() ?? throw new InvalidOperationException("There is no sync to undo.");
        var branch = (await _git.RunAsync(["symbolic-ref", "--short", "-q", "HEAD"], null, default)).StdOut.Trim();
        if (branch != state.Branch)
        {
            throw new InvalidOperationException($"The sync was made on '{state.Branch}': switch back to it to undo.");
        }

        var op = await GetOperationAsync();
        if (op != GitOperation.None)
        {
            await AbortAsync(op);
        }
        else if (state.PostHead is not null && await HeadShaAsync() != state.PostHead)
        {
            throw new InvalidOperationException("The branch has new commits since the sync; undoing would discard them. Use Reset if you really want to go back.");
        }

        if (state.WipSha is not null && state.WipRestored)
        {
            await RemoveUntrackedOfAsync(state.WipSha); // otherwise re-applying would refuse to overwrite them
        }

        await _git.OutAsync("reset", "--hard", state.PreHead);
        if (state.WipSha is not null)
        {
            var applied = await _git.RunAsync(["stash", "apply", "--index", state.WipSha], null, default);
            if (!applied.Success)
            {
                (await _git.RunAsync(["stash", "apply", state.WipSha], null, default)).OrThrow("stash apply");
            }
        }

        await ClearSmartPullAsync();
    }

    /// <summary>Forgets the undo snapshot (keeps the result). Refused while the parked work was not re-applied yet.</summary>
    public async Task DiscardSmartPullAsync()
    {
        if (await GetSmartPullStateAsync() is { IsPending: true })
        {
            throw new InvalidOperationException("Your changes are still parked: restore them (or undo the sync) first.");
        }

        await ClearSmartPullAsync();
    }

    internal async Task<string> HeadShaAsync() => (await _git.OutAsync("rev-parse", "HEAD")).Trim();

    private async Task<string?> StashTopAsync()
    {
        var r = await _git.RunAsync(["rev-parse", "--verify", "-q", "refs/stash"], null, default);
        return r.Success ? r.StdOut.Trim() : null;
    }

    private async Task<bool> HasConflictsAsync()
        => (await GetStatusAsync()).Unstaged.Any(f => f.Kind == ChangeKind.Conflicted);

    private async Task RemoveUntrackedOfAsync(string stashSha)
    {
        // The third parent of a `stash -u` commit holds the untracked files.
        var listing = await _git.RunAsync(["ls-tree", "-r", "-z", "--name-only", stashSha + "^3"], null, default);
        if (!listing.Success)
        {
            return;
        }

        foreach (var rel in listing.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var full = ResolvePath(rel);
            if (File.Exists(full))
            {
                File.Delete(full);
            }
        }
    }

    private async Task<string> SmartPullFileAsync()
        => Path.Combine((await _git.OutAsync("rev-parse", "--absolute-git-dir")).Trim(), "gitcross-smartpull.json");

    private async Task SaveSmartPullAsync(SmartPullState state)
        => await File.WriteAllTextAsync(await SmartPullFileAsync(), JsonSerializer.Serialize(state));

    private async Task ClearSmartPullAsync()
    {
        await _git.RunAsync(["update-ref", "-d", SnapshotHeadRef], null, default);
        await _git.RunAsync(["update-ref", "-d", SnapshotWipRef], null, default);
        File.Delete(await SmartPullFileAsync());
    }
}
