using System.Collections.Generic;

namespace GeotechSlopeApp.Models.Geometry;

public enum GeologicalFeatureKind
{
    WeakPlane = 0,
    Bedding = 1,
    TensionCrack = 2
}

/// <summary>Drawable geological constraint polyline (weak plane, bedding, or tension crack).</summary>
public sealed class GeologicalFeature
{
    public required string Id { get; init; }
    public GeologicalFeatureKind Kind { get; set; }
    public string Name { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    /// <summary>Optional material whose strength applies along the plane.</summary>
    public int? MaterialId { get; set; }
    /// <summary>Axis polyline vertices (open).</summary>
    public List<(double X, double Y)> Vertices { get; } = new();
    /// <summary>For tension cracks: optional water elevation inside the crack (null = dry).</summary>
    public double? WaterElevation { get; set; }
    /// <summary>Bedding / weak-plane dip angle in degrees from +X (counter-clockwise), when constant.</summary>
    public double? DipAngleDeg { get; set; }
    /// <summary>Interface normal stiffness (kPa/m) when exported as a joint.</summary>
    public double? KnKpa { get; set; }
    /// <summary>Interface shear stiffness (kPa/m).</summary>
    public double? KsKpa { get; set; }
    public double? JointCohesionKpa { get; set; }
    public double? JointFrictionDeg { get; set; }
    public double? JointTensionCutoffKpa { get; set; }
    /// <summary>Library property set used when this joint was placed.</summary>
    public int? PropertySetId { get; set; }
    public FeJointEndCondition EndCondition { get; set; } = FeJointEndCondition.BothClosed;
    public int InstallStage { get; set; } = 1;
    /// <summary>When set, this trace belongs to a generated joint network and is replaced on regenerate.</summary>
    public string? JointNetworkId { get; set; }
    public string? JointNetworkName { get; set; }
}
