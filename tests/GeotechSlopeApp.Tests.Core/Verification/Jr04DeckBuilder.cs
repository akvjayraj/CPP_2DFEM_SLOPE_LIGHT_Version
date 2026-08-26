using System.Globalization;
using System.IO;
using System.Text;

namespace GeotechSlopeApp.Tests.Core.Verification;

/// <summary>
/// RS3 Joint Verification Case 2 — jointed column under body force (unit weight = 10 MPa/m).
/// Same 1 m × 3 m column and mid-height joint as JR-03.
/// Analytical: u(y) = (γ/E)(H y − y²/2) + H(y − yj)·γ(H − yj)/kn.
/// </summary>
public static class Jr04DeckBuilder
{
    public const double KnKpaPerM = 1.0e7;
    public const double KsKpaPerM = 1.0e7;
    public const double EKpa = 2.0e6;
    public const double Nu = 0.01;
    public const double GammaKpaPerM = 1.0e4;
    public const double WidthM = 1.0;
    public const double HeightM = 3.0;
    public const double JointYM = 1.5;
    public const double PhiDeg = 89;

    public static readonly Jr03DeckBuilder.MeshSpec Coarse = Jr03DeckBuilder.Coarse;
    public static readonly Jr03DeckBuilder.MeshSpec Medium = Jr03DeckBuilder.Medium;
    public static readonly Jr03DeckBuilder.MeshSpec Fine = Jr03DeckBuilder.Fine;

    public static double OverburdenAtJointKpa => GammaKpaPerM * (HeightM - JointYM);

    public static double AnalyticalClosureM() => OverburdenAtJointKpa / KnKpaPerM;

    /// <summary>Downward displacement. Joint jump γ(H − yj)/kn is added for y ≥ yj.</summary>
    public static double AnalyticalUyDownM(double y)
    {
        double rock = GammaKpaPerM / EKpa * (HeightM * y - 0.5 * y * y);
        return y > JointYM - 1e-12 ? rock + AnalyticalClosureM() : rock;
    }

    public static string BuildDat(Jr03DeckBuilder.MeshSpec mesh, int increments = 5)
    {
        int nx = mesh.Nx;
        int nyL = mesh.NyQuads / 2;
        int nyU = mesh.NyQuads - nyL;
        int nxl = nx + 1;
        int nLower = nxl * (nyL + 1);
        int nUpper = nxl * (nyU + 1);
        int nn = nLower + nUpper;
        int nels = nx * nyL * 2 + nx * nyU * 2;

        int NodeL(int ix, int iy) => iy * nxl + ix + 1;
        int NodeU(int ix, int iy) => nLower + iy * nxl + ix + 1;

        var sb = new StringBuilder();
        sb.AppendLine("! <solver:p610_t3>");
        sb.AppendLine($"! JR-04 RS3 Case 2 body-force column  mesh={mesh.Name}  gamma={Fmt(GammaKpaPerM)} kPa/m");
        sb.AppendLine("! <BEGIN:AnalysisOptions>");
        sb.AppendLine("! failure_criterion Elastic");
        sb.AppendLine("! material_type Isotropic");
        sb.AppendLine("! mesh_type T3");
        sb.AppendLine("! solver_executable p610_t3_elastoplastic");
        sb.AppendLine("! <END:AnalysisOptions>");
        sb.AppendLine($"{nels}  {nn}  1");
        sb.AppendLine($"{Fmt(PhiDeg)}  1.0e10  0  {Fmt(GammaKpaPerM)}  {Fmt(EKpa)}  {Fmt(Nu)}  0");

        for (int iy = 0; iy <= nyL; iy++)
        {
            double y = JointYM * iy / nyL;
            for (int ix = 0; ix <= nx; ix++)
                sb.AppendLine($"{Fmt(ix / (double)nx)}  {Fmt(y)}");
        }
        for (int iy = 0; iy <= nyU; iy++)
        {
            double y = JointYM + (HeightM - JointYM) * iy / nyU;
            for (int ix = 0; ix <= nx; ix++)
                sb.AppendLine($"{Fmt(ix / (double)nx)}  {Fmt(y)}");
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
        sb.AppendLine($"0.0001  80  {Math.Max(1, increments)}  1");
        sb.AppendLine("0");

        sb.AppendLine("! <BEGIN:Joints>");
        sb.AppendLine("! include true");
        sb.AppendLine("! njoint 1");
        sb.AppendLine($"! ninterface {nx}");
        sb.AppendLine("! joint weakplane \"JR-04 horizontal joint\"");
        sb.AppendLine($"!   kn {Fmt(KnKpaPerM)}");
        sb.AppendLine($"!   ks {Fmt(KsKpaPerM)}");
        sb.AppendLine("!   cohesion 0");
        sb.AppendLine($"!   friction {Fmt(PhiDeg)}");
        sb.AppendLine("!   tension 0");
        sb.AppendLine("!   ends both_open");
        sb.AppendLine("!   install_stage 1");
        sb.AppendLine("!   nvert 2");
        sb.AppendLine($"!     0  {Fmt(JointYM)}");
        sb.AppendLine($"!     1  {Fmt(JointYM)}");
        sb.AppendLine("! interfaces");
        for (int ix = 0; ix < nx; ix++)
        {
            int n1 = NodeL(ix, nyL);
            int n2 = NodeL(ix + 1, nyL);
            int n3 = NodeU(ix + 1, 0);
            int n4 = NodeU(ix, 0);
            sb.AppendLine(
                $"!   {n1} {n2} {n3} {n4}  {Fmt(KnKpaPerM)} {Fmt(KsKpaPerM)} 0 {Fmt(PhiDeg)} 0  both_open 1 0 0");
        }
        sb.AppendLine("! <END:Joints>");
        return sb.ToString();
    }

    public static void WriteStandardDecks(string decksDir)
    {
        Directory.CreateDirectory(decksDir);
        File.WriteAllText(Path.Combine(decksDir, "jr04_medium.dat"), BuildDat(Medium));
        File.WriteAllText(Path.Combine(decksDir, "jr04_coarse.dat"), BuildDat(Coarse));
        File.WriteAllText(Path.Combine(decksDir, "jr04_fine.dat"), BuildDat(Fine));
    }

    private static string Fmt(double v) => v.ToString("G10", CultureInfo.InvariantCulture);
}
