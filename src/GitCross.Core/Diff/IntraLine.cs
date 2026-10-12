namespace GitCross.Core.Diff;

/// <summary>Finds the edited characters inside paired removed/added lines (common prefix and suffix are unchanged).</summary>
public static class IntraLine
{
    /// <summary>Lines that share less than this fraction of the shorter line are considered rewritten, not edited.</summary>
    private const double MinSimilarity = 0.3;

    public static IReadOnlyList<DiffLine> Annotate(List<DiffLine> lines)
    {
        var i = 0;
        while (i < lines.Count)
        {
            if (lines[i].Kind != DiffLineKind.Removed)
            {
                i++;
                continue;
            }

            var rs = i;
            while (i < lines.Count && lines[i].Kind == DiffLineKind.Removed) { i++; }
            var addStart = i;
            while (i < lines.Count && lines[i].Kind == DiffLineKind.Added) { i++; }

            var pairs = Math.Min(addStart - rs, i - addStart);
            for (var k = 0; k < pairs; k++)
            {
                var (a, b) = (lines[rs + k], lines[addStart + k]);
                var (ra, rb) = Compare(a.Text, b.Text);
                if (ra.Length > 0 || rb.Length > 0)
                {
                    lines[rs + k] = a with { ChangeStart = ra.Start, ChangeLength = ra.Length };
                    lines[addStart + k] = b with { ChangeStart = rb.Start, ChangeLength = rb.Length };
                }
            }
        }

        return lines;
    }

    private static (Range A, Range B) Compare(string a, string b)
    {
        var max = Math.Min(a.Length, b.Length);
        var prefix = 0;
        while (prefix < max && a[prefix] == b[prefix]) { prefix++; }

        var suffix = 0;
        while (suffix < max - prefix && a[^(suffix + 1)] == b[^(suffix + 1)]) { suffix++; }

        var none = (new Range(0, 0), new Range(0, 0));
        if (max == 0 || (double)(prefix + suffix) / max < MinSimilarity)
        {
            return none;
        }

        return (new Range(prefix, a.Length - prefix - suffix), new Range(prefix, b.Length - prefix - suffix));
    }

    private readonly record struct Range(int Start, int Length);
}
