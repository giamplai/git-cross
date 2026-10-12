using System.Globalization;
using GitCross.Core.Models;

namespace GitCross.Core.Git;

public static class Parsers
{
    public const char FieldSep = '\u001f';
    public const char RecordSep = '\u001e';

    /// <summary>Format used with <c>git log --format</c>: sha, parents, name, email, unix-date, subject.</summary>
    public const string LogFormat = "%H%x1f%P%x1f%an%x1f%ae%x1f%at%x1f%s%x1e";

    public static IEnumerable<Commit> ParseLog(string output)
    {
        foreach (var rec in output.Split(RecordSep, StringSplitOptions.RemoveEmptyEntries))
        {
            var f = rec.Trim('\n', '\r').Split(FieldSep);
            if (f.Length < 6)
            {
                continue;
            }

            var parents = f[1].Length == 0 ? [] : f[1].Split(' ');
            yield return new Commit(f[0], parents, f[2], f[3],
                DateTimeOffset.FromUnixTimeSeconds(long.Parse(f[4], CultureInfo.InvariantCulture)).ToLocalTime(), f[5]);
        }
    }

    /// <summary>Parses <c>git for-each-ref --format=%(refname)%00%(objectname)%00%(*objectname)%00%(HEAD)</c>.</summary>
    public const string RefFormat = "%(refname)%00%(objectname)%00%(*objectname)%00%(HEAD)";

    public static IEnumerable<GitRef> ParseRefs(string output)
    {
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split('\0');
            if (f.Length < 4)
            {
                continue;
            }

            var full = f[0];
            var sha = f[2].Length > 0 ? f[2] : f[1]; // peel annotated tags
            if (full.StartsWith("refs/heads/", StringComparison.Ordinal))
            {
                yield return new GitRef(full["refs/heads/".Length..], full, RefKind.LocalBranch, sha, f[3] == "*");
            }
            else if (full.StartsWith("refs/remotes/", StringComparison.Ordinal))
            {
                if (!full.EndsWith("/HEAD", StringComparison.Ordinal))
                {
                    yield return new GitRef(full["refs/remotes/".Length..], full, RefKind.RemoteBranch, sha);
                }
            }
            else if (full.StartsWith("refs/tags/", StringComparison.Ordinal))
            {
                yield return new GitRef(full["refs/tags/".Length..], full, RefKind.Tag, sha);
            }
            else if (full.StartsWith("refs/stash", StringComparison.Ordinal))
            {
                yield return new GitRef("stash", full, RefKind.Stash, sha);
            }
        }
    }

    /// <summary>Parses <c>git status --porcelain=v1 -z --branch</c>.</summary>
    public static WorkingStatus ParseStatus(string output)
    {
        string? branch = null;
        int ahead = 0, behind = 0;
        List<FileChange> staged = [], unstaged = [];
        var parts = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var e = parts[i];
            if (e.StartsWith("## ", StringComparison.Ordinal))
            {
                ParseBranchLine(e[3..], out branch, out ahead, out behind);
                continue;
            }

            if (e.Length < 4)
            {
                continue;
            }

            char x = e[0], y = e[1];
            var path = e[3..];
            string? oldPath = null;
            if (x is 'R' or 'C')
            {
                oldPath = i + 1 < parts.Length ? parts[++i] : null;
            }

            if (x == '?' && y == '?')
            {
                unstaged.Add(new FileChange(path, ChangeKind.Untracked));
                continue;
            }

            if (x == 'U' || y == 'U' || (x == 'A' && y == 'A') || (x == 'D' && y == 'D'))
            {
                unstaged.Add(new FileChange(path, ChangeKind.Conflicted));
                continue;
            }

            if (x != ' ')
            {
                staged.Add(new FileChange(path, Kind(x), oldPath));
            }

            if (y != ' ')
            {
                unstaged.Add(new FileChange(path, Kind(y)));
            }
        }

        return new WorkingStatus(branch, ahead, behind, staged, unstaged);
    }

    private static void ParseBranchLine(string s, out string? branch, out int ahead, out int behind)
    {
        ahead = behind = 0;
        var bracket = s.IndexOf(" [", StringComparison.Ordinal);
        var head = bracket >= 0 ? s[..bracket] : s;
        var dots = head.IndexOf("...", StringComparison.Ordinal);
        branch = dots >= 0 ? head[..dots] : head;
        if (branch.StartsWith("No commits yet on ", StringComparison.Ordinal))
        {
            branch = branch["No commits yet on ".Length..];
        }
        else if (branch.StartsWith("HEAD (no branch)", StringComparison.Ordinal))
        {
            branch = null;
        }

        if (bracket < 0)
        {
            return;
        }

        foreach (var tok in s[(bracket + 2)..].TrimEnd(']').Split(", "))
        {
            if (tok.StartsWith("ahead ", StringComparison.Ordinal))
            {
                ahead = int.Parse(tok[6..], CultureInfo.InvariantCulture);
            }
            else if (tok.StartsWith("behind ", StringComparison.Ordinal))
            {
                behind = int.Parse(tok[7..], CultureInfo.InvariantCulture);
            }
        }
    }

    private static ChangeKind Kind(char c) => c switch
    {
        'A' => ChangeKind.Added,
        'D' => ChangeKind.Deleted,
        'R' => ChangeKind.Renamed,
        'C' => ChangeKind.Copied,
        'T' => ChangeKind.TypeChanged,
        _ => ChangeKind.Modified,
    };

    /// <summary>Parses <c>git diff-tree --name-status -z</c> / <c>git diff --name-status -z</c> output.</summary>
    public static IEnumerable<FileChange> ParseNameStatus(string output)
    {
        var p = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < p.Length; i++)
        {
            var code = p[i].Trim();
            if (code.Length == 0)
            {
                continue;
            }

            if (code[0] is 'R' or 'C' && i + 2 < p.Length)
            {
                yield return new FileChange(p[i + 2], Kind(code[0]), p[i + 1]);
                i += 2;
            }
            else if (i + 1 < p.Length)
            {
                yield return new FileChange(p[++i], Kind(code[0]));
            }
        }
    }
}
