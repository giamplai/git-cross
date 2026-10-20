using GitCross.Core.Models;
using GitCross.Core.Submodules;

namespace GitCross.Core.Git;

/// <summary>One thing that would make a multi-repository operation stop half way. Reported before anything is changed.</summary>
public sealed record SyncProblem(string Repo, string Message)
{
    public override string ToString() => $"{Repo}: {Message}";
}

/// <summary>Live state of one submodule: branch (null = detached), commits ahead/behind its upstream, uncommitted changes.</summary>
public sealed record SubmoduleLiveStatus(string Path, string? Branch, int Ahead, int Behind, bool Dirty);

/// <summary>A repository taking part in a coordinated operation: <see cref="Path"/> is relative to the top repository ("." for itself).</summary>
public sealed record SyncMember(string Path, GitRepository Repo)
{
    public bool IsRoot => Path == ".";
}

/// <summary>Thrown by the coordinated operations when the pre-flight check found problems; nothing was changed.</summary>
public sealed class SyncPreflightException(IReadOnlyList<SyncProblem> problems)
    : InvalidOperationException("Nothing was changed. Fix these first:\n" + string.Join('\n', problems.Select(p => "• " + p)))
{
    public IReadOnlyList<SyncProblem> Problems { get; } = problems;
}

/// <summary>Operations applied to a repository and all its (nested) submodules together: same branch everywhere, one commit, one push.</summary>
public sealed partial class GitRepository
{
    /// <summary>This repository and every initialized submodule below it, deepest first (so the top repository is last).</summary>
    public async Task<IReadOnlyList<SyncMember>> GetSyncMembersAsync()
    {
        var list = new List<SyncMember>();
        await CollectAsync(this, ".", list);
        return list;

        static async Task CollectAsync(GitRepository repo, string rel, List<SyncMember> acc)
        {
            foreach (var sub in await repo.GetSubmodulesAsync())
            {
                if (sub.IsInitialized && await OpenAsync(repo.SubmoduleFullPath(sub)) is { } child)
                {
                    await CollectAsync(child, rel == "." ? sub.Path : rel + "/" + sub.Path, acc);
                }
            }

            acc.Add(new SyncMember(rel, repo));
        }
    }

    /// <summary>Name of the checked out branch, or null when HEAD is detached.</summary>
    public async Task<string?> GetCurrentBranchAsync()
    {
        var r = await _git.RunAsync(["symbolic-ref", "--short", "-q", "HEAD"], null, default);
        return r.Success ? r.StdOut.Trim() : null;
    }

    private async Task<bool> RefExistsAsync(string fullRef)
        => (await _git.RunAsync(["show-ref", "--verify", "--quiet", fullRef], null, default)).Success;

    private async Task<string?> RemoteBranchOfAsync(string branch)
    {
        var r = await _git.RunAsync(["for-each-ref", "--format=%(refname)", $"refs/remotes/*/{branch}"], null, default);
        return r.Success ? r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(n => !n.EndsWith("/HEAD"))?.Trim() : null;
    }

    private async Task<bool> IsAncestorAsync(string ancestor, string descendant)
        => (await _git.RunAsync(["merge-base", "--is-ancestor", ancestor, descendant], null, default)).Success;

    /// <summary>Places that block any coordinated operation: not initialized submodules, paused operations, conflicts.</summary>
    private async Task<List<SyncProblem>> BaselineProblemsAsync(IReadOnlyList<SyncMember> members, bool needInitialized)
    {
        var problems = new List<SyncProblem>();
        if (needInitialized)
        {
            foreach (var m in members)
            {
                foreach (var sub in (await m.Repo.GetSubmodulesAsync()).Where(s => !s.IsInitialized))
                {
                    problems.Add(new SyncProblem(m.IsRoot ? sub.Path : m.Path + "/" + sub.Path, "submodule not initialized (right-click → Initialize / update)"));
                }
            }
        }

        foreach (var m in members)
        {
            if (await m.Repo.GetOperationAsync() != GitOperation.None)
            {
                problems.Add(new SyncProblem(m.Path, "a merge/rebase/cherry-pick is in progress"));
            }
        }

        return problems;
    }

    // ───────── same branch everywhere ─────────

    /// <summary>Why <see cref="CreateBranchEverywhereAsync"/> would fail, without changing anything.</summary>
    public async Task<IReadOnlyList<SyncProblem>> CheckCreateBranchEverywhereAsync(string name)
    {
        var members = await GetSyncMembersAsync();
        var problems = await BaselineProblemsAsync(members, needInitialized: true);
        if (!(await _git.RunAsync(["check-ref-format", "--branch", name], null, default)).Success)
        {
            problems.Add(new SyncProblem(".", $"'{name}' is not a valid branch name"));
            return problems;
        }

        foreach (var m in members)
        {
            if (await m.Repo.RefExistsAsync("refs/heads/" + name))
            {
                problems.Add(new SyncProblem(m.Path, $"branch '{name}' already exists"));
            }
        }

        return problems;
    }

    /// <summary>Creates <paramref name="name"/> at the current commit of the top repository and of every submodule, and checks it out everywhere.</summary>
    public async Task CreateBranchEverywhereAsync(string name)
    {
        if (await CheckCreateBranchEverywhereAsync(name) is { Count: > 0 } problems)
        {
            throw new SyncPreflightException(problems);
        }

        var members = await GetSyncMembersAsync();
        var done = new List<(SyncMember M, string? Prev)>();
        try
        {
            foreach (var m in members)
            {
                var prev = await m.Repo.GetCurrentBranchAsync() ?? await m.Repo.HeadShaAsync();
                await m.Repo.CreateBranchAsync(name);
                done.Add((m, prev));
            }
        }
        catch
        {
            foreach (var (m, prev) in done)
            {
                await m.Repo._git.RunAsync(["checkout", "-q", prev!], null, default);
                await m.Repo._git.RunAsync(["branch", "-D", name], null, default);
            }

            throw;
        }
    }

    /// <summary>Why <see cref="AlignSubmodulesToBranchAsync"/> would fail, without changing anything.</summary>
    public async Task<IReadOnlyList<SyncProblem>> CheckAlignSubmodulesAsync(string branch)
    {
        var members = await GetSyncMembersAsync();
        var problems = await BaselineProblemsAsync(members, needInitialized: true);
        foreach (var m in members.Where(m => !m.IsRoot))
        {
            if (!await m.Repo.RefExistsAsync("refs/heads/" + branch) && await m.Repo.RemoteBranchOfAsync(branch) is null)
            {
                problems.Add(new SyncProblem(m.Path, $"has no branch '{branch}' (local or remote)"));
            }
        }

        return problems;
    }

    /// <summary>
    /// Puts every submodule on <paramref name="branch"/> (creating a tracking branch from the remote when needed) instead of leaving it
    /// detached at the recorded commit. When the branch is just behind the recorded commit it is fast-forwarded to it, so nothing is lost.
    /// </summary>
    public async Task AlignSubmodulesToBranchAsync(string branch)
    {
        if (await CheckAlignSubmodulesAsync(branch) is { Count: > 0 } problems)
        {
            throw new SyncPreflightException(problems);
        }

        var done = new List<(SyncMember M, string Prev)>();
        try
        {
            foreach (var m in (await GetSyncMembersAsync()).Where(m => !m.IsRoot))
            {
                var recorded = await m.Repo.HeadShaAsync();
                var prev = await m.Repo.GetCurrentBranchAsync() ?? recorded;
                if (await m.Repo.RefExistsAsync("refs/heads/" + branch))
                {
                    await m.Repo.CheckoutAsync(branch);
                }
                else
                {
                    await m.Repo._git.OutAsync("checkout", "-b", branch, "--track", (await m.Repo.RemoteBranchOfAsync(branch))!.Replace("refs/remotes/", ""));
                }

                done.Add((m, prev));
                var tip = await m.Repo.HeadShaAsync();
                if (tip != recorded && await m.Repo.IsAncestorAsync(tip, recorded))
                {
                    await m.Repo._git.OutAsync("merge", "--ff-only", recorded);
                }
            }
        }
        catch
        {
            foreach (var (m, prev) in done)
            {
                await m.Repo._git.RunAsync(["checkout", "-q", prev], null, default);
            }

            throw;
        }
    }

    // ───────── one commit everywhere ─────────

    /// <summary>Why <see cref="CommitEverywhereAsync"/> would fail, without changing anything.</summary>
    public async Task<IReadOnlyList<SyncProblem>> CheckCommitEverywhereAsync(string message)
    {
        var members = await GetSyncMembersAsync();
        var problems = await BaselineProblemsAsync(members, needInitialized: false);
        if (message.Trim().Length == 0)
        {
            problems.Add(new SyncProblem(".", "empty commit message"));
        }

        foreach (var m in members)
        {
            var status = await m.Repo.GetStatusAsync();
            if (status.Unstaged.Any(f => f.Kind == ChangeKind.Conflicted))
            {
                problems.Add(new SyncProblem(m.Path, "unresolved conflicts"));
            }

            var changed = status.Staged.Count + status.Unstaged.Count > 0;
            if (!changed && !m.IsRoot)
            {
                continue;
            }

            if (status.Branch is null)
            {
                problems.Add(new SyncProblem(m.Path, "HEAD is detached: check out a branch first (commits would be orphaned)"));
            }

            if (!(await m.Repo._git.RunAsync(["var", "GIT_COMMITTER_IDENT"], null, default)).Success)
            {
                problems.Add(new SyncProblem(m.Path, "git user.name / user.email are not configured"));
            }
        }

        return problems;
    }

    /// <summary>
    /// Stages everything and commits with the same message in every repository that has changes, deepest submodule first, so the
    /// parents record the new submodule commits in the same step. Returns the paths that received a commit.
    /// </summary>
    public async Task<IReadOnlyList<string>> CommitEverywhereAsync(string message)
    {
        if (await CheckCommitEverywhereAsync(message) is { Count: > 0 } problems)
        {
            throw new SyncPreflightException(problems);
        }

        var committed = new List<(SyncMember M, string Before)>();
        try
        {
            foreach (var m in await GetSyncMembersAsync())
            {
                var before = await m.Repo.HeadShaAsync();
                await m.Repo._git.OutAsync("add", "-A");
                if ((await m.Repo._git.RunAsync(["diff", "--cached", "--quiet"], null, default)).Success)
                {
                    continue; // nothing to record here
                }

                await m.Repo.CommitAsync(message);
                committed.Add((m, before));
            }
        }
        catch
        {
            foreach (var (m, before) in committed)
            {
                await m.Repo._git.RunAsync(["reset", "--soft", before], null, default); // changes stay staged
            }

            throw;
        }

        return committed.Select(c => c.M.Path).ToList();
    }

    // ───────── one push everywhere ─────────

    private sealed record PushPlan(SyncMember Member, bool SetUpstream);

    private async Task<(List<PushPlan> Plan, List<SyncProblem> Problems)> PlanPushAsync()
    {
        var members = await GetSyncMembersAsync();
        var problems = await BaselineProblemsAsync(members, needInitialized: false);
        var plan = new List<PushPlan>();
        foreach (var m in members)
        {
            var fetched = await m.Repo._git.RunAsync(["fetch", "--all", "--prune"], null, default);
            if (!fetched.Success)
            {
                problems.Add(new SyncProblem(m.Path, "cannot reach the remote: " + fetched.StdErr.Trim()));
                continue;
            }

            var status = await m.Repo.GetStatusAsync();
            if (status.Branch is null)
            {
                if (!m.IsRoot)
                {
                    problems.Add(new SyncProblem(m.Path, "HEAD is detached: check out a branch first"));
                }

                continue;
            }

            var hasUpstream = (await m.Repo._git.RunAsync(["rev-parse", "--abbrev-ref", "@{u}"], null, default)).Success;
            if (hasUpstream && status.Behind > 0)
            {
                problems.Add(new SyncProblem(m.Path, $"{status.Behind} commit(s) behind its upstream: pull first"));
                continue;
            }

            if (!hasUpstream || status.Ahead > 0)
            {
                plan.Add(new PushPlan(m, !hasUpstream));
            }
        }

        // The remote itself has the last word (hooks, protected branches, auth): ask it without sending anything.
        foreach (var p in plan.Where(p => problems.All(x => x.Repo != p.Member.Path)))
        {
            var dry = await p.Member.Repo._git.RunAsync(PushArgs(p.SetUpstream, dryRun: true), null, default);
            if (!dry.Success)
            {
                problems.Add(new SyncProblem(p.Member.Path, "push would be rejected: " + dry.StdErr.Trim().Replace('\n', ' ')));
            }
        }

        return (plan, problems);
    }

    private static string[] PushArgs(bool setUpstream, bool dryRun)
    {
        List<string> a = ["push"];
        if (dryRun) { a.Add("--dry-run"); }
        if (setUpstream) { a.AddRange(["-u", "origin", "HEAD"]); }
        return [.. a];
    }

    /// <summary>Why <see cref="PushEverywhereAsync"/> would fail. Fetches every repository and does a dry-run push of each.</summary>
    public async Task<IReadOnlyList<SyncProblem>> CheckPushEverywhereAsync() => (await PlanPushAsync()).Problems;

    /// <summary>Pushes every repository that is ahead (submodules first, so the top repository never points to unpublished commits). Returns the pushed paths.</summary>
    public async Task<IReadOnlyList<string>> PushEverywhereAsync()
    {
        var (plan, problems) = await PlanPushAsync();
        if (problems.Count > 0)
        {
            throw new SyncPreflightException(problems);
        }

        var pushed = new List<string>();
        foreach (var p in plan)
        {
            try
            {
                await p.Member.Repo._git.OutAsync(PushArgs(p.SetUpstream, dryRun: false));
            }
            catch (GitException ex)
            {
                // Earlier pushes cannot be taken back: say exactly what already went out.
                throw new InvalidOperationException(
                    $"Push failed in {p.Member.Path}: {ex.Message}\nAlready pushed: {(pushed.Count == 0 ? "nothing" : string.Join(", ", pushed))}. Fix the cause and push again.", ex);
            }

            pushed.Add(p.Member.Path);
        }

        return pushed;
    }

    // ───────── live state of the direct submodules ─────────

    /// <summary>Branch, ahead/behind and dirtiness of each initialized direct submodule, by path.</summary>
    public async Task<IReadOnlyDictionary<string, SubmoduleLiveStatus>> GetSubmoduleLiveStatusesAsync()
    {
        var result = new Dictionary<string, SubmoduleLiveStatus>();
        foreach (var sub in await GetSubmodulesAsync())
        {
            if (sub.IsInitialized && await OpenAsync(SubmoduleFullPath(sub)) is { } child)
            {
                var st = await child.GetStatusAsync();
                result[sub.Path] = new SubmoduleLiveStatus(sub.Path, st.Branch, st.Ahead, st.Behind, st.Staged.Count + st.Unstaged.Count > 0);
            }
        }

        return result;
    }

    // ───────── one pull everywhere (keeping work in progress) ─────────

    /// <summary>What <see cref="PullEverywhereAsync"/> did in one repository.</summary>
    public sealed record SyncPullStep(string Path, int Commits, SmartPullOutcome Outcome);

    /// <summary>Undo points of the last coordinated pull, deepest repository first. Empty when there is none.</summary>
    public async Task<IReadOnlyList<(SyncMember Member, SmartPullState State)>> GetSyncStatesAsync()
    {
        var list = new List<(SyncMember, SmartPullState)>();
        foreach (var m in await GetSyncMembersAsync())
        {
            if (await m.Repo.GetSmartPullStateAsync() is { } st)
            {
                list.Add((m, st));
            }
        }

        return list;
    }

    /// <summary>
    /// Why <see cref="PullEverywhereAsync"/> would fail, without changing anything: fetches every repository, then requires a branch with
    /// an upstream, no conflicts or paused operation, no unfinished earlier sync, and (for merge) predicts conflicts with a trial merge
    /// that writes nothing. Uncommitted changes are fine: they are parked and re-applied.
    /// </summary>
    public async Task<IReadOnlyList<SyncProblem>> CheckPullEverywhereAsync(bool rebase)
    {
        var members = await GetSyncMembersAsync();
        var problems = await BaselineProblemsAsync(members, needInitialized: false);
        foreach (var m in members)
        {
            if (await m.Repo.GetSmartPullStateAsync() is { IsPending: true })
            {
                problems.Add(new SyncProblem(m.Path, "an earlier sync left your changes parked: restore them or undo it first"));
            }

            var fetched = await m.Repo._git.RunAsync(["fetch", "--all", "--prune"], null, default);
            if (!fetched.Success)
            {
                problems.Add(new SyncProblem(m.Path, "cannot reach the remote: " + fetched.StdErr.Trim()));
                continue;
            }

            var status = await m.Repo.GetStatusAsync();
            if (status.Unstaged.Any(f => f.Kind == ChangeKind.Conflicted))
            {
                problems.Add(new SyncProblem(m.Path, "unresolved conflicts"));
                continue;
            }

            if (status.Branch is null)
            {
                problems.Add(new SyncProblem(m.Path, "HEAD is detached: check out a branch first"));
                continue;
            }

            if (!(await m.Repo._git.RunAsync(["rev-parse", "--abbrev-ref", "@{u}"], null, default)).Success)
            {
                problems.Add(new SyncProblem(m.Path, $"branch '{status.Branch}' has no upstream"));
                continue;
            }

            if (!rebase && status.Behind > 0 && status.Ahead > 0
                && !(await m.Repo._git.RunAsync(["merge-tree", "--write-tree", "HEAD", "@{u}"], null, default)).Success)
            {
                problems.Add(new SyncProblem(m.Path, "merging the upstream would conflict"));
            }
        }

        return problems;
    }

    /// <summary>
    /// Pulls every repository that is behind, submodules first, parking and re-applying uncommitted work in each one
    /// (<see cref="SmartPullAsync"/>). Every touched repository keeps an undo point: <see cref="UndoEverywhereAsync"/> restores them all.
    /// If a step fails, the repositories already pulled are put back, so you are never left half updated.
    /// </summary>
    public async Task<IReadOnlyList<SyncPullStep>> PullEverywhereAsync(bool rebase = false)
    {
        if (await CheckPullEverywhereAsync(rebase) is { Count: > 0 } problems)
        {
            throw new SyncPreflightException(problems);
        }

        var members = await GetSyncMembersAsync();
        foreach (var m in members)
        {
            if (await m.Repo.GetSmartPullStateAsync() is { IsPending: false })
            {
                await m.Repo.DiscardSmartPullAsync(); // a new sync supersedes the previous undo points
            }
        }

        var done = new List<SyncMember>();
        var steps = new List<SyncPullStep>();
        foreach (var m in members)
        {
            var behind = (await m.Repo.GetStatusAsync()).Behind;
            if (behind == 0)
            {
                continue;
            }

            SmartPullOutcome outcome;
            try
            {
                outcome = await m.Repo.SmartPullAsync(rebase);
            }
            catch (Exception ex) when (ex is GitException or InvalidOperationException)
            {
                throw new InvalidOperationException($"Pull failed in {m.Path}: {ex.Message}\n{await RollbackAsync(done)}", ex);
            }

            done.Add(m);
            if (outcome == SmartPullOutcome.PullConflicts)
            {
                throw new InvalidOperationException($"The pull conflicted in {m.Path}.\n{await RollbackAsync(done)}");
            }

            steps.Add(new SyncPullStep(m.Path, behind, outcome));
        }

        return steps;
    }

    private static async Task<string> RollbackAsync(List<SyncMember> done)
    {
        var failed = new List<string>();
        foreach (var m in Enumerable.Reverse(done))
        {
            try
            {
                await m.Repo.UndoSmartPullAsync();
            }
            catch (Exception ex) when (ex is GitException or InvalidOperationException)
            {
                failed.Add($"{m.Path} ({ex.Message.Split('\n')[0]})");
            }
        }

        return failed.Count == 0 ? "Everything was put back as it was before." : "Could not restore: " + string.Join(", ", failed) + ".";
    }

    /// <summary>Why <see cref="UndoEverywhereAsync"/> would fail: a repository changed branch or received new commits since the sync.</summary>
    public async Task<IReadOnlyList<SyncProblem>> CheckUndoEverywhereAsync()
    {
        var problems = new List<SyncProblem>();
        foreach (var (m, st) in await GetSyncStatesAsync())
        {
            if (await m.Repo.GetCurrentBranchAsync() != st.Branch)
            {
                problems.Add(new SyncProblem(m.Path, $"is no longer on '{st.Branch}'"));
            }
            else if (await m.Repo.GetOperationAsync() == GitOperation.None && st.PostHead is not null && await m.Repo.HeadShaAsync() != st.PostHead)
            {
                problems.Add(new SyncProblem(m.Path, "has new commits since the sync; undoing would discard them"));
            }
        }

        return problems;
    }

    /// <summary>Puts every repository back to how it was before <see cref="PullEverywhereAsync"/>: branches at their old commits, work in progress restored.</summary>
    public async Task UndoEverywhereAsync()
    {
        if (await CheckUndoEverywhereAsync() is { Count: > 0 } problems)
        {
            throw new SyncPreflightException(problems);
        }

        foreach (var (m, _) in (await GetSyncStatesAsync()).Reverse())
        {
            await m.Repo.UndoSmartPullAsync();
        }
    }

    /// <summary>Forgets the undo points of all repositories (keeps the result).</summary>
    public async Task KeepEverywhereAsync()
    {
        var states = await GetSyncStatesAsync();
        if (states.FirstOrDefault(s => s.State.IsPending) is { Member: { } pending })
        {
            throw new InvalidOperationException($"{pending.Path}: your changes are still parked; restore them (or undo the sync) first.");
        }

        foreach (var (m, _) in states)
        {
            await m.Repo.DiscardSmartPullAsync();
        }
    }
}
