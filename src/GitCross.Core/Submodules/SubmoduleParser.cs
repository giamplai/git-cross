using System.Text.RegularExpressions;

namespace GitCross.Core.Submodules;

public enum SubmoduleState
{
    /// <summary>Checked out at the commit the superproject records.</summary>
    UpToDate,

    /// <summary>Registered in .gitmodules but not cloned yet.</summary>
    NotInitialized,

    /// <summary>The checked-out commit differs from the one recorded in the superproject.</summary>
    Modified,

    /// <summary>Merge conflicts in the submodule entry.</summary>
    Conflicted,
}

public sealed record SubmoduleInfo(string Name, string Path, string? Url, string Sha, SubmoduleState State, string? Describe)
{
    public bool IsInitialized => State != SubmoduleState.NotInitialized;
}

public static partial class SubmoduleParser
{
    [GeneratedRegex(@"^(?<p>[ \-+U])(?<sha>[0-9a-f]{40}) (?<path>.+?)(?: \((?<desc>.*)\))?$")]
    private static partial Regex StatusLine();

    /// <summary>Parses <c>git submodule status</c>. <paramref name="config"/> maps path → (name, url) from .gitmodules.</summary>
    public static IReadOnlyList<SubmoduleInfo> ParseStatus(string output, IReadOnlyDictionary<string, (string Name, string? Url)> config)
    {
        var list = new List<SubmoduleInfo>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (StatusLine().Match(line.TrimEnd('\r')) is not { Success: true } m)
            {
                continue;
            }

            var path = m.Groups["path"].Value;
            var state = m.Groups["p"].Value[0] switch
            {
                '-' => SubmoduleState.NotInitialized,
                '+' => SubmoduleState.Modified,
                'U' => SubmoduleState.Conflicted,
                _ => SubmoduleState.UpToDate,
            };
            config.TryGetValue(path, out var c);
            var desc = m.Groups["desc"].Success ? m.Groups["desc"].Value : null;
            list.Add(new SubmoduleInfo(c.Name ?? path, path, c.Url, m.Groups["sha"].Value, state, desc));
        }

        return list;
    }

    /// <summary>Parses <c>git config -f .gitmodules -z --get-regexp "^submodule\..*\.(path|url)$"</c> into path → (name, url).</summary>
    public static Dictionary<string, (string Name, string? Url)> ParseModulesConfig(string output)
    {
        var paths = new Dictionary<string, string>(); // name -> path
        var urls = new Dictionary<string, string>();  // name -> url
        foreach (var rec in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var nl = rec.IndexOf('\n');
            if (nl < 0 || !rec.StartsWith("submodule.", StringComparison.Ordinal))
            {
                continue;
            }

            var key = rec[10..nl];
            var value = rec[(nl + 1)..];
            var dot = key.LastIndexOf('.');
            if (dot < 0)
            {
                continue;
            }

            var (name, prop) = (key[..dot], key[(dot + 1)..]);
            if (prop == "path") { paths[name] = value; }
            else if (prop == "url") { urls[name] = value; }
        }

        return paths.ToDictionary(kv => kv.Value, kv => (kv.Key, urls.GetValueOrDefault(kv.Key)));
    }
}
