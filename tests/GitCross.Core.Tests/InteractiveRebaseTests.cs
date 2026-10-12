using GitCross.Core.Git;
using GitCross.Core.Models;
using GitCross.Core.Rebase;

namespace GitCross.Core.Tests;

public class InteractiveRebaseTests
{
    private static async Task<(TempRepo T, GitRepository R, string Base, IReadOnlyList<Commit> C)> Seed()
    {
        var (t, r) = await TempRepo.CreateAsync();
        t.Write("base.txt", "base"); await r.StageAsync(["base.txt"]); await r.CommitAsync("base");
        var baseSha = (await r.GetLogAsync())[0].Sha;
        foreach (var n in new[] { "c1", "c2", "c3", "c4" })
        {
            t.Write($"{n}.txt", n); await r.StageAsync([$"{n}.txt"]); await r.CommitAsync(n);
        }

        return (t, r, baseSha, await r.GetRebaseCandidatesAsync(baseSha));
    }

    private static RebaseTodoItem Item(Commit c, RebaseAction a = RebaseAction.Pick, string? msg = null) => new(a, c.Sha, c.Subject, msg);

    private static async Task<List<string>> Subjects(GitRepository r) => (await r.GetLogAsync(all: false)).Select(c => c.Subject).ToList();

    [Fact]
    public async Task Candidates_are_listed_oldest_first_and_exclude_the_base()
    {
        var (t, r, _, c) = await Seed();
        using (t) { Assert.Equal(["c1", "c2", "c3", "c4"], c.Select(x => x.Subject)); }
    }

    [Fact]
    public async Task Reorder_commits()
    {
        var (t, r, b, c) = await Seed();
        using (t)
        {
            await r.InteractiveRebaseAsync(b, [Item(c[2]), Item(c[0]), Item(c[1]), Item(c[3])]);
            Assert.Equal(["c4", "c2", "c1", "c3", "base"], await Subjects(r));
            Assert.Equal(GitOperation.None, await r.GetOperationAsync());
        }
    }

    [Fact]
    public async Task Drop_removes_the_commit_and_its_changes()
    {
        var (t, r, b, c) = await Seed();
        using (t)
        {
            await r.InteractiveRebaseAsync(b, [Item(c[0]), Item(c[1], RebaseAction.Drop), Item(c[2]), Item(c[3])]);
            Assert.Equal(["c4", "c3", "c1", "base"], await Subjects(r));
            Assert.False(File.Exists(Path.Combine(t.Path, "c2.txt")));
        }
    }

    [Fact]
    public async Task Squash_combines_messages_and_fixup_discards_the_message()
    {
        var (t, r, b, c) = await Seed();
        using (t)
        {
            await r.InteractiveRebaseAsync(b, [Item(c[0]), Item(c[1], RebaseAction.Squash), Item(c[2]), Item(c[3], RebaseAction.Fixup)]);
            var log = await r.GetLogAsync(all: false);
            Assert.Equal(3, log.Count);
            var squashed = await r.GetCommitMessageAsync(log[1].Sha);   // c1 + c2
            Assert.Contains("c1", squashed);
            Assert.Contains("c2", squashed);
            var fixedUp = await r.GetCommitMessageAsync(log[0].Sha);     // c3 (+ c4 content, message dropped)
            Assert.StartsWith("c3", fixedUp);
            Assert.DoesNotContain("c4", fixedUp);
            Assert.True(File.Exists(Path.Combine(t.Path, "c4.txt"))); // fixup keeps the content
        }
    }

    [Fact]
    public async Task Reword_and_squash_group_message_are_applied()
    {
        var (t, r, b, c) = await Seed();
        using (t)
        {
            await r.InteractiveRebaseAsync(b,
            [
                Item(c[0], RebaseAction.Reword, "Add the first feature\n\nWith a body paragraph."),
                Item(c[1], RebaseAction.Squash),
                Item(c[2]),
                Item(c[3], RebaseAction.Reword, "Final touches"),
            ]);
            var log = await r.GetLogAsync(all: false);
            Assert.Equal(["Final touches", "c3", "Add the first feature", "base"], log.Select(x => x.Subject));
            Assert.Contains("With a body paragraph.", await r.GetCommitMessageAsync(log[2].Sha));
            Assert.DoesNotContain("c2", await r.GetCommitMessageAsync(log[2].Sha)); // group message was replaced
            Assert.True(File.Exists(Path.Combine(t.Path, "c2.txt")));
        }
    }

    [Fact]
    public async Task Invalid_plans_are_rejected_before_git_runs()
    {
        var (t, r, b, c) = await Seed();
        using (t)
        {
            Assert.NotNull(InteractiveRebasePlanner.Validate([Item(c[0], RebaseAction.Squash), Item(c[1])]));
            Assert.NotNull(InteractiveRebasePlanner.Validate([Item(c[0], RebaseAction.Drop)]));
            Assert.NotNull(InteractiveRebasePlanner.Validate([Item(c[0], RebaseAction.Reword)])); // no message
            Assert.Null(InteractiveRebasePlanner.Validate([Item(c[0], RebaseAction.Drop), Item(c[1]), Item(c[2], RebaseAction.Squash)]));
            await Assert.ThrowsAsync<InvalidOperationException>(() => r.InteractiveRebaseAsync(b, [Item(c[0], RebaseAction.Fixup)]));
            Assert.Equal(["c4", "c3", "c2", "c1", "base"], await Subjects(r)); // untouched
        }
    }

    [Fact]
    public async Task Conflict_pauses_and_continue_still_applies_a_later_reword()
    {
        var (t, r) = await TempRepo.CreateAsync();
        using (t)
        {
            t.Write("f.txt", "0\n"); await r.StageAsync(["f.txt"]); await r.CommitAsync("base");
            var baseSha = (await r.GetLogAsync())[0].Sha;
            t.Write("f.txt", "1\n"); await r.StageAsync(["f.txt"]); await r.CommitAsync("one");
            t.Write("f.txt", "2\n"); await r.StageAsync(["f.txt"]); await r.CommitAsync("two");
            t.Write("g.txt", "g\n"); await r.StageAsync(["g.txt"]); await r.CommitAsync("gee");
            var c = await r.GetRebaseCandidatesAsync(baseSha);

            // swapping "one" and "two" conflicts on f.txt
            await Assert.ThrowsAsync<GitException>(() => r.InteractiveRebaseAsync(baseSha, [Item(c[1]), Item(c[0]), Item(c[2], RebaseAction.Reword, "Renamed gee")]));
            Assert.Equal(GitOperation.Rebase, await r.GetOperationAsync());

            // Both swapped commits touch f.txt, so each replay conflicts in turn.
            var rounds = 0;
            while (await r.GetOperationAsync() == GitOperation.Rebase && rounds++ < 5)
            {
                t.Write("f.txt", $"resolved{rounds}\n");
                await r.StageAsync(["f.txt"]);
                try
                {
                    await r.ContinueAsync(GitOperation.Rebase);
                }
                catch (GitException)
                {
                    // git reports the next conflicting commit as a failure of --continue; the loop condition
                    // (still Rebase) distinguishes that from a real error, which would end the loop with a wrong round count.
                }
            }

            Assert.Equal(2, rounds);

            Assert.Equal(GitOperation.None, await r.GetOperationAsync());
            Assert.Equal("Renamed gee", (await r.GetLogAsync(all: false))[0].Subject);
        }
    }
}
