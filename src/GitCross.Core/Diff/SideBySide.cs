namespace GitCross.Core.Diff;

/// <summary>One row of the split view. Header rows (file header / hunk) span both columns.</summary>
public sealed record SplitRow(DiffLine? Header, DiffLine? Left, DiffLine? Right)
{
    public bool IsHeader => Header is not null;
}

public static class SideBySide
{
    /// <summary>Pairs removed lines (left) with added lines (right) in order; context goes on both sides.</summary>
    public static IReadOnlyList<SplitRow> Build(IReadOnlyList<DiffLine> lines)
    {
        var rows = new List<SplitRow>();
        var i = 0;
        while (i < lines.Count)
        {
            var l = lines[i];
            switch (l.Kind)
            {
                case DiffLineKind.Meta or DiffLineKind.Hunk:
                    rows.Add(new SplitRow(l, null, null));
                    i++;
                    break;
                case DiffLineKind.Context:
                    rows.Add(new SplitRow(null, l, l));
                    i++;
                    break;
                default:
                    var removed = new List<DiffLine>();
                    var added = new List<DiffLine>();
                    while (i < lines.Count && lines[i].Kind == DiffLineKind.Removed) { removed.Add(lines[i++]); }
                    while (i < lines.Count && lines[i].Kind == DiffLineKind.Added) { added.Add(lines[i++]); }
                    for (var k = 0; k < Math.Max(removed.Count, added.Count); k++)
                    {
                        rows.Add(new SplitRow(null, k < removed.Count ? removed[k] : null, k < added.Count ? added[k] : null));
                    }

                    break;
            }
        }

        return rows;
    }
}
