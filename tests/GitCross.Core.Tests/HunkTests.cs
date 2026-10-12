using GitCross.Core.Diff;

namespace GitCross.Core.Tests;

public class HunkTests
{
    private static string Lines(int n, Func<int, string> f) => string.Join("\n", Enumerable.Range(1, n).Select(f)) + "\n";

    private static async Task<(TempRepo T, Git.GitRepository R)> TwoHunkRepo()
    {
        var (t, r) = await TempRepo.CreateAsync();
        t.Write("f.txt", Lines(30, i => $"line{i}"));
        await r.StageAsync(["f.txt"]);
        await r.CommitAsync("base");
        // change line 2 and line 28 => two separate hunks
        t.Write("f.txt", Lines(30, i => i is 2 or 28 ? $"CHANGED{i}" : $"line{i}"));
        return (t, r);
    }

    [Fact]
    public async Task Hunk_count_and_indexes()
    {
        var (t, r) = await TwoHunkRepo();
        using (t)
        {
            var diff = await r.GetWorkingDiffAsync("f.txt", false);
            Assert.Equal(2, PatchBuilder.CountHunks(diff));
            var lines = DiffParser.Parse(diff);
            Assert.Equal([0, 1], lines.Where(l => l.Kind == DiffLineKind.Hunk).Select(l => l.HunkIndex));
            Assert.Null(PatchBuilder.BuildHunkPatch(diff, 2));
        }
    }

    [Fact]
    public async Task Stage_single_hunk_then_unstage_it()
    {
        var (t, r) = await TwoHunkRepo();
        using (t)
        {
            await r.StageHunkAsync("f.txt", 1, unstage: false);
            var staged = await r.GetWorkingDiffAsync("f.txt", true);
            Assert.Contains("+CHANGED28", staged);
            Assert.DoesNotContain("CHANGED2\n", staged);
            var unstaged = await r.GetWorkingDiffAsync("f.txt", false);
            Assert.Contains("+CHANGED2", unstaged);
            Assert.Equal(1, PatchBuilder.CountHunks(unstaged));

            // The staged diff only holds one hunk now, so index 0 refers to it.
            await r.StageHunkAsync("f.txt", 0, unstage: true);
            Assert.Equal(0, PatchBuilder.CountHunks(await r.GetWorkingDiffAsync("f.txt", true)));
            Assert.Equal(2, PatchBuilder.CountHunks(await r.GetWorkingDiffAsync("f.txt", false)));
        }
    }

    [Fact]
    public async Task Discard_hunk_restores_only_that_hunk()
    {
        var (t, r) = await TwoHunkRepo();
        using (t)
        {
            await r.DiscardHunkAsync("f.txt", 0);
            var text = File.ReadAllText(Path.Combine(t.Path, "f.txt"));
            Assert.Contains("line2\n", text);
            Assert.Contains("CHANGED28", text);
        }
    }
}
