using System.Diagnostics;
using System.Text;

namespace GitCross.Core.Git;

public sealed record GitResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;

    public string OrThrow(string command)
        => Success ? StdOut : throw new GitException(command, ExitCode, StdErr.Trim().Length > 0 ? StdErr : StdOut);
}

public sealed class GitException(string command, int exitCode, string stdErr)
    : Exception($"git {command} failed ({exitCode}): {stdErr.Trim()}")
{
    public int ExitCode { get; } = exitCode;
}

/// <summary>Runs the git executable. Works the same on Windows, Linux and macOS.</summary>
public sealed class GitRunner(string workingDirectory, string? gitExecutable = null)
{
    /// <summary>Executable used when none is given (set from the user's settings; plain "git" resolves via PATH).</summary>
    public static string Executable { get; set; } = "git";

    public string WorkingDirectory { get; } = workingDirectory;

    public async Task<GitResult> RunAsync(IEnumerable<string> args, string? stdin = null, CancellationToken ct = default, IReadOnlyDictionary<string, string>? env = null)
    {
        var psi = new ProcessStartInfo(gitExecutable ?? Executable)
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
        // --continue and merges must never open an editor we cannot show.
        psi.Environment["GIT_EDITOR"] = "true";
        psi.Environment["GIT_MERGE_AUTOEDIT"] = "no";
        if (env is not null)
        {
            foreach (var (k, v) in env)
            {
                psi.Environment[k] = v;
            }
        }

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

    /// <summary>Runs git and returns stdout as raw bytes (file contents must not go through a text decoder).</summary>
    public async Task<(int ExitCode, byte[] Data, string StdErr)> RunBytesAsync(IEnumerable<string> args, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(gitExecutable ?? Executable)
        {
            WorkingDirectory = WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["LC_ALL"] = "C";
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Cannot start git");
        using var ms = new MemoryStream();
        var copy = p.StandardOutput.BaseStream.CopyToAsync(ms, ct);
        var err = p.StandardError.ReadToEndAsync(ct);
        await copy;
        await p.WaitForExitAsync(ct);
        return (p.ExitCode, ms.ToArray(), await err);
    }

    public Task<GitResult> RunAsync(params string[] args) => RunAsync(args, null, default);

    public async Task<string> OutAsync(params string[] args)
        => (await RunAsync(args)).OrThrow(string.Join(' ', args));
}
