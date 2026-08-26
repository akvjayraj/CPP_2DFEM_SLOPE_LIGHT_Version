using System.Collections.Generic;
using System.Windows;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Converts a T3 (3-node linear triangle) mesh to a T6 (6-node quadratic triangle) mesh
/// by inserting midside nodes at the midpoint of every triangle edge.
/// Edges shared between adjacent triangles use the SAME midside node index, so the
/// upgraded mesh remains conforming with no duplicate interface nodes.
/// </summary>
public static class T6Upgrader
{
    /// <summary>
    /// Upgrade a T3 mesh to T6 in-place.
    /// </summary>
    /// <param name="nodes">
    /// Existing corner nodes. On return, midside nodes have been appended;
    /// the original corner nodes keep their original indices.
    /// </param>
    /// <param name="t3Triangles">
    /// Input T3 connectivity: each tuple is (corner1, corner2, corner3) — 0-based into <paramref name="nodes"/>.
    /// </param>
    /// <returns>
    /// T6 connectivity: each tuple is (n1, n2, n3, n4, n5, n6) where n4 = midside of n1-n2,
    /// n5 = midside of n2-n3, n6 = midside of n3-n1. All indices are 0-based into the
    /// extended <paramref name="nodes"/> list (including newly appended midsides).
    /// </returns>
    public static List<(int n1, int n2, int n3, int n4, int n5, int n6)> Upgrade(
        List<Point>                         nodes,
        IReadOnlyList<(int a, int b, int c)> t3Triangles)
    {
        // Edge → midside node index.  Key = (min, max) to be canonical.
        var edgeToMid = new Dictionary<(int lo, int hi), int>(t3Triangles.Count * 3);
        var t6 = new List<(int, int, int, int, int, int)>(t3Triangles.Count);

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

        foreach (var (a, b, c) in t3Triangles)
        {
            int n4 = GetOrAddMid(a, b); // mid of edge a-b
            int n5 = GetOrAddMid(b, c); // mid of edge b-c
            int n6 = GetOrAddMid(c, a); // mid of edge c-a
            t6.Add((a, b, c, n4, n5, n6));
        }

        return t6;
    }
}
