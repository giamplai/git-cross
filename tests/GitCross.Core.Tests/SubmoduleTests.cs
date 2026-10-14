using GitCross.Core.Git;
using GitCross.Core.Submodules;

namespace GitCross.Core.Tests;

public class SubmoduleTests
{
    private const string Sha = "36a827c287fe147d0b589d2e666657fef28be6d0";

    [Fact]
    public void Parses_all_states_and_paths_with_spaces()
    {
        var cfg = SubmoduleParser.ParseModulesConfig("submodule.my lib.path\nlibs/my lib\0submodule.my lib.url\n../lib\0submodule.b.path\nb\0submodule.b.url\nhttps://x/b.git\0");
        var list = SubmoduleParser.ParseStatus(
            $" {Sha} libs/my lib (v1)\n-{Sha} b\n+{Sha} c (heads/main-2-gabc)\nU{Sha} d\n", cfg);

        Assert.Equal(["libs/my lib", "b", "c", "d"], list.Select(s => s.Path));
        Assert.Equal([SubmoduleState.UpToDate, SubmoduleState.NotInitialized, SubmoduleState.Modified, SubmoduleState.Conflicted], list.Select(s => s.State));
        Assert.Equal(("my lib", "../lib", "v1"), (list[0].Name, list[0].Url, list[0].Describe));
        Assert.Equal("https://x/b.git", list[1].Url);
        Assert.Null(list[1].Describe);
        Assert.False(list[1].IsInitialized);
        Assert.Equal("c", list[2].Name); // no config entry: falls back to the path
    }

    [Fact]
    public void Garbage_lines_are_ignored() => Assert.Empty(SubmoduleParser.ParseStatus("fatal: nope\n", new Dictionary<string, (string, string?)>()));

    /// <summary>Recent git refuses file:// submodule transports unless allowed; scope the exception to this test.</summary>
    private sealed class FileTransportScope : IDisposable
    {
        private readonly string?[] _old = new[] { "GIT_CONFIG_COUNT", "GIT_CONFIG_KEY_0", "GIT_CONFIG_VALUE_0" }.Select(Environment.GetEnvironmentVariable).ToArray();

        public FileTransportScope()
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_COUNT", "1");
            Environment.SetEnvironmentVariable("GIT_CONFIG_KEY_0", "protocol.file.allow");
            Environment.SetEnvironmentVariable("GIT_CONFIG_VALUE_0", "always");
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_COUNT", _old[0]);
            Environment.SetEnvironmentVariable("GIT_CONFIG_KEY_0", _old[1]);
            Environment.SetEnvironmentVariable("GIT_CONFIG_VALUE_0", _old[2]);
        }
    }

    [Fact]
    public async Task Add_modify_clone_and_update_submodule()
    {
        using var _ = new FileTransportScope();

        var (libT, lib) = await TempRepo.CreateAsync();
        var (superT, sup) = await TempRepo.CreateAsync();
        var cloneDirs = new List<string>();
        using (libT)
        using (superT)
        {
            libT.Write("a.txt", "1"); await lib.StageAsync(["a.txt"]); await lib.CommitAsync("lib 1");
            Assert.Empty(await sup.GetSubmodulesAsync()); // no .gitmodules yet

            await sup.SubmoduleAddAsync(libT.Path, "libs/lib");
            await sup.CommitAsync("add submodule");
            var sub = (await sup.GetSubmodulesAsync()).Single();
            Assert.Equal(("libs/lib", SubmoduleState.UpToDate), (sub.Path, sub.State));
            Assert.Equal(libT.Path.Replace('\\', '/'), sub.Url!.Replace('\\', '/'));

            // a new commit inside the submodule makes it differ from the recorded one
            var subRepo = (await GitRepository.OpenAsync(sup.SubmoduleFullPath(sub)))!;
            File.WriteAllText(Path.Combine(subRepo.Root, "b.txt"), "2");
            await subRepo.StageAsync(["b.txt"]);
            await subRepo.CommitAsync("lib 2");
            Assert.Equal(SubmoduleState.Modified, (await sup.GetSubmodulesAsync()).Single().State);

            // plain clone leaves the submodule empty; update fills it
            var target = Path.Combine(Path.GetTempPath(), "gxc-subclone-" + Guid.NewGuid().ToString("N")[..8]);
            cloneDirs.Add(target);
            var clone = await GitRepository.CloneAsync(superT.Path, target);
            Assert.Equal(SubmoduleState.NotInitialized, (await clone.GetSubmodulesAsync()).Single().State);
            await clone.SubmoduleUpdateAsync();
            Assert.Equal(SubmoduleState.UpToDate, (await clone.GetSubmodulesAsync()).Single().State);
            Assert.True(File.Exists(Path.Combine(target, "libs", "lib", "a.txt")));

            // recursive clone does both in one step
            var target2 = Path.Combine(Path.GetTempPath(), "gxc-subclone-" + Guid.NewGuid().ToString("N")[..8]);
            cloneDirs.Add(target2);
            var clone2 = await GitRepository.CloneAsync(superT.Path, target2, recursive: true);
            Assert.Equal(SubmoduleState.UpToDate, (await clone2.GetSubmodulesAsync()).Single().State);
        }

        foreach (var d in cloneDirs)
        {
            foreach (var f in new DirectoryInfo(d).EnumerateFiles("*", SearchOption.AllDirectories)) { f.Attributes = FileAttributes.Normal; }
            Directory.Delete(d, true);
        }
    }
}
