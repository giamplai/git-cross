using GitCross.Core.Git;
using GitCross.Core.Models;

namespace GitCross.Core.Tests;

public class GitRepositoryTests
{
    [Fact]
    public async Task Stage_commit_and_read_back()
    {
        var (t, repo) = await TempRepo.CreateAsync();
        using (t)
        {
            t.Write("a.txt", "hello\n");
            var st = await repo.GetStatusAsync();
            Assert.Equal(ChangeKind.Untracked, st.Unstaged.Single().Kind);

            await repo.StageAsync(["a.txt"]);
            Assert.Single((await repo.GetStatusAsync()).Staged);
            await repo.CommitAsync("first");

            var log = await repo.GetLogAsync();
            Assert.Equal("first", log.Single().Subject);
            Assert.Equal("a.txt", (await repo.GetCommitFilesAsync(log[0].Sha)).Single().Path);
            Assert.Contains("+hello", await repo.GetCommitDiffAsync(log[0].Sha, "a.txt"));
        }
    }

    [Fact]
    public async Task Branches_tags_and_checkout()
    {
        var (t, repo) = await TempRepo.CreateAsync();
        using (t)
        {
            t.Write("a.txt", "1");
            await repo.StageAsync(["a.txt"]);
            await repo.CommitAsync("c1");
            await repo.CreateBranchAsync("feature");
            await repo.CreateTagAsync("v1");
            await repo.CheckoutAsync("main");

            var refs = await repo.GetRefsAsync();
            Assert.Equal("main", refs.Single(r => r.IsCurrent).Name);
            Assert.Contains(refs, r => r is { Name: "feature", Kind: RefKind.LocalBranch });
            Assert.Contains(refs, r => r is { Name: "v1", Kind: RefKind.Tag });
        }
    }

    [Fact]
    public async Task Open_returns_null_outside_repo()
        => Assert.Null(await Git.GitRepository.OpenAsync(Path.GetTempPath() + "/definitely-not-a-repo-xyz"));

    [Fact]
    public async Task Empty_repo_has_empty_log()
    {
        var (t, repo) = await TempRepo.CreateAsync();
        using (t) { Assert.Empty(await repo.GetLogAsync()); }
    }
}

public class CloneNamingTests
{
    [Theory]
    [InlineData("https://github.com/gitextensions/gitextensions.git", "gitextensions")]
    [InlineData("git@github.com:user/my-repo.git", "my-repo")]
    [InlineData("https://host/path/repo/", "repo")]
    [InlineData("/srv/git/local", "local")]
    public void Suggests_folder_name(string url, string expected)
        => Assert.Equal(expected, Git.GitRepository.SuggestFolderName(url));
}

public class HistoryEditingTests
{
    private static async Task<(TempRepo T, Git.GitRepository R, string[] Shas)> Seed()
    {
        var (t, r) = await TempRepo.CreateAsync();
        var shas = new List<string>();
        foreach (var n in new[] { "a", "b", "c" })
        {
            t.Write($"{n}.txt", n);
            await r.StageAsync([$"{n}.txt"]);
            await r.CommitAsync(n);
            shas.Add((await r.GetLogAsync())[0].Sha);
        }

        return (t, r, [.. shas]);
    }

    [Fact]
    public async Task Revert_adds_inverse_commit()
    {
        var (t, r, s) = await Seed();
        using (t)
        {
            await r.RevertAsync(s[2]);
            Assert.False(File.Exists(Path.Combine(t.Path, "c.txt")));
            Assert.StartsWith("Revert", (await r.GetLogAsync())[0].Subject);
        }
    }

    [Fact]
    public async Task Reset_hard_and_soft()
    {
        var (t, r, s) = await Seed();
        using (t)
        {
            await r.ResetAsync(s[1], ResetMode.Soft);
            Assert.Equal(2, (await r.GetLogAsync()).Count);
            Assert.Single((await r.GetStatusAsync()).Staged); // c.txt still staged
            await r.ResetAsync(s[0], ResetMode.Hard);
            Assert.False(File.Exists(Path.Combine(t.Path, "b.txt")));
        }
    }

    [Fact]
    public async Task CherryPick_applies_commit_from_other_branch()
    {
        var (t, r, s) = await Seed();
        using (t)
        {
            await r.CreateBranchAsync("side", s[0]);
            await r.CherryPickAsync(s[2]);
            Assert.True(File.Exists(Path.Combine(t.Path, "c.txt")));
            Assert.False(File.Exists(Path.Combine(t.Path, "b.txt")));
        }
    }
}

public class StashTests
{
    [Fact]
    public void Parses_stash_list()
    {
        var l = Parsers.ParseStashes("aaa\u001fWIP on main: 1 x\u001e\nbbb\u001fOn main: named\u001e\n").ToList();
        Assert.Equal(["stash@{0}", "stash@{1}"], l.Select(s => s.Ref));
        Assert.Equal("On main: named", l[1].Message);
    }

    [Fact]
    public async Task Stash_list_apply_drop_and_log_excludes_stash_commits()
    {
        var (t, r) = await TempRepo.CreateAsync();
        using (t)
        {
            t.Write("a.txt", "1"); await r.StageAsync(["a.txt"]); await r.CommitAsync("c1");
            t.Write("a.txt", "2");
            await r.StashPushAsync("my work");
            var list = await r.GetStashesAsync();
            Assert.Contains("my work", list.Single().Message);
            Assert.Single(await r.GetLogAsync()); // stash commits are not part of history

            await r.StashApplyAsync(list[0]);
            Assert.Equal("2", File.ReadAllText(Path.Combine(t.Path, "a.txt")));
            await r.StashDropAsync(list[0]);
            Assert.Empty(await r.GetStashesAsync());
        }
    }
}
