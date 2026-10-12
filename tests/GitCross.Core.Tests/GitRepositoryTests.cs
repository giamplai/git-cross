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
