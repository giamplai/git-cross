using GitCross.Core.Git;
using GitCross.Core.Models;

namespace GitCross.Core.Tests;

public class SmartPullTests
{
    /// <summary>origin (a.txt, b.txt) and a clone of it that is behind after origin commits <paramref name="upstream"/>.</summary>
    private static async Task<(TempRepo Origin, TempRepo Local, GitRepository R)> Behind(Action<TempRepo> upstream)
    {
        var (origin, o) = await TempRepo.CreateAsync();
        origin.Write("a.txt", "a\n"); origin.Write("b.txt", "b\n");
        await o.StageAsync(["a.txt", "b.txt"]); await o.CommitAsync("base");
        var (local, r) = await TempRepo.CloneAsync(origin.Path);
        upstream(origin);
        return (origin, local, r);
    }

    /// <summary>File content with line endings normalized (Windows checkouts may apply autocrlf).</summary>
    private static string Text(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

    private static void Commit(TempRepo t, string message)
    {
        t.Git("add", "-A");
        t.Git("commit", "-q", "-m", message);
    }

    [Fact]
    public async Task Pull_keeps_staged_unstaged_and_untracked_work_and_undo_restores_everything()
    {
        var (origin, local, r) = await Behind(o => { o.Write("up.txt", "up\n"); Commit(o, "upstream"); });
        using (origin) using (local)
        {
            var before = (await r.GetLogAsync(all: false))[0].Sha;
            local.Write("a.txt", "a\nstaged\n"); await r.StageAsync(["a.txt"]);
            local.Write("b.txt", "b\nunstaged\n");
            local.Write("new.txt", "untracked\n");

            Assert.Equal(SmartPullOutcome.Clean, await r.SmartPullAsync());

            Assert.True(File.Exists(Path.Combine(local.Path, "up.txt")));
            var s = await r.GetStatusAsync();
            Assert.Equal(["a.txt"], s.Staged.Select(f => f.Path));
            Assert.Equal(["b.txt", "new.txt"], s.Unstaged.Select(f => f.Path).Order());
            Assert.Empty(await r.GetStashesAsync());
            Assert.NotNull(await r.GetSmartPullStateAsync());

            await r.UndoSmartPullAsync();

            Assert.Equal(before, (await r.GetLogAsync(all: false))[0].Sha);
            Assert.False(File.Exists(Path.Combine(local.Path, "up.txt")));
            s = await r.GetStatusAsync();
            Assert.Equal(["a.txt"], s.Staged.Select(f => f.Path));
            Assert.Equal(["b.txt", "new.txt"], s.Unstaged.Select(f => f.Path).Order());
            Assert.Equal("a\nstaged\n", Text(Path.Combine(local.Path, "a.txt")));
            Assert.Null(await r.GetSmartPullStateAsync());
        }
    }

    [Fact]
    public async Task Clean_tree_still_records_an_undo_point()
    {
        var (origin, local, r) = await Behind(o => { o.Write("up.txt", "up\n"); Commit(o, "upstream"); });
        using (origin) using (local)
        {
            Assert.Equal(SmartPullOutcome.Clean, await r.SmartPullAsync());
            Assert.Null((await r.GetSmartPullStateAsync())!.WipSha);
            await r.UndoSmartPullAsync();
            Assert.False(File.Exists(Path.Combine(local.Path, "up.txt")));
        }
    }

    [Fact]
    public async Task Rebase_variant_replays_local_commits_and_keeps_work()
    {
        var (origin, local, r) = await Behind(o => { o.Write("up.txt", "up\n"); Commit(o, "upstream"); });
        using (origin) using (local)
        {
            local.Write("mine.txt", "m\n"); Commit(local, "mine");
            local.Write("b.txt", "b\nwip\n");
            Assert.Equal(SmartPullOutcome.Clean, await r.SmartPullAsync(rebase: true));
            var log = await r.GetLogAsync(all: false);
            Assert.Equal(["mine", "upstream", "base"], log.Select(c => c.Subject));
            Assert.Equal(["b.txt"], (await r.GetStatusAsync()).Unstaged.Select(f => f.Path));
        }
    }

    [Fact]
    public async Task Overlapping_change_reports_wip_conflicts_and_undo_still_works()
    {
        var (origin, local, r) = await Behind(o => { o.Write("a.txt", "from origin\n"); Commit(o, "upstream edits a"); });
        using (origin) using (local)
        {
            local.Write("a.txt", "from my wip\n");
            Assert.Equal(SmartPullOutcome.WipConflicts, await r.SmartPullAsync());
            Assert.Contains((await r.GetStatusAsync()).Unstaged, f => f.Kind == ChangeKind.Conflicted);

            await r.UndoSmartPullAsync();
            Assert.Equal("from my wip\n", Text(Path.Combine(local.Path, "a.txt")));
            Assert.DoesNotContain((await r.GetStatusAsync()).Unstaged, f => f.Kind == ChangeKind.Conflicted);
        }
    }

    [Fact]
    public async Task Conflicting_pull_parks_wip_until_restored_then_undo_goes_back()
    {
        var (origin, local, r) = await Behind(o => { o.Write("a.txt", "origin\n"); Commit(o, "upstream a"); });
        using (origin) using (local)
        {
            local.Write("a.txt", "local\n"); Commit(local, "local a");
            local.Write("b.txt", "b\nwip\n");
            var before = (await r.GetLogAsync(all: false))[0].Sha;

            Assert.Equal(SmartPullOutcome.PullConflicts, await r.SmartPullAsync());
            Assert.True((await r.GetSmartPullStateAsync())!.IsPending);
            await Assert.ThrowsAsync<InvalidOperationException>(() => r.DiscardSmartPullAsync());

            await r.ResolveAsync("a.txt", takeOurs: true);
            await r.ContinueAsync(GitOperation.Merge);
            Assert.Equal(SmartPullOutcome.Clean, await r.RestoreWipAsync());
            Assert.Equal(["b.txt"], (await r.GetStatusAsync()).Unstaged.Select(f => f.Path));

            await r.UndoSmartPullAsync();
            Assert.Equal(before, (await r.GetLogAsync(all: false))[0].Sha);
            Assert.Equal("b\nwip\n", Text(Path.Combine(local.Path, "b.txt")));
        }
    }

    [Fact]
    public async Task Undo_is_refused_after_new_commits_but_keeping_is_allowed()
    {
        var (origin, local, r) = await Behind(o => { o.Write("up.txt", "up\n"); Commit(o, "upstream"); });
        using (origin) using (local)
        {
            await r.SmartPullAsync();
            local.Write("later.txt", "x\n"); Commit(local, "later");
            await Assert.ThrowsAsync<InvalidOperationException>(() => r.UndoSmartPullAsync());
            await r.DiscardSmartPullAsync();
            Assert.Null(await r.GetSmartPullStateAsync());
        }
    }

    [Fact]
    public async Task Failing_pull_without_changes_restores_the_work_tree_and_rethrows()
    {
        var (t, r) = await TempRepo.CreateAsync();
        using (t)
        {
            t.Write("a.txt", "a\n"); await r.StageAsync(["a.txt"]); await r.CommitAsync("base");
            t.Write("a.txt", "a\nwip\n");
            await Assert.ThrowsAsync<GitException>(() => r.SmartPullAsync()); // no remote/upstream
            Assert.Equal("a\nwip\n", Text(Path.Combine(t.Path, "a.txt")));
            Assert.Null(await r.GetSmartPullStateAsync());
        }
    }
}
