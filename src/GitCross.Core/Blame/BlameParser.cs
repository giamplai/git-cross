using System.Globalization;
using System.Text.RegularExpressions;

namespace GitCross.Core.Blame;

public sealed record BlameLine(int LineNumber, string Sha, string Author, DateTimeOffset Date, string Summary, string Text)
{
    public string ShortSha => Sha.Length > 8 ? Sha[..8] : Sha;
}

/// <summary>Parses <c>git blame --porcelain</c>: metadata is only emitted the first time a commit appears.</summary>
public static partial class BlameParser
{
    [GeneratedRegex(@"^([0-9a-f]{40}) \d+ (\d+)")]
    private static partial Regex Header();

    private sealed class Meta
    {
        public string Author = "";
        public DateTimeOffset Date = DateTimeOffset.UnixEpoch;
        public string Summary = "";
    }

    public static IReadOnlyList<BlameLine> Parse(string porcelain)
    {
        var metas = new Dictionary<string, Meta>();
        var result = new List<BlameLine>();
        string? sha = null;
        var finalLine = 0;

        foreach (var raw in porcelain.Split('\n'))
        {
            if (raw.StartsWith('\t'))
            {
                if (sha is not null && metas.TryGetValue(sha, out var m))
                {
                    result.Add(new BlameLine(finalLine, sha, m.Author, m.Date, m.Summary, raw[1..].TrimEnd('\r')));
                }

                continue;
            }

            if (Header().Match(raw) is { Success: true } h)
            {
                sha = h.Groups[1].Value;
                finalLine = int.Parse(h.Groups[2].Value, CultureInfo.InvariantCulture);
                if (!metas.ContainsKey(sha))
                {
                    metas[sha] = new Meta();
                }

                continue;
            }

            if (sha is null)
            {
                continue;
            }

            var meta = metas[sha];
            if (raw.StartsWith("author ", StringComparison.Ordinal))
            {
                meta.Author = raw[7..];
            }
            else if (raw.StartsWith("author-time ", StringComparison.Ordinal))
            {
                meta.Date = DateTimeOffset.FromUnixTimeSeconds(long.Parse(raw[12..], CultureInfo.InvariantCulture)).ToLocalTime();
            }
            else if (raw.StartsWith("summary ", StringComparison.Ordinal))
            {
                meta.Summary = raw[8..];
            }
        }

        return result;
    }
}
