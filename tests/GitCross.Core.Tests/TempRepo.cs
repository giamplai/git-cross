using System.Diagnostics;
using GitCross.Core.Git;

namespace GitCross.Core.Tests;

public sealed class TempRepo : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gxc-" + Guid.NewGuid().ToString("N")[..8]);

    private TempRepo() { }

    public static async Task<(TempRepo Temp, GitRepository Repo)> CreateAsync()
    {
        var t = new TempRepo();
        var repo = await GitRepository.InitAsync(t.Path);
        t.Git("config", "user.name", "Test");
        t.Git("config", "user.email", "t@example.com");
        t.Git("checkout", "-q", "-b", "main");
        return (t, repo);
    }

    public void Write(string rel, string content)
    {
        var full = System.IO.Path.Combine(Path, rel);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    public void Git(params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = Path, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) { psi.ArgumentList.Add(a); }
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        if (p.ExitCode != 0) { throw new InvalidOperationException($"git {string.Join(' ', args)}: {p.StandardError.ReadToEnd()}"); }
    }

    public void Dispose()
    {
        try
        {
            foreach (var f in new DirectoryInfo(Path).EnumerateFiles("*", SearchOption.AllDirectories)) { f.Attributes = FileAttributes.Normal; }
            Directory.Delete(Path, true);
        }
        catch (IOException) { }
    }
}
