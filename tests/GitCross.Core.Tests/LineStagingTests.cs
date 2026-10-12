using GitCross.Core.Diff;
using GitCross.Core.Git;

namespace GitCross.Core.Tests;

public class LineStagingTests
{
    private static string Lines(params string[] l) => string.Join("\n", l) + "\n";

    /// <summary>Selection of changed lines (added/removed) whose text is in <paramref name="texts"/>.</summary>
    private static Dictionary<int, ISet<int>> Pick(string diff, params string[] texts)
    {
        var map = new Dictionary<int, ISet<int>>();
        foreach (var l in DiffParser.Parse(diff).Where(l => l.Kind is DiffLineKind.Added or DiffLineKind.Removed && texts.Contains(l.Text)))
        {
            if (!map.TryGetValue(l.HunkIndex, out var set)) { map[l.HunkIndex] = set = new HashSet<int>(); }
            set.Add(l.BodyIndex);
        }

        return map;
    }

    private static async Task<(TempRepo T, GitRepository R)> Base()
    {
        var (t, r) = await TempRepo.CreateAsync();
        t.Write("f.txt", Lines("a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m", "n", "o", "p", "q", "r", "s", "t"));
        await r.StageAsync(["f.txt"]); await r.CommitAsync("base");
        return (t, r);
    }

    private static string Staged(TempRepo t) => RunGit(t, "show", ":f.txt");

    private static string RunGit(TempRepo t, params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = t.Path, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) { psi.ArgumentList.Add(a); }
        using var p = System.Diagnostics.Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return o.Replace("\r\n", "\n");
    }

    [Fact]
    public async Task Stage_only_one_of_three_added_lines()
    {
        var (t, r) = await Base();
        using (t)
        {
            t.Write("f.txt", File.ReadAllText(System.IO.Path.Combine(t.Path, "f.txt")).Replace("b\n", "b\nX\nY\nZ\n"));
            var diff = await r.GetWorkingDiffAsync("f.txt", false);
            await r.StageLinesAsync("f.txt", Pick(diff, "Y"), unstage: false);

            Assert.Contains("a\nb\nY\nc\n", Staged(t));
            Assert.DoesNotContain("X", Staged(t));
            var rest = await r.GetWorkingDiffAsync("f.txt", false);
            Assert.Contains("+X", rest);
            Assert.Contains("+Z", rest);
            Assert.DoesNotContain("+Y", rest);
        }
    }

    [Fact]
    public async Task Stage_only_the_removal_of_a_replaced_line()
    {
        var (t, r) = await Base();
        using (t)
        {
            t.Write("f.txt", File.ReadAllText(System.IO.Path.Combine(t.Path, "f.txt")).Replace("c\n", "C\n"));
            var diff = await r.GetWorkingDiffAsync("f.txt", false);
            await r.StageLinesAsync("f.txt", Pick(diff, "c"), unstage: false); // stage "-c" only

            var staged = Staged(t);
            Assert.DoesNotContain("\nc\n", staged);
            Assert.DoesNotContain("C", staged);
            Assert.Contains("+C", await r.GetWorkingDiffAsync("f.txt", false)); // the replacement is still unstaged
        }
    }

    [Fact]
    public async Task Unstage_a_single_line_from_a_fully_staged_change()
    {
        var (t, r) = await Base();
        using (t)
        {
            t.Write("f.txt", File.ReadAllText(System.IO.Path.Combine(t.Path, "f.txt")).Replace("b\n", "b\nX\nY\n"));
            await r.StageAsync(["f.txt"]);
            var staged = await r.GetWorkingDiffAsync("f.txt", true);
            await r.StageLinesAsync("f.txt", Pick(staged, "X"), unstage: true);

            Assert.DoesNotContain("X", Staged(t));
            Assert.Contains("b\nY\nc", Staged(t)); // Y stays staged
            Assert.Contains("X", File.ReadAllText(System.IO.Path.Combine(t.Path, "f.txt"))); // work tree untouched
        }
    }

    [Fact]
    public async Task Discard_one_line_keeps_the_others()
    {
        var (t, r) = await Base();
        using (t)
        {
            t.Write("f.txt", File.ReadAllText(System.IO.Path.Combine(t.Path, "f.txt")).Replace("b\n", "b\nX\nY\n"));
            var diff = await r.GetWorkingDiffAsync("f.txt", false);
            await r.DiscardLinesAsync("f.txt", Pick(diff, "X"));

            var text = File.ReadAllText(System.IO.Path.Combine(t.Path, "f.txt")).Replace("\r\n", "\n");
            Assert.Contains("b\nY\nc", text);
            Assert.DoesNotContain("X", text);
        }
    }

    [Fact]
    public async Task Selection_can_span_several_hunks()
    {
        var (t, r) = await Base();
        using (t)
        {
            var text = File.ReadAllText(System.IO.Path.Combine(t.Path, "f.txt")).Replace("b\n", "b\nTOP\n").Replace("s\n", "s\nBOTTOM\n");
            t.Write("f.txt", text);
            var diff = await r.GetWorkingDiffAsync("f.txt", false);
            var sel = Pick(diff, "TOP", "BOTTOM");
            Assert.Equal(2, sel.Count);
            await r.StageLinesAsync("f.txt", sel, unstage: false);
            Assert.Contains("TOP", Staged(t));
            Assert.Contains("BOTTOM", Staged(t));
        }
    }

    [Fact]
    public void Nothing_selected_builds_no_patch()
    {
        var diff = "diff --git a/f b/f\n--- a/f\n+++ b/f\n@@ -1,2 +1,3 @@\n a\n+x\n b\n";
        Assert.Null(PatchBuilder.BuildLinePatch(diff, new Dictionary<int, ISet<int>>(), PartialMode.Stage));
        Assert.Null(PatchBuilder.BuildLinePatch(diff, new Dictionary<int, ISet<int>> { [0] = new HashSet<int> { 0 } }, PartialMode.Stage)); // a context line is not a change
    }

    [Fact]
    public void Body_indexes_count_from_the_line_after_the_hunk_header()
    {
        var l = DiffParser.Parse("diff --git a/f b/f\n--- a/f\n+++ b/f\n@@ -1,2 +1,3 @@\n a\n+x\n b\n");
        Assert.Equal([0, 1, 2], l.Where(x => x.HunkIndex == 0 && x.Kind != DiffLineKind.Hunk).Select(x => x.BodyIndex));
    }
}
