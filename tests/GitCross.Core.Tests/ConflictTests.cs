using GitCross.Core.Conflicts;
using GitCross.Core.Diff;
using GitCross.Core.Models;

namespace GitCross.Core.Tests;

public class ConflictTests
{
    /// <summary>main and topic both change line 1 of f.txt differently; checked out on main.</summary>
    private static async Task<(TempRepo T, Git.GitRepository R)> Diverged()
    {
        var (t, r) = await TempRepo.CreateAsync();
        t.Write("f.txt", "base\n"); await r.StageAsync(["f.txt"]); await r.CommitAsync("base");
        await r.CreateBranchAsync("topic");
        t.Write("f.txt", "topic\n"); await r.StageAsync(["f.txt"]); await r.CommitAsync("topic change");
        await r.CheckoutAsync("main");
        t.Write("f.txt", "main\n"); await r.StageAsync(["f.txt"]); await r.CommitAsync("main change");
        return (t, r);
    }

    [Fact]
    public void Parser_marks_ours_and_theirs()
    {
        var l = ConflictParser.Parse("a\n<<<<<<< HEAD\nours\n=======\ntheirs\n>>>>>>> topic\nz\n");
        Assert.Equal(
            [DiffLineKind.Context, DiffLineKind.Hunk, DiffLineKind.Removed, DiffLineKind.Hunk, DiffLineKind.Added, DiffLineKind.Hunk, DiffLineKind.Context],
            l.Select(x => x.Kind));
        Assert.Equal(7, l.Count);
        Assert.True(ConflictParser.HasMarkers("x\n<<<<<<< a\n"));
        Assert.False(ConflictParser.HasMarkers("clean"));
    }

    [Fact]
    public async Task Merge_conflict_resolve_theirs_and_continue()
    {
        var (t, r) = await Diverged();
        using (t)
        {
            await Assert.ThrowsAsync<Git.GitException>(() => r.MergeAsync("topic"));
            Assert.Equal(GitOperation.Merge, await r.GetOperationAsync());
            Assert.Equal(ChangeKind.Conflicted, (await r.GetStatusAsync()).Unstaged.Single().Kind);
            Assert.True(ConflictParser.HasMarkers(File.ReadAllText(r.ResolvePath("f.txt"))));

            await r.ResolveAsync("f.txt", takeOurs: false);
            Assert.Equal("topic\n", File.ReadAllText(r.ResolvePath("f.txt")).Replace("\r\n", "\n"));
            await r.ContinueAsync(GitOperation.Merge);

            Assert.Equal(GitOperation.None, await r.GetOperationAsync());
            Assert.Equal(2, (await r.GetLogAsync())[0].Parents.Count);
        }
    }

    [Fact]
    public async Task Cherry_pick_conflict_can_be_aborted()
    {
        var (t, r) = await Diverged();
        using (t)
        {
            var topicSha = (await r.GetRefsAsync()).Single(x => x.Name == "topic").Sha;
            await Assert.ThrowsAsync<Git.GitException>(() => r.CherryPickAsync(topicSha));
            Assert.Equal(GitOperation.CherryPick, await r.GetOperationAsync());
            await r.AbortAsync(GitOperation.CherryPick);
            Assert.Equal(GitOperation.None, await r.GetOperationAsync());
            Assert.Equal("main\n", File.ReadAllText(r.ResolvePath("f.txt")).Replace("\r\n", "\n"));
        }
    }
}
