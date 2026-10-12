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
