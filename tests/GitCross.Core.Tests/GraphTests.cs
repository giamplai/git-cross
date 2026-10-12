using GitCross.Core.Graph;
using GitCross.Core.Models;

namespace GitCross.Core.Tests;

public class GraphTests
{
    private static Commit C(string sha, params string[] parents)
        => new(sha, parents, "a", "a@x", DateTimeOffset.UnixEpoch, sha);

    [Fact]
    public void Linear_history_uses_one_lane()
    {
        var rows = GraphLayout.Build([C("c", "b"), C("b", "a"), C("a")]);
        Assert.All(rows, r => Assert.Equal(0, r.Lane));
    }

    [Fact]
    public void Merge_opens_second_lane_and_branch_rejoins()
    {
        // m merges b(feature) into a(main-line); both descend from r
        var rows = GraphLayout.Build([C("m", "a", "b"), C("a", "r"), C("b", "r"), C("r")]);
        Assert.Equal(0, rows[0].Lane);
        Assert.Equal(1, rows[2].Lane);
        Assert.Equal(0, rows[3].Lane);
        Assert.Contains(rows[3].Edges, e => e.From == 1 && e.To == 0);
    }
}
