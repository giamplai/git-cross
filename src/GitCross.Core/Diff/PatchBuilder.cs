using System.Text;

using System.Globalization;
using System.Text.RegularExpressions;

namespace GitCross.Core.Diff;

/// <summary>What the partial patch will be applied to, which decides how unselected changes are treated.</summary>
public enum PartialMode
{
    /// <summary>Apply forward to the index (stage): unselected additions are skipped, unselected removals stay as context.</summary>
    Stage,

    /// <summary>Apply in reverse (unstage from the staged diff, or discard from the unstaged diff): unselected additions stay as context, unselected removals are skipped.</summary>
    Reverse,
}

/// <summary>Cuts a single hunk out of a one-file unified diff so it can be fed to <c>git apply</c>.</summary>
public static partial class PatchBuilder
{
    [GeneratedRegex(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@(.*)$")]
    private static partial Regex HunkHeader();

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

    /// <summary>
    /// Builds a patch containing only the selected changed lines. <paramref name="selection"/> maps a hunk index to the body
    /// indexes (see <see cref="DiffLine.BodyIndex"/>) of the lines to include. Returns null when no change is selected.
    /// </summary>
    public static string? BuildLinePatch(string fileDiff, IReadOnlyDictionary<int, ISet<int>> selection, PartialMode mode)
    {
        var (header, hunks) = Split(fileDiff);
        var sb = new StringBuilder();
        var any = false;

        for (var h = 0; h < hunks.Count; h++)
        {
            if (!selection.TryGetValue(h, out var chosen) || chosen.Count == 0)
            {
                continue;
            }

            var hunk = hunks[h];
            var m = HunkHeader().Match(hunk[0]);
            if (!m.Success)
            {
                continue;
            }

            var body = new List<string>();
            int oldCount = 0, newCount = 0;
            var keepNoNewlineMarker = false;
            var hasChange = false;

            for (var i = 1; i < hunk.Count; i++)
            {
                var line = hunk[i];
                var selected = chosen.Contains(i - 1);
                switch (line[0])
                {
                    case ' ':
                        body.Add(line); oldCount++; newCount++; keepNoNewlineMarker = true;
                        break;
                    case '+':
                        if (selected) { body.Add(line); newCount++; hasChange = true; keepNoNewlineMarker = true; }
                        else if (mode == PartialMode.Reverse) { body.Add(" " + line[1..]); oldCount++; newCount++; keepNoNewlineMarker = true; }
                        else { keepNoNewlineMarker = false; }
                        break;
                    case '-':
                        if (selected) { body.Add(line); oldCount++; hasChange = true; keepNoNewlineMarker = true; }
                        else if (mode == PartialMode.Stage) { body.Add(" " + line[1..]); oldCount++; newCount++; keepNoNewlineMarker = true; }
                        else { keepNoNewlineMarker = false; }
                        break;
                    case '\\':
                        if (keepNoNewlineMarker) { body.Add(line); }
                        break;
                }
            }

            if (!hasChange)
            {
                continue;
            }

            if (!any)
            {
                foreach (var l in header) { sb.Append(l).Append('\n'); }
            }

            any = true;
            var oldStart = m.Groups[1].Value;
            var newStart = m.Groups[2].Value;
            sb.Append(CultureInfo.InvariantCulture, $"@@ -{oldStart},{oldCount} +{newStart},{newCount} @@{m.Groups[3].Value}\n");
            foreach (var l in body) { sb.Append(l).Append('\n'); }
        }

        return any ? sb.ToString() : null;
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
