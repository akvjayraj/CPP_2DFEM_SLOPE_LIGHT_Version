using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;

namespace GeotechSlopeApp.Models;

/// <summary>Item for mesh node number display (canvas coords).</summary>
public class MeshNodeDisplayItem
{
    public double X { get; set; }
    public double Y { get; set; }
    public int NodeId { get; set; }
}

/// <summary>Display node for mesh statistics and canvas (Location in axis coords until converted for canvas).</summary>
public class MeshDiscretizationNodeDisplay
{
    /// <summary>Global mesh node id (1..nn). When 0, canvas falls back to collection index + 1.</summary>
    public int NodeId { get; set; }
    public Point Location { get; set; }
    /// <summary>User-defined table / mesh-info selection highlight on the canvas.</summary>
    public bool IsHighlighted { get; set; }
}

/// <summary>Display element for mesh statistics and canvas. 1D: 2 nodes; 2D: 3+ nodes as Polygon.</summary>
public class MeshElementDisplay
{
    public PointCollection Nodes { get; set; } = new();
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public int DisplayId { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsHighlighted { get; set; }
    /// <summary>True for T6 (6-node) triangles: canvas uses slightly thicker stroke and NonZero fill rule.</summary>
    public bool IsQuadraticTriangle { get; set; }
    /// <summary>True for Q4 (4-node) quadrilaterals.</summary>
    public bool IsQuadrilateral { get; set; }
    /// <summary>True for Q8 (8-node serendipity) quadrilaterals; <see cref="IsQuadrilateral"/> is also true.</summary>
    public bool IsQuadraticQuad { get; set; }
    /// <summary>Q8 outline uses Smith emb_2d local node order (p64 import); see <see cref="MeshInformationRow.SmithEmb2dLocalOrder"/>.</summary>
    public bool Q8SmithEmb2dLocalOrder { get; set; }
    /// <summary>When true, preserve the full 8-point Q8 outline in the canvas instead of collapsing to corners only.</summary>
    public bool PreserveQ8MidEdgeOutline { get; set; }
    /// <summary>True if this triangle was unmatched during Q4/Q8 conversion (kept as fallback). Highlighted optionally.</summary>
    public bool IsUnmatchedTriangle { get; set; }
    /// <summary>
    /// Canvas coordinates of midside nodes (N4, N5, N6) for T6 only; order matches
    /// corner→midside outline N1–N4–N2–N5–N3–N6 at indices 1,3,5. Empty for T3.
    /// </summary>
    public PointCollection MidsideMarkerPoints { get; set; } = new();
    /// <summary>Element-ID text font size in canvas units (matches geometry vertex numbers).</summary>
    public double LabelFontSize { get; set; } = 8.0;
    /// <summary>Element-ID text block width in canvas units for centering.</summary>
    public double LabelWidth { get; set; } = 10.0;
    /// <summary>Translate X used to center the ID text block around CenterX.</summary>
    public double LabelOffsetX { get; set; } = -5.0;
    /// <summary>Translate Y used to center the ID text block around CenterY.</summary>
    public double LabelOffsetY { get; set; } = -5.0;
    /// <summary>Semi-transparent fill from Material Properties (by element etype). Null = outline only.</summary>
    public Brush? Fill { get; set; }
}

/// <summary>
/// Row for Mesh Information DataGrid.
/// T3:  corners 1-3,                 Node4..8 = 0.
/// T6:  corners 1-3 + midsides 4-6 (mid of edges 1-2, 2-3, 3-1), Node7/8 = 0.
/// Q4:  corners 1-4,                 Node5..8 = 0.
/// Q8 (serendipity): corners 1-4 + midsides 5-8 (mid of edges 1-2, 2-3, 3-4, 4-1).
/// Q8 (Smith emb_2d / p64 .dat): N1..N8 in perimeter order — set <see cref="SmithEmb2dLocalOrder"/>.
/// 1D:  Node3=0, N3X=N3Y=0.
/// </summary>
public class MeshInformationRow
{
    /// <summary>When true, N1..N8 follow Smith <c>emb_2d_geom</c> local order (p64 explicit decks).</summary>
    public bool SmithEmb2dLocalOrder { get; set; }

    public int ElementId { get; set; }
    public int Node1 { get; set; }
    public int Node2 { get; set; }
    public int Node3 { get; set; }
    /// <summary>T6: midside on edge Node1–Node2. Q4: 4th corner. Q8: 4th corner. 0 for T3.</summary>
    public int Node4 { get; set; }
    /// <summary>T6: midside on edge Node2–Node3. Q8: midside on edge Node1–Node2. 0 for T3/Q4.</summary>
    public int Node5 { get; set; }
    /// <summary>T6: midside on edge Node3–Node1. Q8: midside on edge Node2–Node3. 0 for T3/Q4.</summary>
    public int Node6 { get; set; }
    /// <summary>Q8: midside on edge Node3–Node4. 0 otherwise.</summary>
    public int Node7 { get; set; }
    /// <summary>Q8: midside on edge Node4–Node1. 0 otherwise.</summary>
    public int Node8 { get; set; }
    /// <summary>Element type (1-based): which layer's material (prop set) applies to this element. Matches .dat etype.</summary>
    public int Etype { get; set; }
    public double N1X { get; set; }
    public double N1Y { get; set; }
    public double N2X { get; set; }
    public double N2Y { get; set; }
    public double N3X { get; set; }
    public double N3Y { get; set; }
    public double N4X { get; set; }
    public double N4Y { get; set; }
    public double N5X { get; set; }
    public double N5Y { get; set; }
    public double N6X { get; set; }
    public double N6Y { get; set; }
    public double N7X { get; set; }
    public double N7Y { get; set; }
    public double N8X { get; set; }
    public double N8Y { get; set; }
    /// <summary>Optional kind tag set by the mesher: "T3", "T6", "Q4", "Q8". Empty falls back to inference.</summary>
    public string Kind { get; set; } = "";
    public string Status { get; set; } = "Active";

    /// <summary>True when this row represents a 4-node quadrilateral (Q4 or Q8 corner block).</summary>
    public bool IsQuad =>
        string.Equals(Kind, "Q4", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Kind, "Q8", StringComparison.OrdinalIgnoreCase) ||
        (Node4 > 0 && (string.IsNullOrEmpty(Kind) ? IsQuadInferred() : false));
    /// <summary>True when this row represents an 8-node serendipity quadrilateral.</summary>
    public bool IsQ8 =>
        string.Equals(Kind, "Q8", StringComparison.OrdinalIgnoreCase) ||
        (Node5 > 0 && Node6 > 0 && Node7 > 0 && Node8 > 0 && Node4 > 0);
    /// <summary>True when this row represents a 4-node linear quadrilateral.</summary>
    public bool IsQ4 =>
        string.Equals(Kind, "Q4", StringComparison.OrdinalIgnoreCase) ||
        (Node4 > 0 && Node5 == 0 && Node6 == 0 && Node7 == 0 && Node8 == 0 && IsQuadInferred());
    /// <summary>True when this is a 6-node quadratic triangle (T6).</summary>
    public bool IsT6 =>
        string.Equals(Kind, "T6", StringComparison.OrdinalIgnoreCase) ||
        (Node4 > 0 && Node5 > 0 && Node6 > 0 && Node7 == 0 && Node8 == 0 && !IsQuadInferred());

    /// <summary>True when this is a 3-node linear triangle (T3).</summary>
    public bool IsT3 =>
        string.Equals(Kind, "T3", StringComparison.OrdinalIgnoreCase) ||
        (Node3 > 0 && Node4 == 0 && Node5 == 0 && Node6 == 0 && Node7 == 0 && Node8 == 0);

    /// <summary>Midside node ids for T6 (4–6) or Q8 (serendipity 5–8 / Smith 2,4,6,8).</summary>
    public IEnumerable<int> EnumerateMidsideNodeIds()
    {
        foreach (var (_, _, nodeId) in EnumerateMidsideNodes())
        {
            if (nodeId > 0)
                yield return nodeId;
        }
    }

    /// <summary>
    /// Corner-to-corner boundary edges with optional midside NodeC.
    /// Works for T3 / T6 / Q4 / Q8 (serendipity and Smith emb_2d perimeter order).
    /// </summary>
    public IEnumerable<(int A, int B, int Mid)> EnumerateCornerEdgesWithMid()
    {
        if (IsQ8 && SmithEmb2dLocalOrder
            && Node1 > 0 && Node2 > 0 && Node3 > 0 && Node4 > 0
            && Node5 > 0 && Node6 > 0 && Node7 > 0 && Node8 > 0)
        {
            // Perimeter: C-M-C-M-C-M-C-M
            yield return (Node1, Node3, Node2);
            yield return (Node3, Node5, Node4);
            yield return (Node5, Node7, Node6);
            yield return (Node7, Node1, Node8);
            yield break;
        }

        if (IsQ8 || (IsQuad && Node5 > 0 && Node6 > 0 && Node7 > 0 && Node8 > 0))
        {
            yield return (Node1, Node2, Node5);
            yield return (Node2, Node3, Node6);
            yield return (Node3, Node4, Node7);
            yield return (Node4, Node1, Node8);
            yield break;
        }

        if (IsQuad && Node4 > 0)
        {
            yield return (Node1, Node2, 0);
            yield return (Node2, Node3, 0);
            yield return (Node3, Node4, 0);
            yield return (Node4, Node1, 0);
            yield break;
        }

        if (IsT6 && Node4 > 0 && Node5 > 0 && Node6 > 0)
        {
            yield return (Node1, Node2, Node4);
            yield return (Node2, Node3, Node5);
            yield return (Node3, Node1, Node6);
            yield break;
        }

        if (Node1 > 0 && Node2 > 0 && Node3 > 0)
        {
            yield return (Node1, Node2, 0);
            yield return (Node2, Node3, 0);
            yield return (Node3, Node1, 0);
        }
    }

    /// <summary>Corner node ids used for element centroid / area probes (excludes midsides).</summary>
    public IEnumerable<int> EnumerateCornerNodeIds()
    {
        if (IsQ8 && SmithEmb2dLocalOrder)
        {
            if (Node1 > 0) yield return Node1;
            if (Node3 > 0) yield return Node3;
            if (Node5 > 0) yield return Node5;
            if (Node7 > 0) yield return Node7;
            yield break;
        }

        if (IsQuad && Node4 > 0)
        {
            yield return Node1;
            yield return Node2;
            yield return Node3;
            yield return Node4;
            yield break;
        }

        if (Node1 > 0) yield return Node1;
        if (Node2 > 0) yield return Node2;
        if (Node3 > 0) yield return Node3;
    }

    /// <summary>Midside axis positions and optional global node ids for canvas highlighting.</summary>
    public IEnumerable<(double X, double Y, int NodeId)> EnumerateMidsideNodes()
    {
        if (IsT6)
        {
            if (Node4 > 0) yield return (N4X, N4Y, Node4);
            if (Node5 > 0) yield return (N5X, N5Y, Node5);
            if (Node6 > 0) yield return (N6X, N6Y, Node6);
        }
        else if (IsQ8)
        {
            if (SmithEmb2dLocalOrder)
            {
                if (Node2 > 0) yield return (N2X, N2Y, Node2);
                if (Node4 > 0) yield return (N4X, N4Y, Node4);
                if (Node6 > 0) yield return (N6X, N6Y, Node6);
                if (Node8 > 0) yield return (N8X, N8Y, Node8);
            }
            else
            {
                if (Node5 > 0) yield return (N5X, N5Y, Node5);
                if (Node6 > 0) yield return (N6X, N6Y, Node6);
                if (Node7 > 0) yield return (N7X, N7Y, Node7);
                if (Node8 > 0) yield return (N8X, N8Y, Node8);
            }
        }
    }

    private bool IsQuadInferred()
    {
        if (!string.IsNullOrEmpty(Kind))
            return string.Equals(Kind, "Q4", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(Kind, "Q8", StringComparison.OrdinalIgnoreCase);
        if (Node4 == 0) return false;
        return !MidsideOnEdge(N1X, N1Y, N2X, N2Y, N4X, N4Y);
    }

    private static bool MidsideOnEdge(double ax, double ay, double bx, double by, double mx, double my)
    {
        double exx = (ax + bx) * 0.5, eyy = (ay + by) * 0.5;
        double dx = mx - exx, dy = my - eyy;
        double len2 = (bx - ax) * (bx - ax) + (by - ay) * (by - ay);
        if (len2 <= 0) return false;
        return (dx * dx + dy * dy) <= 0.05 * 0.05 * len2;
    }
}

/// <summary>
/// Record of a triangle that could not be paired into an acceptable quad during RS2D Q4/Q8 conversion.
/// Surfaced by the optional unmatched-triangle highlight tool.
/// </summary>
public sealed class UnmatchedTriangleInfo
{
    public int ElementId { get; init; }
    public string Reason { get; init; } = "";
}

/// <summary>One advisory warning row for the optional RS2D best-practice panel.</summary>
public sealed class Rs2dWarning
{
    public string Severity { get; init; } = "Info";
    public string Message { get; init; } = "";
    public string Suggestion { get; init; } = "";
    public Rs2dWarning() { }
    public Rs2dWarning(string severity, string message, string suggestion)
    {
        Severity = severity;
        Message = message;
        Suggestion = suggestion;
    }
}
