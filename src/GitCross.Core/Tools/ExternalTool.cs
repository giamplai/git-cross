using System.ComponentModel;
using System.Diagnostics;

namespace GitCross.Core.Tools;

public sealed record ToolRunResult(int ExitCode, string? StartError)
{
    public bool Started => StartError is null;
    public bool Succeeded => Started && ExitCode == 0;
}

public static class ExternalTool
{
    /// <summary>Ready-made merge/diff templates offered in Settings.</summary>
    public static IReadOnlyList<(string Name, string Merge, string Diff)> Presets { get; } =
    [
        ("Collate", "collate --merge --base \"$BASE\" --left \"$LOCAL\" --right \"$REMOTE\" --output \"$MERGED\"", "collate \"$LOCAL\" \"$REMOTE\""),
        ("Meld", "meld --output \"$MERGED\" \"$LOCAL\" \"$BASE\" \"$REMOTE\"", "meld \"$LOCAL\" \"$REMOTE\""),
        ("KDiff3", "kdiff3 \"$BASE\" \"$LOCAL\" \"$REMOTE\" -o \"$MERGED\"", "kdiff3 \"$LOCAL\" \"$REMOTE\""),
        ("VS Code", "code --wait --merge \"$LOCAL\" \"$REMOTE\" \"$BASE\" \"$MERGED\"", "code --wait --diff \"$LOCAL\" \"$REMOTE\""),
    ];

    /// <summary>Finds an executable by name on PATH (and the usual install folders). Null when absent.</summary>
    public static string? Locate(string name)
    {
        var exts = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", ".bat", "" } : [""];
        var dirs = new List<string>((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        dirs.AddRange([Path.Combine(home, ".local", "bin"), Path.Combine(home, ".local", "opt", name), "/opt/" + name, "/usr/local/bin", "/Applications/" + name + ".app/Contents/MacOS"]);
        foreach (var d in dirs)
        {
            foreach (var e in exts)
            {
                var f = Path.Combine(d, name + e);
                if (File.Exists(f))
                {
                    return f;
                }
            }
        }

        return null;
    }

    /// <summary>The program a template would start, resolved (null when it cannot be found).</summary>
    public static string? ResolveExecutable(string template)
    {
        var parts = CommandTemplate.Split(template);
        if (parts.Count == 0)
        {
            return null;
        }

        var exe = parts[0];
        return Path.IsPathRooted(exe) || exe.Contains(Path.DirectorySeparatorChar) ? (File.Exists(exe) ? exe : null) : Locate(exe);
    }

    public static async Task<ToolRunResult> RunAsync(string template, IReadOnlyDictionary<string, string> vars, CancellationToken ct = default)
    {
        var built = CommandTemplate.Build(template, vars);
        if (built is null)
        {
            return new ToolRunResult(-1, "No command is configured.");
        }

        var psi = new ProcessStartInfo(ResolveExecutable(template) is { } full && !Path.IsPathRooted(built.Value.Executable) ? full : built.Value.Executable)
        {
            UseShellExecute = false,
        };
        foreach (var a in built.Value.Args)
        {
            psi.ArgumentList.Add(a);
        }

        try
        {
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null");
            await p.WaitForExitAsync(ct);
            return new ToolRunResult(p.ExitCode, null);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return new ToolRunResult(-1, $"Could not start “{built.Value.Executable}”: {ex.Message}");
        }
    }
}
