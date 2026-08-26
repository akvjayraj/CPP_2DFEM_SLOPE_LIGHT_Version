namespace GeotechSlopeApp.Models.Geometry;

/// <summary>Stiffness/strength copied onto a canvas-drawn joint when the wizard uses Manual draw.</summary>
public sealed class JointWizardDraftProperties
{
    public GeologicalFeatureKind Kind { get; init; } = GeologicalFeatureKind.WeakPlane;
    public string Name { get; init; } = "";
    public int? MaterialId { get; init; }
    public double? DipAngleDeg { get; init; }
    public double? KnKpa { get; init; }
    public double? KsKpa { get; init; }
    public double? JointCohesionKpa { get; init; }
    public double? JointFrictionDeg { get; init; }
    public double? JointTensionCutoffKpa { get; init; }
    public double? WaterElevation { get; init; }
    public int? PropertySetId { get; init; }
    public FeJointEndCondition EndCondition { get; init; } = FeJointEndCondition.BothClosed;
    public int InstallStage { get; init; } = 1;
}
