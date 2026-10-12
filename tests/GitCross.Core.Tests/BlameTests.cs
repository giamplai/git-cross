using GitCross.Core.Blame;

namespace GitCross.Core.Tests;

public class BlameTests
{
    private const string Sha1 = "1111111111111111111111111111111111111111";
    private const string Sha2 = "2222222222222222222222222222222222222222";

    [Fact]
    public void Parses_groups_and_reuses_metadata_for_repeated_commits()
    {
        var text =
            $"{Sha1} 1 1 2\nauthor Ann\nauthor-time 1700000000\nsummary first\nfilename f\n\tone\n" +
            $"{Sha1} 2 2\n\ttwo\n" +
            $"{Sha2} 3 3 1\nauthor Bob\nauthor-time 1700001000\nsummary second\nfilename f\n\tthree\n";
        var l = BlameParser.Parse(text);
        Assert.Equal(3, l.Count);
        Assert.Equal(["Ann", "Ann", "Bob"], l.Select(x => x.Author));
        Assert.Equal(["one", "two", "three"], l.Select(x => x.Text));
        Assert.Equal(2, l[1].LineNumber);
        Assert.Equal("second", l[2].Summary);
    }

    [Fact]
    public async Task Blame_and_file_history_on_real_repo()
    {
        var (t, r) = await TempRepo.CreateAsync();
        using (t)
        {
            t.Write("f.txt", "a\nb\n"); await r.StageAsync(["f.txt"]); await r.CommitAsync("first");
            t.Write("f.txt", "a\nB\n"); await r.StageAsync(["f.txt"]); await r.CommitAsync("second");
            t.Write("other.txt", "x"); await r.StageAsync(["other.txt"]); await r.CommitAsync("unrelated");

            var blame = await r.GetBlameAsync("f.txt");
            Assert.Equal(["first", "second"], blame.Select(b => b.Summary));
            Assert.Equal("Test", blame[0].Author);

            var history = await r.GetFileLogAsync("f.txt");
            Assert.Equal(["second", "first"], history.Select(c => c.Subject));
        }
    }
}
