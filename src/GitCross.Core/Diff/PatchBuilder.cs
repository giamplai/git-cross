using System.Text;

namespace GitCross.Core.Diff;

/// <summary>Cuts a single hunk out of a one-file unified diff so it can be fed to <c>git apply</c>.</summary>
public static class PatchBuilder
{
    public static int CountHunks(string fileDiff) => Split(fileDiff).Hunks.Count;

    /// <summary>Header + the chosen hunk, newline-terminated; null when the index is out of range.</summary>
    public static string? BuildHunkPatch(string fileDiff, int hunkIndex)
    {
        var (header, hunks) = Split(fileDiff);
        if (hunkIndex < 0 || hunkIndex >= hunks.Count)
        {
            return null;
        }

        var sb = new StringBuilder();
        foreach (var l in header) { sb.Append(l).Append('\n'); }
        foreach (var l in hunks[hunkIndex]) { sb.Append(l).Append('\n'); }
        return sb.ToString();
    }

    private static (List<string> Header, List<List<string>> Hunks) Split(string diff)
    {
        var header = new List<string>();
        var hunks = new List<List<string>>();
        foreach (var raw in diff.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                hunks.Add([line]);
            }
            else if (hunks.Count > 0)
            {
                // Empty lines are split artifacts: real context lines carry a leading space.
                if (line.Length > 0)
                {
                    hunks[^1].Add(line);
                }
            }
            else if (line.Length > 0)
            {
                header.Add(line);
            }
        }

        return (header, hunks);
    }
}
