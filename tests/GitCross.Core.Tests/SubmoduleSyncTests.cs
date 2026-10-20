using GitCross.Core.Git;

namespace GitCross.Core.Tests;

// Both classes set process-wide environment variables (FileTransportScope): they must not run in parallel.
[Collection("process-environment")]
public class SubmoduleSyncTests
{
    /// <summary>Bare remotes for a library and a superproject, plus a local clone of the superproject with its submodule initialized (detached, as git leaves it).</summary>
    private sealed class World : IDisposable
    {
        public required TempRepo Holder { get; init; }
        public required TempRepo LibSeed { get; init; }
        public required TempRepo Local { get; init; }
        public required GitRepository Super { get; init; }
        public string LibPath => Path.Combine(Local.Path, "libs", "lib");

        public static async Task<World> CreateAsync()
        {
            var (holder, _) = await TempRepo.CreateAsync();
            holder.Git("init", "-q", "--bare", "-b", "main", "lib.git");
            holder.Git("init", "-q", "--bare", "-b", "main", "super.git");
            var libUrl = Path.Combine(holder.Path, "lib.git");
            var superUrl = Path.Combine(holder.Path, "super.git");

            var (libSeed, lib) = await TempRepo.CreateAsync();
            libSeed.Write("a.txt", "1\n"); await lib.StageAsync(["a.txt"]); await lib.CommitAsync("lib 1");
            libSeed.Git("remote", "add", "origin", libUrl); libSeed.Git("push", "-q", "-u", "origin", "main");

            var (superSeed, sup) = await TempRepo.CreateAsync();
            using (superSeed)
            {
                superSeed.Write("s.txt", "1\n"); await sup.StageAsync(["s.txt"]); await sup.CommitAsync("super 1");
                await sup.SubmoduleAddAsync(libUrl, "libs/lib");
                await sup.CommitAsync("add lib");
                superSeed.Git("remote", "add", "origin", superUrl); superSeed.Git("push", "-q", "-u", "origin", "main");
            }

            var (local, repo) = await TempRepo.CloneAsync(superUrl);
            await repo.SubmoduleUpdateAsync();
            return new World { Holder = holder, LibSeed = libSeed, Local = local, Super = repo };
        }

        public async Task<GitRepository> LibAsync() => (await GitRepository.OpenAsync(LibPath))!;

        public void Dispose()
        {
            Holder.Dispose();
            LibSeed.Dispose();
            Local.Dispose();
        }
    }

    [Fact]
    public async Task Create_branch_everywhere_checks_it_out_in_parent_and_submodule()
    {
        using var _ = new SubmoduleTests.FileTransportScope();
        using var w = await World.CreateAsync();
        var lib = await w.LibAsync();
        Assert.Null(await lib.GetCurrentBranchAsync()); // detached after a plain submodule update

        await w.Super.CreateBranchEverywhereAsync("feature/x");

        Assert.Equal("feature/x", await w.Super.GetCurrentBranchAsync());
        Assert.Equal("feature/x", await lib.GetCurrentBranchAsync());
    }

    [Fact]
    public async Task Create_branch_everywhere_changes_nothing_when_one_repository_already_has_it()
    {
        using var _ = new SubmoduleTests.FileTransportScope();
        using var w = await World.CreateAsync();
        var lib = await w.LibAsync();
        await lib.CreateBranchAsync("feature/x", checkout: false);

        var ex = await Assert.ThrowsAsync<SyncPreflightException>(() => w.Super.CreateBranchEverywhereAsync("feature/x"));

        Assert.Contains("libs/lib", ex.Message);
        Assert.Equal("main", await w.Super.GetCurrentBranchAsync());
        Assert.Null(await lib.GetCurrentBranchAsync());
    }

    [Fact]
    public async Task Align_attaches_submodule_to_remote_branch_and_reports_missing_ones()
    {
        using var _ = new SubmoduleTests.FileTransportScope();
        using var w = await World.CreateAsync();
        var lib = await w.LibAsync();

        var ex = await Assert.ThrowsAsync<SyncPreflightException>(() => w.Super.AlignSubmodulesToBranchAsync("dev"));
        Assert.Contains("no branch 'dev'", ex.Message);

        w.LibSeed.Git("push", "-q", "origin", "main:dev"); // same commit as the recorded one
        Assert.Equal("dev", await Align(w, lib));

        static async Task<string?> Align(World w, GitRepository lib)
        {
            await lib.FetchAsync();
            await w.Super.AlignSubmodulesToBranchAsync("dev");
            return await lib.GetCurrentBranchAsync();
        }
    }

    [Fact]
    public async Task Commit_and_push_everywhere_records_pointer_and_publishes_submodule_first()
    {
        using var _ = new SubmoduleTests.FileTransportScope();
        using var w = await World.CreateAsync();
        var lib = await w.LibAsync();
        await w.Super.CreateBranchEverywhereAsync("feature/x");
        File.WriteAllText(Path.Combine(w.LibPath, "a.txt"), "2\n");
        w.Local.Write("s.txt", "2\n");

        Assert.Empty(await w.Super.CheckCommitEverywhereAsync("sync work"));
        var committed = await w.Super.CommitEverywhereAsync("sync work");

        Assert.Equal(["libs/lib", "."], committed);
        Assert.Equal("sync work", (await lib.GetLogAsync(all: false))[0].Subject);
        Assert.Empty((await w.Super.GetStatusAsync()).Unstaged); // pointer recorded in the same step
        Assert.Equal(Core.Submodules.SubmoduleState.UpToDate, (await w.Super.GetSubmodulesAsync()).Single().State);

        var pushed = await w.Super.PushEverywhereAsync();

        Assert.Equal(["libs/lib", "."], pushed);
        w.Holder.Git("--git-dir", "lib.git", "rev-parse", "--verify", "refs/heads/feature/x");
        w.Holder.Git("--git-dir", "super.git", "rev-parse", "--verify", "refs/heads/feature/x");
        Assert.Empty(await w.Super.CheckPushEverywhereAsync()); // nothing left to push
    }

    [Fact]
    public async Task Commit_everywhere_refuses_a_detached_submodule_with_changes()
    {
        using var _ = new SubmoduleTests.FileTransportScope();
        using var w = await World.CreateAsync();
        File.WriteAllText(Path.Combine(w.LibPath, "a.txt"), "2\n");
        w.Local.Write("s.txt", "2\n");

        var problems = await w.Super.CheckCommitEverywhereAsync("msg");

        Assert.Contains(problems, p => p.Repo == "libs/lib" && p.Message.Contains("detached"));
        await Assert.ThrowsAsync<SyncPreflightException>(() => w.Super.CommitEverywhereAsync("msg"));
        Assert.Equal("2\n", Text(Path.Combine(w.Local.Path, "s.txt"))); // untouched
        Assert.Single((await w.Super.GetStatusAsync()).Unstaged, f => f.Path == "s.txt");
    }

    [Fact]
    public async Task Push_everywhere_pushes_nothing_when_one_repository_is_behind()
    {
        using var _ = new SubmoduleTests.FileTransportScope();
        using var w = await World.CreateAsync();
        await w.Super.CreateBranchEverywhereAsync("main2");
        await w.Super.CheckoutAsync("main");
        w.Local.Write("s.txt", "local\n"); await w.Super.StageAsync(["s.txt"]); await w.Super.CommitAsync("local only");

        // someone else advances the superproject remote
        var (other, o) = await TempRepo.CloneAsync(Path.Combine(w.Holder.Path, "super.git"));
        using (other)
        {
            other.Write("o.txt", "x\n"); await o.StageAsync(["o.txt"]); await o.CommitAsync("theirs");
            other.Git("push", "-q");
        }

        var problems = await w.Super.CheckPushEverywhereAsync();
        Assert.Contains(problems, p => p.Repo == "." && p.Message.Contains("behind"));
        await Assert.ThrowsAsync<SyncPreflightException>(() => w.Super.PushEverywhereAsync());
    }

    /// <summary>Advances both remotes: the library gets a commit on main, the superproject gets an unrelated one.</summary>
    private static async Task AdvanceRemotesAsync(World w)
    {
        w.LibSeed.Write("a.txt", "2\n");
        w.LibSeed.Git("add", "-A"); w.LibSeed.Git("commit", "-q", "-m", "lib 2"); w.LibSeed.Git("push", "-q");
        var (other, o) = await TempRepo.CloneAsync(Path.Combine(w.Holder.Path, "super.git"));
        using (other)
        {
            other.Write("o.txt", "x\n"); await o.StageAsync(["o.txt"]); await o.CommitAsync("theirs");
            other.Git("push", "-q");
        }
    }

    /// <summary>File content with line endings normalized (Windows checkouts may apply autocrlf).</summary>
    private static string Text(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

    private static async Task<string> Head(GitRepository r) => (await r.GetLogAsync(all: false))[0].Sha;

    private static async Task<GitRepository> OnMainAsync(World w)
    {
        await w.Super.CheckoutAsync("main");
        var lib = await w.LibAsync();
        await lib.CheckoutAsync("main");
        return lib;
    }

    [Fact]
    public async Task Pull_everywhere_keeps_wip_in_both_repositories_and_undo_restores_all()
    {
        using var _ = new SubmoduleTests.FileTransportScope();
        using var w = await World.CreateAsync();
        var lib = await OnMainAsync(w);
        Assert.Equal("main", (await w.Super.GetSubmoduleLiveStatusesAsync())["libs/lib"].Branch);
        var libBefore = await Head(lib);
        var superBefore = await Head(w.Super);
        await AdvanceRemotesAsync(w);
        File.WriteAllText(Path.Combine(w.LibPath, "wip.txt"), "lib wip\n");
        w.Local.Write("s.txt", "super wip\n");

        Assert.Empty(await w.Super.CheckPullEverywhereAsync(rebase: false));
        var steps = await w.Super.PullEverywhereAsync();

        Assert.Equal([("libs/lib", 1, SmartPullOutcome.Clean), (".", 1, SmartPullOutcome.Clean)], steps.Select(s => (s.Path, s.Commits, s.Outcome)));
        Assert.Equal("2\n", Text(Path.Combine(w.LibPath, "a.txt")));
        Assert.True(File.Exists(Path.Combine(w.Local.Path, "o.txt")));
        Assert.Equal("lib wip\n", Text(Path.Combine(w.LibPath, "wip.txt")));
        Assert.Equal("super wip\n", Text(Path.Combine(w.Local.Path, "s.txt")));
        Assert.Equal(2, (await w.Super.GetSyncStatesAsync()).Count);

        await w.Super.UndoEverywhereAsync();

        Assert.Equal(libBefore, await Head(lib));
        Assert.Equal(superBefore, await Head(w.Super));
        Assert.Equal("lib wip\n", Text(Path.Combine(w.LibPath, "wip.txt")));
        Assert.Equal("super wip\n", Text(Path.Combine(w.Local.Path, "s.txt")));
        Assert.False(File.Exists(Path.Combine(w.Local.Path, "o.txt")));
        Assert.Empty(await w.Super.GetSyncStatesAsync());
    }

    [Fact]
    public async Task Keep_everywhere_forgets_all_undo_points()
    {
        using var _ = new SubmoduleTests.FileTransportScope();
        using var w = await World.CreateAsync();
        await OnMainAsync(w);
        await AdvanceRemotesAsync(w);
        await w.Super.PullEverywhereAsync();

        await w.Super.KeepEverywhereAsync();

        Assert.Empty(await w.Super.GetSyncStatesAsync());
    }

    [Fact]
    public async Task Undo_everywhere_is_refused_when_one_repository_got_new_commits()
    {
        using var _ = new SubmoduleTests.FileTransportScope();
        using var w = await World.CreateAsync();
        var lib = await OnMainAsync(w);
        await AdvanceRemotesAsync(w);
        await w.Super.PullEverywhereAsync();
        File.WriteAllText(Path.Combine(w.LibPath, "later.txt"), "x\n");
        await lib.StageAsync(["later.txt"]); await lib.CommitAsync("later");

        var problems = await w.Super.CheckUndoEverywhereAsync();

        Assert.Equal(["libs/lib"], problems.Select(p => p.Repo));
        await Assert.ThrowsAsync<SyncPreflightException>(() => w.Super.UndoEverywhereAsync());
        Assert.Equal(2, (await w.Super.GetSyncStatesAsync()).Count); // nothing was touched
    }

    [Fact]
    public async Task Pull_everywhere_refuses_detached_repositories_and_changes_nothing()
    {
        using var _ = new SubmoduleTests.FileTransportScope();
        using var w = await World.CreateAsync();
        w.Local.Write("s.txt", "dirty\n");

        var problems = await w.Super.CheckPullEverywhereAsync(rebase: false);

        Assert.Contains(problems, p => p.Repo == "libs/lib" && p.Message.Contains("detached"));
        Assert.DoesNotContain(problems, p => p.Repo == ".");
        await Assert.ThrowsAsync<SyncPreflightException>(() => w.Super.PullEverywhereAsync());
        Assert.Equal("dirty\n", Text(Path.Combine(w.Local.Path, "s.txt")));
        Assert.Empty(await w.Super.GetSyncStatesAsync());
    }
}
