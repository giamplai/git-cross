using System.Globalization;
using System.Text.RegularExpressions;

namespace GitCross.Core.Diff;

public enum DiffLineKind { Meta, Hunk, Added, Removed, Context }

/// <param name="BodyIndex">Zero-based position inside its hunk body (first line after the <c>@@</c> header = 0); -1 for headers. Used to address single lines for partial staging.</param>
/// <param name="ChangeStart">Start of the edited characters inside <c>Text</c> (changed lines paired with their counterpart only).</param>
/// <param name="ChangeLength">Number of edited characters; 0 = highlight nothing extra.</param>
/// <param name="HunkIndex">Zero-based hunk this line belongs to (hunk header included); -1 for file header lines.</param>
public sealed record DiffLine(DiffLineKind Kind, string Text, int? OldNumber, int? NewNumber, int HunkIndex = -1, int ChangeStart = 0, int ChangeLength = 0, int BodyIndex = -1)
{
    /// <summary>True when part of the line (not all of it) was edited, as found by intra-line comparison.</summary>
    public bool HasInlineChange => ChangeLength > 0;

    public bool IsHunk => Kind == DiffLineKind.Hunk;
}

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
        var hunk = -1;
        var body = 0;

        foreach (var raw in diff.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("diff ", StringComparison.Ordinal))
            {
                inHunk = false;
                hunk = -1;
            }

            if (HunkHeader().Match(line) is { Success: true } m)
            {
                oldNo = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                newNo = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                inHunk = true;
                body = 0;
                result.Add(new DiffLine(DiffLineKind.Hunk, line, null, null, ++hunk));
            }
            else if (!inHunk || line.StartsWith('\\'))
            {
                if (line.Length > 0)
                {
                    result.Add(new DiffLine(DiffLineKind.Meta, line, null, null, hunk, BodyIndex: inHunk ? body++ : -1));
                }
            }
            else if (line.StartsWith('+'))
            {
                result.Add(new DiffLine(DiffLineKind.Added, line[1..], null, newNo++, hunk, BodyIndex: body++));
            }
            else if (line.StartsWith('-'))
            {
                result.Add(new DiffLine(DiffLineKind.Removed, line[1..], oldNo++, null, hunk, BodyIndex: body++));
            }
            else if (line.Length > 0)
            {
                // Context lines start with one space; empty lines are only split artifacts.
                result.Add(new DiffLine(DiffLineKind.Context, line[1..], oldNo++, newNo++, hunk, BodyIndex: body++));
            }
        }

        return IntraLine.Annotate(result);
    }
}
