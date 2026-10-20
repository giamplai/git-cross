using GitCross.Core.Git;

namespace GitCross.Core.Tests;

public class TagTests
{
    private static async Task<(TempRepo T, GitRepository R)> TwoCommits()
    {
        var (t, r) = await TempRepo.CreateAsync();
        t.Write("a.txt", "1"); await r.StageAsync(["a.txt"]); await r.CommitAsync("c1");
        t.Write("a.txt", "2"); await r.StageAsync(["a.txt"]); await r.CommitAsync("c2");
        return (t, r);
    }

    [Fact]
    public async Task Message_makes_an_annotated_tag_on_the_chosen_commit()
    {
        var (t, r) = await TwoCommits();
        using (t)
        {
            var first = (await r.GetLogAsync(all: false))[1].Sha;
            await r.CreateTagAsync(new TagOptions("v1.0.0", first, "First release\n\nDetails"));
            await r.CreateTagAsync(new TagOptions("light"));

            var info = await r.GetTagInfoAsync();
            Assert.Equal(new TagInfo(true, "First release"), info["v1.0.0"]);
            Assert.Equal(new TagInfo(false, ""), info["light"]);
        }
    }

    [Fact]
    public async Task Validation_rejects_bad_names_duplicates_unsigned_messages_and_unknown_targets()
    {
        var (t, r) = await TwoCommits();
        using (t)
        {
            await r.CreateTagAsync(new TagOptions("v1"));
            Assert.Null(await r.ValidateTagAsync(new TagOptions("v2")));
            Assert.Contains("name", await r.ValidateTagAsync(new TagOptions("  ")));
            Assert.Contains("valid", await r.ValidateTagAsync(new TagOptions("bad name~")));
            Assert.Contains("already exists", await r.ValidateTagAsync(new TagOptions("v1")));
            Assert.Null(await r.ValidateTagAsync(new TagOptions("v1", Force: true)));
            Assert.Contains("message", await r.ValidateTagAsync(new TagOptions("s1", Sign: true)));
            Assert.Contains("not a commit", await r.ValidateTagAsync(new TagOptions("v3", "nope")));
        }
    }

    [Fact]
    public async Task Force_moves_an_existing_tag()
    {
        var (t, r) = await TwoCommits();
        using (t)
        {
            var log = await r.GetLogAsync(all: false);
            await r.CreateTagAsync(new TagOptions("v1", log[1].Sha));
            await Assert.ThrowsAsync<GitException>(() => r.CreateTagAsync(new TagOptions("v1", log[0].Sha)));
            await r.CreateTagAsync(new TagOptions("v1", log[0].Sha, Force: true));
            Assert.Equal(log[0].Sha, (await r.GetRefsAsync()).Single(x => x.Name == "v1").Sha);
        }
    }

    [Fact]
    public async Task Push_and_delete_tags_on_a_remote_and_delete_locally()
    {
        var (holder, _) = await TempRepo.CreateAsync();
        var (t, r) = await TwoCommits();
        using (holder)
        using (t)
        {
            holder.Git("init", "-q", "--bare", "remote.git");
            t.Git("remote", "add", "origin", Path.Combine(holder.Path, "remote.git"));
            Assert.Equal(["origin"], await r.GetRemotesAsync());
            await r.CreateTagAsync(new TagOptions("v1", Message: "rel"));

            await r.PushTagAsync("v1");
            holder.Git("--git-dir", "remote.git", "rev-parse", "--verify", "refs/tags/v1");

            await r.DeleteRemoteTagAsync("v1");
            await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() => holder.Git("--git-dir", "remote.git", "rev-parse", "--verify", "refs/tags/v1")));

            await r.DeleteTagAsync("v1");
            Assert.Empty(await r.GetTagInfoAsync());
        }
    }
}
