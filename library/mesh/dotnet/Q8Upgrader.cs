using System.Collections.Generic;
using System.Windows;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Converts a Q4 (4-node linear quadrilateral) mesh to a Q8 (8-node serendipity quadrilateral)
/// mesh by inserting one midside node at the midpoint of every quad edge.
/// Edges shared between adjacent quads use the SAME midside node index, so the upgraded mesh
/// remains conforming with no duplicate interface nodes (mirrors <see cref="T6Upgrader"/>).
/// </summary>
public static class Q8Upgrader
{
    /// <summary>
    /// Upgrade a Q4 mesh to Q8 in-place.
    /// </summary>
    /// <param name="nodes">
    /// Existing corner nodes. On return, midside nodes have been appended;
    /// the original corner nodes keep their original indices.
    /// </param>
    /// <param name="q4Quads">
    /// Input Q4 connectivity: each tuple is (corner1, corner2, corner3, corner4) — 0-based,
    /// CCW — into <paramref name="nodes"/>.
    /// </param>
    /// <returns>
    /// Q8 serendipity connectivity: each tuple is (n1, n2, n3, n4, n5, n6, n7, n8) where
    /// n1..n4 are the corners and n5 = midside of n1-n2, n6 = midside of n2-n3,
    /// n7 = midside of n3-n4, n8 = midside of n4-n1. All indices are 0-based into the
    /// extended <paramref name="nodes"/> list (including newly appended midsides).
    /// </returns>
    public static List<(int n1, int n2, int n3, int n4, int n5, int n6, int n7, int n8)> Upgrade(
        List<Point>                              nodes,
        IReadOnlyList<(int a, int b, int c, int d)> q4Quads)
    {
        // Edge → midside node index.  Key = (min, max) to be canonical.
        var edgeToMid = new Dictionary<(int lo, int hi), int>(q4Quads.Count * 4);
        var q8 = new List<(int, int, int, int, int, int, int, int)>(q4Quads.Count);

        int GetOrAddMid(int i, int j)
        {
            var key = i < j ? (i, j) : (j, i);
            if (edgeToMid.TryGetValue(key, out int mid)) return mid;
            // Insert midpoint
            mid = nodes.Count;
            nodes.Add(new Point((nodes[i].X + nodes[j].X) * 0.5,
                                (nodes[i].Y + nodes[j].Y) * 0.5));
            edgeToMid[key] = mid;
            return mid;
        }

        foreach (var (a, b, c, d) in q4Quads)
        {
            int n5 = GetOrAddMid(a, b); // mid of edge a-b
            int n6 = GetOrAddMid(b, c); // mid of edge b-c
            int n7 = GetOrAddMid(c, d); // mid of edge c-d
            int n8 = GetOrAddMid(d, a); // mid of edge d-a
            q8.Add((a, b, c, d, n5, n6, n7, n8));
        }

        return q8;
    }
}
