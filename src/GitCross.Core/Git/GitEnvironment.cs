namespace GitCross.Core.Git;

/// <summary>Facts about the git installation and the user's global git configuration.</summary>
public static class GitEnvironment
{
    private static GitRunner Neutral => new(Path.GetTempPath());

    /// <summary>"git version 2.43.0" for a working executable (null/empty = the configured one), otherwise null.</summary>
    public static async Task<string?> ProbeAsync(string? executable = null)
    {
        try
        {
            var r = await new GitRunner(Path.GetTempPath(), string.IsNullOrWhiteSpace(executable) ? null : executable.Trim()).RunAsync("--version");
            return r.Success ? r.StdOut.Trim() : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return null;
        }
    }

    public static async Task<string> GetGlobalAsync(string key)
    {
        var r = await Neutral.RunAsync("config", "--global", "--get", key);
        return r.Success ? r.StdOut.Trim() : "";
    }

    /// <summary>Sets (or, for an empty value, unsets) a key in the user's global git config.</summary>
    public static async Task SetGlobalAsync(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            // Exit code 5 = key was not set; that is fine.
            await Neutral.RunAsync("config", "--global", "--unset", key);
            return;
        }

        (await Neutral.RunAsync("config", "--global", key, value.Trim())).OrThrow($"config --global {key}");
    }
}
