using GitCross.Core.Git;
using GitCross.Core.Models;
using GitCross.Core.Tools;

namespace GitCross.Core.Tests;

public class ExternalToolTests
{
    private static bool Unix => !OperatingSystem.IsWindows();

    [Fact]
    public void Split_groups_quotes_and_keeps_windows_backslashes()
    {
        Assert.Equal(["collaterale", "--merge", "--base", "$BASE", "C:\\Program Files\\x.exe"],
            CommandTemplate.Split("collaterale --merge --base \"$BASE\" 'C:\\Program Files\\x.exe'"));
        Assert.Equal(["a", "", "b"], CommandTemplate.Split("a \"\" b"));
        Assert.Empty(CommandTemplate.Split("   "));
    }

    [Fact]
    public void Build_substitutes_after_splitting_so_special_characters_stay_one_argument()
    {
        var hostile = "/tmp/my file; rm -rf $(echo x) 'q'.txt";
        var (exe, args) = CommandTemplate.Build("tool --left \"$LOCAL\" $REMOTE", new Dictionary<string, string>
        {
            [CommandTemplate.Local] = hostile,
            [CommandTemplate.Remote] = "/r",
        })!.Value;
        Assert.Equal("tool", exe);
        Assert.Equal(["--left", hostile, "/r"], args);
        Assert.Null(CommandTemplate.Build("", new Dictionary<string, string>()));
    }

    [Fact]
    public void Unknown_variables_are_reported()
        => Assert.Equal(["$MERGE"], CommandTemplate.UnknownVariables("t $LOCAL $MERGE", [CommandTemplate.Local, CommandTemplate.Merged]));

    [Fact]
    public async Task Missing_executable_gives_a_message_not_an_exception()
    {
        var r = await ExternalTool.RunAsync("/definitely/not/a/tool $LOCAL", new Dictionary<string, string> { ["$LOCAL"] = "x" });
        Assert.False(r.Started);
        Assert.Contains("Could not start", r.StartError);
    }

    private static async Task<(TempRepo T, GitRepository R)> Conflicted(string file = "f.txt")
    {
        var (t, r) = await TempRepo.CreateAsync();
        t.Write(file, "base\n"); await r.StageAsync([file]); await r.CommitAsync("base");
        await r.CreateBranchAsync("topic");
        t.Write(file, "topic\n"); await r.StageAsync([file]); await r.CommitAsync("topic");
        await r.CheckoutAsync("main");
        t.Write(file, "main\n"); await r.StageAsync([file]); await r.CommitAsync("main");
        await Assert.ThrowsAsync<GitException>(() => r.MergeAsync("topic"));
        return (t, r);
    }

    private const string TakeBoth = "sh -c 'cat \"$1\" \"$2\" > \"$3\"' _ $LOCAL $REMOTE $MERGED";

    [Fact]
    public async Task Tool_result_is_copied_and_staged_when_it_exits_zero()
    {
        if (!Unix) { return; }
        var (t, r) = await Conflicted("sub dir; $(x)/we ird.txt");
        using (t)
        {
            var o = await r.ResolveWithToolAsync("sub dir; $(x)/we ird.txt", TakeBoth);
            Assert.True(o.Success, o.Message);
            Assert.Equal("main\ntopic\n", File.ReadAllText(r.ResolvePath("sub dir; $(x)/we ird.txt")).Replace("\r\n", "\n"));
            Assert.DoesNotContain((await r.GetStatusAsync()).Unstaged, f => f.Kind == ChangeKind.Conflicted);
            await r.ContinueAsync(GitOperation.Merge);
            Assert.Equal(GitOperation.None, await r.GetOperationAsync());
        }
    }

    [Fact]
    public async Task Tool_that_fails_leaves_the_conflict_untouched()
    {
        if (!Unix) { return; }
        var (t, r) = await Conflicted();
        using (t)
        {
            var before = File.ReadAllText(r.ResolvePath("f.txt"));
            var o = await r.ResolveWithToolAsync("f.txt", "sh -c 'echo partial > \"$1\"; exit 1' _ $MERGED");
            Assert.False(o.Success);
            Assert.Contains("still conflicted", o.Message);
            Assert.Equal(before, File.ReadAllText(r.ResolvePath("f.txt")));
            Assert.Contains((await r.GetStatusAsync()).Unstaged, f => f.Kind == ChangeKind.Conflicted);
        }
    }

    [Fact]
    public async Task Tool_that_exits_zero_without_writing_output_is_not_trusted()
    {
        if (!Unix) { return; }
        var (t, r) = await Conflicted();
        using (t)
        {
            var o = await r.ResolveWithToolAsync("f.txt", "sh -c 'exit 0'");
            Assert.False(o.Success);
            Assert.Contains((await r.GetStatusAsync()).Unstaged, f => f.Kind == ChangeKind.Conflicted);
        }
    }

    [Fact]
    public async Task Delete_modify_conflict_explains_why_a_tool_cannot_be_used()
    {
        var (t, r) = await TempRepo.CreateAsync();
        using (t)
        {
            t.Write("f.txt", "base\n"); await r.StageAsync(["f.txt"]); await r.CommitAsync("base");
            await r.CreateBranchAsync("topic");
            File.Delete(r.ResolvePath("f.txt")); await r.StageAsync(["f.txt"]); await r.CommitAsync("delete");
            await r.CheckoutAsync("main");
            t.Write("f.txt", "changed\n"); await r.StageAsync(["f.txt"]); await r.CommitAsync("modify");
            await Assert.ThrowsAsync<GitException>(() => r.MergeAsync("topic"));

            var o = await r.ResolveWithToolAsync("f.txt", TakeBoth);
            Assert.False(o.Success);
            Assert.Contains("deleted", o.Message);
        }
    }

    [Fact]
    public async Task Stage_extraction_is_binary_safe_and_reports_missing_stages()
    {
        var (t, r) = await Conflicted();
        using (t)
        {
            var path = Path.Combine(t.Path, "out.bin");
            Assert.True(await r.ExtractStageAsync("f.txt", GitRepository.ConflictSide.Base, path));
            Assert.Equal("base\n", File.ReadAllText(path).Replace("\r\n", "\n"));
            Assert.False(await r.ExtractStageAsync("nope.txt", GitRepository.ConflictSide.Ours, path));
        }

        var (t2, r2) = await TempRepo.CreateAsync();
        using (t2)
        {
            byte[] bytes = [0xFF, 0x00, 0xFE, 0x0D, 0x0A, 0x80, 0x00];
            File.WriteAllBytes(Path.Combine(t2.Path, "b.bin"), bytes);
            await r2.StageAsync(["b.bin"]); await r2.CommitAsync("bin");
            var o = Path.Combine(t2.Path, "copy.bin");
            Assert.True(await r2.ExtractBlobAsync("HEAD", "b.bin", o));
            Assert.Equal(bytes, File.ReadAllBytes(o));
        }
    }

    [Theory]
    [InlineData(GitRepository.DiffTarget.Unstaged, "index\n", "worktree\n")]
    [InlineData(GitRepository.DiffTarget.Staged, "head\n", "index\n")]
    [InlineData(GitRepository.DiffTarget.Commit, "head\n", "worktree\n")]
    public async Task Diff_tool_receives_the_right_pair_of_versions(GitRepository.DiffTarget target, string expectOld, string expectNew)
    {
        if (!Unix) { return; }
        var (t, r) = await TempRepo.CreateAsync();
        using (t)
        {
            t.Write("f.txt", "head\n"); await r.StageAsync(["f.txt"]); await r.CommitAsync("c1");
            string? sha = null;
            if (target == GitRepository.DiffTarget.Commit)
            {
                t.Write("f.txt", "worktree\n"); await r.StageAsync(["f.txt"]); await r.CommitAsync("c2");
                sha = (await r.GetLogAsync())[0].Sha;
            }
            else
            {
                t.Write("f.txt", "index\n"); await r.StageAsync(["f.txt"]);
                if (target == GitRepository.DiffTarget.Unstaged) { t.Write("f.txt", "worktree\n"); }
            }

            var outOld = Path.Combine(t.Path, "..", "o-" + Guid.NewGuid().ToString("N")[..6]);
            var outNew = outOld + "-new";
            var tool = $"sh -c 'cp \"$1\" \"$3\"; cp \"$2\" \"$4\"' _ $LOCAL $REMOTE {outOld} {outNew}";
            var res = await r.DiffWithToolAsync("f.txt", target, sha, tool);
            Assert.True(res.Success, res.Message);
            Assert.Equal(expectOld, File.ReadAllText(outOld).Replace("\r\n", "\n"));
            Assert.Equal(expectNew, File.ReadAllText(outNew).Replace("\r\n", "\n"));
            File.Delete(outOld); File.Delete(outNew);
        }
    }
}

/// <summary>Runs against the real Collaterale when it is installed (found on PATH, or env COLLATERALE_BIN); otherwise a no-op.</summary>
public class CollateraleIntegrationTests
{
    private static string? Collaterale()
    {
        var env = Environment.GetEnvironmentVariable("COLLATERALE_BIN");
        return !string.IsNullOrEmpty(env) && File.Exists(env) ? env : ExternalTool.LocateCollaterale();
    }

    [Fact]
    public void Locate_prefers_the_new_command_name_over_the_legacy_one()
    {
        var bin = Path.Combine(Path.GetTempPath(), "gxc-loc-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(bin);
        try
        {
            foreach (var n in new[] { "collate", "collaterale" }) { File.WriteAllText(Path.Combine(bin, n), ""); }
            Assert.Equal(Path.Combine(bin, "collaterale"), ExternalTool.LocateCollaterale(bin));
            File.Delete(Path.Combine(bin, "collaterale"));
            Assert.Equal(Path.Combine(bin, "collate"), ExternalTool.LocateCollaterale(bin));
            File.Delete(Path.Combine(bin, "collate"));
            Assert.Null(ExternalTool.LocateCollaterale(bin));
        }
        finally
        {
            Directory.Delete(bin, true);
        }
    }

    private static string Template(string exe)
        => $"\"{exe}\" --merge --base \"$BASE\" --left \"$LOCAL\" --right \"$REMOTE\" --output \"$MERGED\" /automerge";

    [Fact]
    public async Task Template_shape_works_with_the_real_tool_and_exit_zero_means_merged()
    {
        if (Collaterale() is not { } exe) { return; }
        var dir = Path.Combine(Path.GetTempPath(), "gxc-collaterale-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            string P(string n) => Path.Combine(dir, n);
            File.WriteAllText(P("base"), "a\nb\nc\n");
            File.WriteAllText(P("left"), "a\nB\nc\n");
            File.WriteAllText(P("right"), "a\nb\nc\nd\n"); // non-overlapping change
            var r = await ExternalTool.RunAsync(Template(exe), new Dictionary<string, string>
            {
                [CommandTemplate.Base] = P("base"), [CommandTemplate.Local] = P("left"),
                [CommandTemplate.Remote] = P("right"), [CommandTemplate.Merged] = P("out"),
            });
            Assert.True(r.Succeeded, $"exit {r.ExitCode} {r.StartError}");
            Assert.Equal("a\nB\nc\nd\n", File.ReadAllText(P("out")).Replace("\r\n", "\n"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Collaterale_that_finds_a_real_conflict_leaves_the_file_for_the_user()
    {
        if (Collaterale() is not { } exe) { return; }
        var (t, r) = await TempRepo.CreateAsync();
        using (t)
        {
            t.Write("f.txt", "base\n"); await r.StageAsync(["f.txt"]); await r.CommitAsync("base");
            await r.CreateBranchAsync("topic");
            t.Write("f.txt", "topic\n"); await r.StageAsync(["f.txt"]); await r.CommitAsync("topic");
            await r.CheckoutAsync("main");
            t.Write("f.txt", "main\n"); await r.StageAsync(["f.txt"]); await r.CommitAsync("main");
            await Assert.ThrowsAsync<GitException>(() => r.MergeAsync("topic"));

            var o = await r.ResolveWithToolAsync("f.txt", Template(exe));
            Assert.False(o.Success);
            Assert.Contains((await r.GetStatusAsync()).Unstaged, f => f.Kind == Models.ChangeKind.Conflicted);
        }
    }
}
