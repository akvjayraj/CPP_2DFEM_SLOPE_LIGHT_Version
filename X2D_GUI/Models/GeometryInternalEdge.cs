namespace GeotechSlopeApp.Models;

/// <summary>
/// Extra line (or arc) between two existing geometry vertices.
/// For arcs a through-point stable ID is stored; for straight lines StableMid is -1.
/// </summary>
public sealed class GeometryInternalEdge
{
    /// <summary>Straight-line edge between two vertices.</summary>
    public GeometryInternalEdge(int stablePointIdA, int stablePointIdB)
    {
        StableMin = Math.Min(stablePointIdA, stablePointIdB);
        StableMax = Math.Max(stablePointIdA, stablePointIdB);
        StableMid = -1;
        IsArc = false;
    }

    /// <summary>Arc edge through three vertices (A → mid → B in drawing order).</summary>
    public GeometryInternalEdge(int stableStart, int stableMid, int stableEnd)
    {
        // Store A/B as min/max for duplicate detection; mid is the through-point.
        StableMin = Math.Min(stableStart, stableEnd);
        StableMax = Math.Max(stableStart, stableEnd);
        StableMid = stableMid;
        StableStart = stableStart;
        StableEnd = stableEnd;
        IsArc = true;
    }

    public int StableMin { get; }
    public int StableMax { get; }

    /// <summary>Through-point stable ID for arcs; -1 for straight lines.</summary>
    public int StableMid { get; }

    /// <summary>Ordered start stable ID (only meaningful for arcs).</summary>
    public int StableStart { get; }

    /// <summary>Ordered end stable ID (only meaningful for arcs).</summary>
    public int StableEnd { get; }

    public bool IsArc { get; }

    public bool TouchesStableId(int stableId) =>
        stableId == StableMin || stableId == StableMax || stableId == StableMid;
}
