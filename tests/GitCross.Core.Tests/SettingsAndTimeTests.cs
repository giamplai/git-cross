using GitCross.Core.Settings;
using GitCross.Core.Util;

namespace GitCross.Core.Tests;

public class SettingsAndTimeTests
{
    [Fact]
    public void Recent_is_mru_deduplicated_and_capped()
    {
        var s = new AppSettings();
        for (var i = 0; i < 20; i++) { s.AddRecent($"/r{i}"); }
        s.AddRecent("/r5");
        Assert.Equal("/r5", s.RecentRepositories[0]);
        Assert.Equal(AppSettings.MaxRecent, s.RecentRepositories.Count);
        Assert.Single(s.RecentRepositories, "/r5");
    }

    [Fact]
    public void Store_roundtrips_and_survives_corruption()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gxc-" + Guid.NewGuid().ToString("N")[..8]);
        var store = new SettingsStore(Path.Combine(dir, "s.json"));
        Assert.Empty(store.Load().RecentRepositories);

        var s = new AppSettings { Theme = "Dark" };
        s.AddRecent("/x");
        store.Save(s);
        var back = store.Load();
        Assert.Equal("Dark", back.Theme);
        Assert.Equal(["/x"], back.RecentRepositories);

        File.WriteAllText(Path.Combine(dir, "s.json"), "{ not json");
        Assert.Equal("System", store.Load().Theme);
        Directory.Delete(dir, true);
    }

    [Theory]
    [InlineData(30, "just now")]
    [InlineData(5 * 60, "5 min ago")]
    [InlineData(3 * 3600, "3 h ago")]
    [InlineData(86400, "1 day ago")]
    [InlineData(10 * 86400, "10 days ago")]
    [InlineData(65 * 86400, "2 months ago")]
    public void Relative_time(int secondsAgo, string expected)
    {
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(expected, RelativeTime.Format(now.AddSeconds(-secondsAgo), now));
    }

    [Theory]
    [InlineData("Ada Lovelace", "AL")]
    [InlineData("octocat", "O")]
    [InlineData("", "?")]
    public void Initials(string name, string expected) => Assert.Equal(expected, RelativeTime.Initials(name));
}
