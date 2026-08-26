using System.Collections.Generic;

namespace GeotechSlopeApp.Models;

/// <summary>
/// Serializable surface boolean row for .xfem and geometry copy (not UI-bound).
/// </summary>
public sealed class SurfaceBooleanOperationSnapshot
{
    public int OperationId { get; set; }
    public string OperationType { get; set; } = "";
    public BooleanOperationType TypeEnum { get; set; }
    public string? RegionAName { get; set; }
    public string? RegionBName { get; set; }
    public string? ResultRegionName { get; set; }
    public int RegionASurfaceNumber { get; set; }
    public int RegionBSurfaceNumber { get; set; }

    /// <summary>When set (2+ entries), the operation is a multi-surface union; legacy rows omit this.</summary>
    public List<int>? ParticipatingSurfaceNumbers { get; set; }

    public double RegionAArea { get; set; }
    public double RegionBArea { get; set; }
    public double ResultArea { get; set; }
    public bool IsComplete { get; set; }

    /// <summary>Construction stage when this operation becomes visible. 0 = legacy unset.</summary>
    public int AppliedInStageNumber { get; set; }
}
