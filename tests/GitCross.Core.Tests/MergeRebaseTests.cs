using GitCross.Core.Git;
using GitCross.Core.Models;

namespace GitCross.Core.Tests;

public class MergeRebaseTests
{
    /// <summary>main: base; topic (ahead of main by one commit adding t.txt). Currently on main.</summary>
    private static async Task<(TempRepo T, GitRepository R)> TopicAhead()
    {
        var (t, r) = await TempRepo.CreateAsync();
        t.Write("a.txt", "a"); await r.StageAsync(["a.txt"]); await r.CommitAsync("base");
        await r.CreateBranchAsync("topic");
        t.Write("t.txt", "t"); await r.StageAsync(["t.txt"]); await r.CommitAsync("topic work");
        await r.CheckoutAsync("main");
        return (t, r);
    }

    [Fact]
    public async Task Default_merge_fast_forwards_but_no_ff_creates_merge_commit()
    {
        var (t, r) = await TopicAhead();
        using (t)
        {
            await r.MergeAsync("topic", MergeMode.NoFastForward);
            Assert.Equal(2, (await r.GetLogAsync())[0].Parents.Count);
        }

        var (t2, r2) = await TopicAhead();
        using (t2)
        {
            await r2.MergeAsync("topic");
            Assert.Single((await r2.GetLogAsync())[0].Parents); // plain fast-forward
        }
    }

    [Fact]
    public async Task Squash_stages_changes_without_committing()
    {
        var (t, r) = await TopicAhead();
        using (t)
        {
            await r.MergeAsync("topic", MergeMode.Squash);
            Assert.Equal(GitOperation.None, await r.GetOperationAsync());
            Assert.Equal(["t.txt"], (await r.GetStatusAsync()).Staged.Select(f => f.Path));
            Assert.Single(await r.GetLogAsync(all: false)); // HEAD still at base: nothing committed
        }
    }

    [Fact]
    public async Task FastForwardOnly_refuses_diverged_branches()
    {
        var (t, r) = await TopicAhead();
        using (t)
        {
            t.Write("m.txt", "m"); await r.StageAsync(["m.txt"]); await r.CommitAsync("main work");
            await Assert.ThrowsAsync<GitException>(() => r.MergeAsync("topic", MergeMode.FastForwardOnly));
        }
    }

    [Fact]
    public async Task Rebase_replays_commits_linearly()
    {
        var (t, r) = await TopicAhead();
        using (t)
        {
            t.Write("m.txt", "m"); await r.StageAsync(["m.txt"]); await r.CommitAsync("main work");
            await r.CheckoutAsync("topic");
            await r.RebaseAsync("main");
            var log = await r.GetLogAsync();
            Assert.Equal(["topic work", "main work", "base"], log.Select(c => c.Subject));
            Assert.All(log, c => Assert.True(c.Parents.Count <= 1));
        }
    }

    [Fact]
    public async Task Rebase_conflict_is_recognised_and_can_continue()
    {
        var (t, r) = await TempRepo.CreateAsync();
        using (t)
        {
            t.Write("f.txt", "base\n"); await r.StageAsync(["f.txt"]); await r.CommitAsync("base");
            await r.CreateBranchAsync("topic");
            t.Write("f.txt", "topic\n"); await r.StageAsync(["f.txt"]); await r.CommitAsync("topic change");
            await r.CheckoutAsync("main");
            t.Write("f.txt", "main\n"); await r.StageAsync(["f.txt"]); await r.CommitAsync("main change");
            await r.CheckoutAsync("topic");

            await Assert.ThrowsAsync<GitException>(() => r.RebaseAsync("main"));
            Assert.Equal(GitOperation.Rebase, await r.GetOperationAsync());
            Assert.Equal(ChangeKind.Conflicted, (await r.GetStatusAsync()).Unstaged.Single().Kind);

            // During a rebase "ours" is the branch being rebased onto (main), "theirs" is the replayed commit.
            await r.ResolveAsync("f.txt", takeOurs: false);
            await r.ContinueAsync(GitOperation.Rebase);
            Assert.Equal(GitOperation.None, await r.GetOperationAsync());
            Assert.Equal("topic change", (await r.GetLogAsync())[0].Subject);
        }
    }
}
