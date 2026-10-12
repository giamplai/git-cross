using GitCross.Core.Diff;

namespace GitCross.Core.Tests;

public class DiffParserTests
{
    private const string Sample =
        "diff --git a/a.txt b/a.txt\nindex 1..2 100644\n--- a/a.txt\n+++ b/a.txt\n@@ -1,3 +1,3 @@\n one\n-two\n+TWO\n three\n";

    [Fact]
    public void Numbers_lines_per_hunk()
    {
        var l = DiffParser.Parse(Sample);
        Assert.Equal(DiffLineKind.Hunk, l[4].Kind);
        Assert.Equal((1, 1), (l[5].OldNumber, l[5].NewNumber));
        Assert.Equal((2, null), (l[6].OldNumber, l[6].NewNumber));
        Assert.Equal((null, 2), (l[7].OldNumber, l[7].NewNumber));
        Assert.Equal((3, 3), (l[8].OldNumber, l[8].NewNumber));
        Assert.Equal(9, l.Count);
    }

    [Fact]
    public void Header_lines_are_meta_even_if_they_look_like_changes()
        => Assert.All(DiffParser.Parse(Sample).Take(4), x => Assert.Equal(DiffLineKind.Meta, x.Kind));

    [Fact]
    public void Resets_between_files_and_keeps_no_newline_marker_as_meta()
    {
        var l = DiffParser.Parse("diff --git a/x b/x\n--- a/x\n+++ b/x\n@@ -1 +1 @@\n-a\n\\ No newline at end of file\n+b\ndiff --git a/y b/y\n--- a/y\n+++ b/y\n@@ -5 +5 @@\n z\n");
        Assert.Equal(DiffLineKind.Meta, l[5].Kind);
        Assert.Equal((5, 5), (l[^1].OldNumber, l[^1].NewNumber));
    }

    [Fact]
    public void Empty_input_gives_no_lines() => Assert.Empty(DiffParser.Parse(""));
}

public class IntraLineAndSplitTests
{
    private static List<DiffLine> Hunk(params string[] body)
        => DiffParser.Parse("diff --git a/f b/f\n--- a/f\n+++ b/f\n@@ -1,9 +1,9 @@\n" + string.Join("\n", body) + "\n").ToList();

    [Fact]
    public void Edited_characters_are_located_inside_paired_lines()
    {
        var l = Hunk("-let total = price * 2;", "+let total = price * 3;");
        var (rem, add) = (l[4], l[5]);
        Assert.Equal((20, 1), (rem.ChangeStart, rem.ChangeLength)); // the "2"
        Assert.Equal((20, 1), (add.ChangeStart, add.ChangeLength)); // the "3"
        Assert.Equal("2", rem.Text.Substring(rem.ChangeStart, rem.ChangeLength));
    }

    [Fact]
    public void Insertion_in_the_middle_marks_only_the_new_characters()
    {
        var l = Hunk("-foo(bar)", "+foo(a, bar)");
        Assert.False(l[4].HasInlineChange); // nothing was removed from the old line
        Assert.Equal("a, ", l[5].Text.Substring(l[5].ChangeStart, l[5].ChangeLength));
    }

    [Fact]
    public void Completely_different_lines_are_not_partially_highlighted()
    {
        var l = Hunk("-aaaaaaaaaa", "+zzzzzzzzzz");
        Assert.All(l.Skip(4), x => Assert.False(x.HasInlineChange));
    }

    [Fact]
    public void Unpaired_surplus_lines_stay_unmarked()
    {
        var l = Hunk("-one", "-two", "+one!");
        Assert.False(l[4].HasInlineChange);   // "one" pairs with "one!": nothing removed from it
        Assert.False(l[5].HasInlineChange);   // "two" has no partner
        Assert.Equal("!", l[6].Text.Substring(l[6].ChangeStart, l[6].ChangeLength)); // only the appended "!" is marked
    }

    [Fact]
    public void Side_by_side_pairs_changes_and_duplicates_context()
    {
        var lines = Hunk(" keep", "-old1", "-old2", "+new1", " tail");
        var rows = SideBySide.Build(lines);
        Assert.Equal(4 + 1 + 2 + 1, rows.Count); // 4 header rows (3 meta + hunk), context, 2 change rows, context
        var change = rows.Where(r => !r.IsHeader && r.Left?.Kind == DiffLineKind.Removed).ToList();
        Assert.Equal("new1", change[0].Right!.Text);
        Assert.Null(change[1].Right); // surplus removal has an empty right cell
        var ctx = rows.First(r => r.Left?.Text == "keep");
        Assert.Same(ctx.Left, ctx.Right);
    }
}
