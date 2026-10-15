using GitCross.Core.Git;
using GitCross.Core.Settings;

namespace GitCross.Core.Tests;

public class GitEnvironmentTests
{
    [Fact]
    public async Task Probe_finds_git_and_rejects_missing_executable()
    {
        Assert.StartsWith("git version", await GitEnvironment.ProbeAsync());
        Assert.Null(await GitEnvironment.ProbeAsync("/definitely/not/git-xyz"));
    }

    [Fact]
    public async Task Global_identity_roundtrip_uses_isolated_config_file()
    {
        var file = Path.Combine(Path.GetTempPath(), "gxc-gitconfig-" + Guid.NewGuid().ToString("N")[..8]);
        var old = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", file); // never touch the real ~/.gitconfig
        try
        {
            Assert.Equal("", await GitEnvironment.GetGlobalAsync("user.name"));
            await GitEnvironment.SetGlobalAsync("user.name", "  Ada Lovelace ");
            await GitEnvironment.SetGlobalAsync("user.email", "ada@example.com");
            Assert.Equal("Ada Lovelace", await GitEnvironment.GetGlobalAsync("user.name"));
            await GitEnvironment.SetGlobalAsync("user.name", "");
            Assert.Equal("", await GitEnvironment.GetGlobalAsync("user.name"));
            Assert.Equal("ada@example.com", await GitEnvironment.GetGlobalAsync("user.email"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", old);
            File.Delete(file);
        }
    }

    [Fact]
    public void New_settings_have_sensible_defaults_and_roundtrip()
    {
        var s = new AppSettings();
        Assert.Equal((2000, false, null), (s.MaxCommits, s.PullRebase, s.GitExecutable));
        var path = Path.Combine(Path.GetTempPath(), "gxc-" + Guid.NewGuid().ToString("N")[..8], "s.json");
        var store = new SettingsStore(path);
        store.Save(new AppSettings { GitExecutable = "/opt/git/bin/git", PullRebase = true, MaxCommits = 500 });
        var back = store.Load();
        Assert.Equal(("/opt/git/bin/git", true, 500), (back.GitExecutable, back.PullRebase, back.MaxCommits));
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }
}
