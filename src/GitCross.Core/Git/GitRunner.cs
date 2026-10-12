using System.Diagnostics;
using System.Text;

namespace GitCross.Core.Git;

public sealed record GitResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;

    public string OrThrow(string command)
        => Success ? StdOut : throw new GitException(command, ExitCode, StdErr);
}

public sealed class GitException(string command, int exitCode, string stdErr)
    : Exception($"git {command} failed ({exitCode}): {stdErr.Trim()}")
{
    public int ExitCode { get; } = exitCode;
}

/// <summary>Runs the git executable. Works the same on Windows, Linux and macOS.</summary>
public sealed class GitRunner(string workingDirectory, string gitExecutable = "git")
{
    public string WorkingDirectory { get; } = workingDirectory;

    public async Task<GitResult> RunAsync(IEnumerable<string> args, string? stdin = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(gitExecutable)
        {
            WorkingDirectory = WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("core.quotepath=false");
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        // Never block on an interactive prompt we cannot answer.
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["LC_ALL"] = "C";

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Cannot start git");
        if (stdin is not null)
        {
            await p.StandardInput.WriteAsync(stdin);
            p.StandardInput.Close();
        }

        var outTask = p.StandardOutput.ReadToEndAsync(ct);
        var errTask = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return new GitResult(p.ExitCode, await outTask, await errTask);
    }

    public Task<GitResult> RunAsync(params string[] args) => RunAsync(args, null, default);

    public async Task<string> OutAsync(params string[] args)
        => (await RunAsync(args)).OrThrow(string.Join(' ', args));
}
