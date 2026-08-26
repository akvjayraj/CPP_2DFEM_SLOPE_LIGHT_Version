namespace GeotechSlopeApp.Models.Geometry;

/// <summary>
/// 4-node zero-thickness Goodman interface: minus side n1–n2, plus side n4–n3
/// (n−a, n−b, n+b, n+a). Node ids are 1-based mesh ids after the joint split.
/// </summary>
public sealed class FeJointInterfaceElement
{
    public int N1 { get; set; }
    public int N2 { get; set; }
    public int N3 { get; set; }
    public int N4 { get; set; }
    public double Kn { get; set; }
    public double Ks { get; set; }
    public double Cohesion { get; set; }
    public double FrictionDeg { get; set; }
    public double Tension { get; set; }
    public double ResidualCohesion { get; set; }
    public double ResidualFrictionDeg { get; set; }
    public int InstallStage { get; set; } = 1;
    public string Ends { get; set; } = "both_closed";
    public string FeatureId { get; set; } = "";
}
