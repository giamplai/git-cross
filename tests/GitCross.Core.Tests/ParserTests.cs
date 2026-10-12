using GitCross.Core.Git;
using GitCross.Core.Models;

namespace GitCross.Core.Tests;

public class ParserTests
{
    [Fact]
    public void Status_parses_branch_ahead_behind_and_changes()
    {
        var s = Parsers.ParseStatus("## main...origin/main [ahead 2, behind 1]\0M  a.txt\0 M b.txt\0?? c.txt\0R  new.txt\0old.txt\0UU conflict.txt\0");
        Assert.Equal("main", s.Branch);
        Assert.Equal((2, 1), (s.Ahead, s.Behind));
        Assert.Equal(["a.txt", "new.txt"], s.Staged.Select(f => f.Path));
        Assert.Equal("old.txt", s.Staged[1].OldPath);
        Assert.Equal([ChangeKind.Modified, ChangeKind.Untracked, ChangeKind.Conflicted], s.Unstaged.Select(f => f.Kind));
    }

    [Fact]
    public void Status_on_unborn_branch()
        => Assert.Equal("main", Parsers.ParseStatus("## No commits yet on main\0").Branch);

    [Fact]
    public void NameStatus_handles_renames()
    {
        var l = Parsers.ParseNameStatus("M\0a.txt\0R100\0old\0new\0A\0z\0").ToList();
        Assert.Equal(3, l.Count);
        Assert.Equal(new FileChange("new", ChangeKind.Renamed, "old"), l[1]);
        Assert.Equal("z", l[2].Path);
    }

    [Fact]
    public void Log_parses_records()
    {
        var c = Parsers.ParseLog("abc\u001fp1 p2\u001fAnn\u001fa@x\u001f1700000000\u001fhello\u001e\n").Single();
        Assert.Equal(["p1", "p2"], c.Parents);
        Assert.Equal("hello", c.Subject);
    }
}
