using System.Collections.Generic;

namespace GeotechSlopeApp.Models.Geometry;

public enum KernelBooleanKind
{
    Union = 0,
    Subtract = 1,
    Intersect = 2,
    Fill = 3
}

/// <summary>Recomputable boolean / fill commit against the planar kernel.</summary>
public sealed class KernelBooleanOperation
{
    public int Sequence { get; set; }
    public KernelBooleanKind Kind { get; set; }
    public int SubjectFaceId { get; set; }
    public int ToolFaceId { get; set; }
    /// <summary>For Fill: closed ring of axis points that become a new soil face.</summary>
    public List<(double X, double Y)> FillRing { get; } = new();
    public int? ResultMaterialId { get; set; }
    public int? StageNumber { get; set; }
    public string Label { get; set; } = "";
}
