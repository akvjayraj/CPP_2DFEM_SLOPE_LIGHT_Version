using System.Globalization;
using System.IO;
using System.Text;

namespace GeotechSlopeApp.Tests.Core.Verification;

/// <summary>
/// RS3 Joint Verification Case 4 / RS2 Stress §27 — triaxial jointed column, Mohr-Coulomb slip.
/// β is the joint angle from the horizontal. Failure:
///   σ1 = σ3 + 2(σ3 tanφ + c) / ((1 − tanφ cotβ) sin 2β)
/// Cohesions from RS2 Table 27.1 (omitted in the RS3 table).
/// </summary>
public static class Jr05DeckBuilder
{
    public const double KnKpaPerM = 1.0e7;
    public const double KsKpaPerM = 1.0e6;
    public const double EKpa = 2.0e6;
    public const double Nu = 0.3;
    public const double WidthM = 1.0;
    public const double HeightM = 3.0;
    public const double PhiDeg = 30;
    public const double Case1Sigma3Kpa = 35000;
    public const double Case1CohesionKpa = 11135;
    public const double Case2Sigma3Kpa = 70000;
    public const double Case2CohesionKpa = 274.9;

    public static readonly double[] AnglesDeg = { 35, 40, 45, 50, 60, 70 };
    public static readonly Jr03DeckBuilder.MeshSpec Medium = Jr03DeckBuilder.Medium;

    public static double AnalyticalSigma1Kpa(double sigma3Kpa, double cKpa, double betaDeg)
    {
        double beta = betaDeg * Math.PI / 180.0;
        double phi = PhiDeg * Math.PI / 180.0;
        double tanPhi = Math.Tan(phi);
        double cotBeta = 1.0 / Math.Tan(beta);
        double den = (1.0 - tanPhi * cotBeta) * Math.Sin(2.0 * beta);
        return sigma3Kpa + 2.0 * (sigma3Kpa * tanPhi + cKpa) / den;
    }

    public static (double Sn, double Tau) CauchyOnJoint(double sigma1Kpa, double sigma3Kpa, double betaDeg)
    {
        double b = betaDeg * Math.PI / 180.0;
        double sn = sigma1Kpa * Math.Cos(b) * Math.Cos(b) + sigma3Kpa * Math.Sin(b) * Math.Sin(b);
        double tau = (sigma1Kpa - sigma3Kpa) * Math.Sin(b) * Math.Cos(b);
        return (sn, tau);
    }

    public static double JointY0(double betaDeg) => (HeightM - Math.Tan(betaDeg * Math.PI / 180.0)) / 2.0;

    public static string BuildDat(
        Jr03DeckBuilder.MeshSpec mesh,
        double betaDeg,
        double sigma3Kpa,
        double cKpa,
        double sigma1Kpa,
        int increments = 8)
    {
        int nx = mesh.Nx;
        int nyL = mesh.NyQuads / 2;
        int nyU = mesh.NyQuads - nyL;
        int nxl = nx + 1;
        int nLower = nxl * (nyL + 1);
        int nUpper = nxl * (nyU + 1);
        int nn = nLower + nUpper;
        int nels = nx * nyL * 2 + nx * nyU * 2;
        double y0 = JointY0(betaDeg);
        double tanB = Math.Tan(betaDeg * Math.PI / 180.0);

        int NodeL(int ix, int iy) => iy * nxl + ix + 1;
        int NodeU(int ix, int iy) => nLower + iy * nxl + ix + 1;
        double Yj(int ix) => y0 + (ix / (double)nx) * tanB;

        var sb = new StringBuilder();
        sb.AppendLine("! <solver:p610_t3>");
        sb.AppendLine($"! JR-05 RS3 Case 4  beta={Fmt(betaDeg)}  s3={Fmt(sigma3Kpa)}  c={Fmt(cKpa)}  s1={Fmt(sigma1Kpa)}");
        sb.AppendLine("! <BEGIN:AnalysisOptions>");
        sb.AppendLine("! failure_criterion Elastic");
        sb.AppendLine("! material_type Isotropic");
        sb.AppendLine("! mesh_type T3");
        sb.AppendLine("! solver_executable p610_t3_elastoplastic");
        sb.AppendLine("! <END:AnalysisOptions>");
        sb.AppendLine($"{nels}  {nn}  1");
        sb.AppendLine($"89  1.0e10  0  0  {Fmt(EKpa)}  {Fmt(Nu)}  0");

        for (int iy = 0; iy <= nyL; iy++)
            for (int ix = 0; ix <= nx; ix++)
            {
                double yj = Yj(ix);
                sb.AppendLine($"{Fmt(ix / (double)nx)}  {Fmt(yj * iy / nyL)}");
            }
        for (int iy = 0; iy <= nyU; iy++)
            for (int ix = 0; ix <= nx; ix++)
            {
                double yj = Yj(ix);
                sb.AppendLine($"{Fmt(ix / (double)nx)}  {Fmt(yj + (HeightM - yj) * iy / nyU)}");
            }

        void QuadPair(int n00, int n10, int n11, int n01)
        {
            sb.AppendLine($"{n00}  {n10}  {n11}");
            sb.AppendLine($"{n00}  {n11}  {n01}");
        }
        for (int iy = 0; iy < nyL; iy++)
            for (int ix = 0; ix < nx; ix++)
                QuadPair(NodeL(ix, iy), NodeL(ix + 1, iy), NodeL(ix + 1, iy + 1), NodeL(ix, iy + 1));
        for (int iy = 0; iy < nyU; iy++)
            for (int ix = 0; ix < nx; ix++)
                QuadPair(NodeU(ix, iy), NodeU(ix + 1, iy), NodeU(ix + 1, iy + 1), NodeU(ix, iy + 1));

        sb.AppendLine($"{nxl}");
        for (int ix = 0; ix <= nx; ix++)
        {
            int id = NodeL(ix, 0);
            sb.AppendLine(ix == 0 ? $"{id}  0  0" : $"{id}  1  0");
        }

        var watch = new List<int>
        {
            NodeL(0, 0), NodeL(nx, 0),
            NodeL(0, nyL), NodeL(nx, nyL),
            NodeU(0, 0), NodeU(nx, 0),
            NodeU(0, nyU), NodeU(nx, nyU)
        };
        sb.AppendLine($"{watch.Count}");
        sb.AppendLine(string.Join("  ", watch));
        sb.AppendLine($"0.0001  80  {Math.Max(1, increments)}  0");

        int nSide = nyL + nyU;
        sb.AppendLine("3");
        sb.AppendLine($"0  0  {Fmt(-sigma1Kpa)}  0");
        sb.AppendLine($"{nx}");
        for (int ix = nx - 1; ix >= 0; ix--)
            sb.AppendLine($"{NodeU(ix + 1, nyU)}  {NodeU(ix, nyU)}");
        sb.AppendLine($"0  {Fmt(-sigma3Kpa)}  0  0");
        sb.AppendLine($"{nSide}");
        for (int iy = 0; iy < nyL; iy++)
            sb.AppendLine($"{NodeL(nx, iy)}  {NodeL(nx, iy + 1)}");
        for (int iy = 0; iy < nyU; iy++)
            sb.AppendLine($"{NodeU(nx, iy)}  {NodeU(nx, iy + 1)}");
        sb.AppendLine($"0  {Fmt(-sigma3Kpa)}  0  0");
        sb.AppendLine($"{nSide}");
        for (int iy = nyU - 1; iy >= 0; iy--)
            sb.AppendLine($"{NodeU(0, iy + 1)}  {NodeU(0, iy)}");
        for (int iy = nyL - 1; iy >= 0; iy--)
            sb.AppendLine($"{NodeL(0, iy + 1)}  {NodeL(0, iy)}");

        sb.AppendLine("! <BEGIN:Joints>");
        sb.AppendLine("! include true");
        sb.AppendLine("! njoint 1");
        sb.AppendLine($"! ninterface {nx}");
        sb.AppendLine($"! joint weakplane \"JR-05 joint {Fmt(betaDeg)} deg\"");
        sb.AppendLine($"!   kn {Fmt(KnKpaPerM)}");
        sb.AppendLine($"!   ks {Fmt(KsKpaPerM)}");
        sb.AppendLine($"!   cohesion {Fmt(cKpa)}");
        sb.AppendLine($"!   friction {Fmt(PhiDeg)}");
        sb.AppendLine("!   tension 0");
        sb.AppendLine("!   ends both_open");
        sb.AppendLine("!   install_stage 1");
        sb.AppendLine("!   nvert 2");
        sb.AppendLine($"!     0  {Fmt(y0)}");
        sb.AppendLine($"!     1  {Fmt(y0 + tanB)}");
        sb.AppendLine("! interfaces");
        for (int ix = 0; ix < nx; ix++)
        {
            int n1 = NodeL(ix, nyL);
            int n2 = NodeL(ix + 1, nyL);
            int n3 = NodeU(ix + 1, 0);
            int n4 = NodeU(ix, 0);
            sb.AppendLine(
                $"!   {n1} {n2} {n3} {n4}  {Fmt(KnKpaPerM)} {Fmt(KsKpaPerM)} {Fmt(cKpa)} {Fmt(PhiDeg)} 0  both_open 1 0 0");
        }
        sb.AppendLine("! <END:Joints>");
        return sb.ToString();
    }

    public static void WriteStandardDecks(string decksDir)
    {
        Directory.CreateDirectory(decksDir);
        foreach (double beta in AnglesDeg)
        {
            double s1a = AnalyticalSigma1Kpa(Case1Sigma3Kpa, Case1CohesionKpa, beta);
            File.WriteAllText(
                Path.Combine(decksDir, $"jr05_c1_{Tag(beta)}.dat"),
                BuildDat(Medium, beta, Case1Sigma3Kpa, Case1CohesionKpa, s1a));
            double s1b = AnalyticalSigma1Kpa(Case2Sigma3Kpa, Case2CohesionKpa, beta);
            File.WriteAllText(
                Path.Combine(decksDir, $"jr05_c2_{Tag(beta)}.dat"),
                BuildDat(Medium, beta, Case2Sigma3Kpa, Case2CohesionKpa, s1b));
        }
    }

    public static string Tag(double beta) => ((int)beta).ToString(CultureInfo.InvariantCulture) + "deg";

    private static string Fmt(double v) => v.ToString("G10", CultureInfo.InvariantCulture);
}
