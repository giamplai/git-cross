using GitCross.Core.Diff;

namespace GitCross.Core.Conflicts;

/// <summary>Turns a file containing conflict markers into display lines: "ours" as removed, "theirs" as added.</summary>
public static class ConflictParser
{
    public static IReadOnlyList<DiffLine> Parse(string fileText)
    {
        var result = new List<DiffLine>();
        var section = 0; // 0 = outside, 1 = ours, 2 = theirs
        var no = 0;
        var hunk = -1;
        foreach (var raw in fileText.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            no++;
            if (line.StartsWith("<<<<<<<", StringComparison.Ordinal) && section == 0)
            {
                section = 1;
                result.Add(new DiffLine(DiffLineKind.Hunk, line, null, no, ++hunk));
            }
            else if (line.StartsWith("=======", StringComparison.Ordinal) && section == 1)
            {
                section = 2;
                result.Add(new DiffLine(DiffLineKind.Hunk, line, null, no, hunk));
            }
            else if (line.StartsWith(">>>>>>>", StringComparison.Ordinal) && section == 2)
            {
                section = 0;
                result.Add(new DiffLine(DiffLineKind.Hunk, line, null, no, hunk));
            }
            else
            {
                var kind = section switch { 1 => DiffLineKind.Removed, 2 => DiffLineKind.Added, _ => DiffLineKind.Context };
                result.Add(new DiffLine(kind, line, null, no, hunk));
            }
        }

        if (result.Count > 0 && result[^1] is { Text: "", Kind: DiffLineKind.Context })
        {
            result.RemoveAt(result.Count - 1); // artifact of the final newline
        }

        return result;
    }

    public static bool HasMarkers(string fileText)
        => fileText.Contains("\n<<<<<<<", StringComparison.Ordinal) || fileText.StartsWith("<<<<<<<", StringComparison.Ordinal);
}
