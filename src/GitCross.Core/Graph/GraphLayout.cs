using GitCross.Core.Models;

namespace GitCross.Core.Graph;

/// <summary>A segment drawn in one row: from lane <see cref="From"/> at the top edge to lane <see cref="To"/> at the bottom edge.</summary>
public readonly record struct GraphEdge(int From, int To, int ColorIndex);

public sealed record GraphRow(Commit Commit, int Lane, int ColorIndex, IReadOnlyList<GraphEdge> Edges, int LaneCount, bool HasIncoming);

/// <summary>Assigns lanes to a topologically ordered commit list (as produced by <c>git log --topo-order</c>).</summary>
public static class GraphLayout
{
    public static List<GraphRow> Build(IReadOnlyList<Commit> commits)
    {
        var rows = new List<GraphRow>(commits.Count);
        // Each slot holds the sha of the commit the lane is waiting for (null = free) plus its color.
        var lanes = new List<(string? Sha, int Color)>();
        var nextColor = 0;

        foreach (var c in commits)
        {
            var laneIdx = lanes.FindIndex(l => l.Sha == c.Sha);
            var hasIncoming = laneIdx >= 0;
            int color;
            if (laneIdx < 0)
            {
                laneIdx = lanes.FindIndex(l => l.Sha is null);
                color = nextColor++;
                if (laneIdx < 0)
                {
                    lanes.Add((c.Sha, color));
                    laneIdx = lanes.Count - 1;
                }
                else
                {
                    lanes[laneIdx] = (c.Sha, color);
                }
            }
            else
            {
                color = lanes[laneIdx].Color;
            }

            var before = lanes.ToArray();
            var edges = new List<GraphEdge>();

            // Other lanes that also wait for this commit merge into this lane.
            for (var i = 0; i < lanes.Count; i++)
            {
                if (i != laneIdx && lanes[i].Sha == c.Sha)
                {
                    edges.Add(new GraphEdge(i, laneIdx, lanes[i].Color));
                    lanes[i] = (null, 0);
                }
            }

            // First parent continues in this lane; others get/join lanes.
            if (c.Parents.Count == 0)
            {
                lanes[laneIdx] = (null, 0);
            }
            else
            {
                lanes[laneIdx] = (c.Parents[0], color);
                for (var p = 1; p < c.Parents.Count; p++)
                {
                    var existing = lanes.FindIndex(l => l.Sha == c.Parents[p]);
                    if (existing >= 0)
                    {
                        edges.Add(new GraphEdge(laneIdx, existing, lanes[existing].Color));
                    }
                    else
                    {
                        var free = lanes.FindIndex(l => l.Sha is null);
                        var col = nextColor++;
                        if (free < 0)
                        {
                            lanes.Add((c.Parents[p], col));
                            free = lanes.Count - 1;
                        }
                        else
                        {
                            lanes[free] = (c.Parents[p], col);
                        }

                        edges.Add(new GraphEdge(laneIdx, free, col));
                    }
                }
            }

            // Pass-through lines for every lane that stays occupied.
            for (var i = 0; i < before.Length; i++)
            {
                if (i != laneIdx && before[i].Sha is not null && lanes[i].Sha == before[i].Sha)
                {
                    edges.Add(new GraphEdge(i, i, before[i].Color));
                }
            }

            // Continuation of this commit's own lane toward its first parent.
            if (c.Parents.Count > 0)
            {
                edges.Add(new GraphEdge(laneIdx, laneIdx, color));
            }

            while (lanes.Count > 0 && lanes[^1].Sha is null)
            {
                lanes.RemoveAt(lanes.Count - 1);
            }

            rows.Add(new GraphRow(c, laneIdx, color, edges, Math.Max(before.Length, lanes.Count) + 1, hasIncoming));
        }

        return rows;
    }
}
