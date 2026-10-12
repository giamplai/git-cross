using System.Globalization;
using System.Text.RegularExpressions;

namespace GitCross.Core.Diff;

public enum DiffLineKind { Meta, Hunk, Added, Removed, Context }

public sealed record DiffLine(DiffLineKind Kind, string Text, int? OldNumber, int? NewNumber);

/// <summary>Parses unified diff text (as printed by git) into numbered lines.</summary>
public static partial class DiffParser
{
    [GeneratedRegex(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@")]
    private static partial Regex HunkHeader();

    public static IReadOnlyList<DiffLine> Parse(string diff)
    {
        var result = new List<DiffLine>();
        int oldNo = 0, newNo = 0;
        var inHunk = false;

        foreach (var raw in diff.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("diff ", StringComparison.Ordinal))
            {
                inHunk = false;
            }

            if (HunkHeader().Match(line) is { Success: true } m)
            {
                oldNo = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                newNo = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                inHunk = true;
                result.Add(new DiffLine(DiffLineKind.Hunk, line, null, null));
            }
            else if (!inHunk || line.StartsWith('\\'))
            {
                if (line.Length > 0)
                {
                    result.Add(new DiffLine(DiffLineKind.Meta, line, null, null));
                }
            }
            else if (line.StartsWith('+'))
            {
                result.Add(new DiffLine(DiffLineKind.Added, line[1..], null, newNo++));
            }
            else if (line.StartsWith('-'))
            {
                result.Add(new DiffLine(DiffLineKind.Removed, line[1..], oldNo++, null));
            }
            else if (line.Length > 0)
            {
                // Context lines start with one space; empty lines are only split artifacts.
                result.Add(new DiffLine(DiffLineKind.Context, line[1..], oldNo++, newNo++));
            }
        }

        return result;
    }
}
