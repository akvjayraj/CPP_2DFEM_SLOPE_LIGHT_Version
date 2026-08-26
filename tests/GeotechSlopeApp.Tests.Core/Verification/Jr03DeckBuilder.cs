using System.Globalization;
using System.IO;
using System.Text;

namespace GeotechSlopeApp.Tests.Core.Verification;

/// <summary>
/// RS3 Joint Verification Case 1 — single-jointed rock column under axial pressure.
/// Column 1 m × 3 m, horizontal Goodman joint at mid-height y = 1.5 m.
/// Units: kPa, m. Analytical: u(y) = P y / E + H(y − yj) · (P / kn).
/// </summary>
public static class Jr03DeckBuilder
{
    public const double KnKpaPerM = 1.0e7;
    public const double KsKpaPerM = 1.0e7;
    public const double EKpa = 2.0e6;
    public const double Nu = 0.01;
    public const double WidthM = 1.0;
    public const double HeightM = 3.0;
    public const double JointYM = 1.5;
    public const double PhiDeg = 89;

    public sealed record MeshSpec(string Name, int Nx, int NyQuads);

    public static readonly MeshSpec Coarse = new("coarse", 4, 12);
    public static readonly MeshSpec Medium = new("medium", 8, 24);
    public static readonly MeshSpec Fine = new("fine", 16, 48);

    /// <summary>Joint closure δc = P / kn (independent of E).</summary>
    public static double AnalyticalClosureM(double sigmaKpa) => sigmaKpa / KnKpaPerM;

    /// <summary>Downward displacement magnitude. Heaviside jump P/kn for y above the joint.</summary>
    public static double AnalyticalUyDownM(double y, double sigmaKpa)
    {
        double rock = sigmaKpa * y / EKpa;
        return y > JointYM - 1e-12 ? rock + AnalyticalClosureM(sigmaKpa) : rock;
    }

    public static double AnalyticalTopUM(double sigmaKpa) => AnalyticalUyDownM(HeightM, sigmaKpa);

    public static string BuildDat(MeshSpec mesh, double syyTensionPositiveKpa, int increments = 5)
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
        sb.AppendLine($"! JR-03 RS3 Case 1 jointed column  mesh={mesh.Name}  syy={Fmt(syyTensionPositiveKpa)} kPa");
        sb.AppendLine("! <BEGIN:AnalysisOptions>");
        sb.AppendLine("! failure_criterion Elastic");
        sb.AppendLine("! material_type Isotropic");
        sb.AppendLine("! mesh_type T3");
        sb.AppendLine("! solver_executable p610_t3_elastoplastic");
        sb.AppendLine("! <END:AnalysisOptions>");
        sb.AppendLine($"{nels}  {nn}  1");
        sb.AppendLine($"{Fmt(PhiDeg)}  1.0e10  0  0  {Fmt(EKpa)}  {Fmt(Nu)}  0");

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
        sb.AppendLine($"0.0001  80  {Math.Max(1, increments)}  0");

        if (Math.Abs(syyTensionPositiveKpa) < 1e-15)
        {
            sb.AppendLine("0");
        }
        else
        {
            sb.AppendLine("1");
            sb.AppendLine($"0  0  {Fmt(syyTensionPositiveKpa)}  0");
            sb.AppendLine($"{nx}");
            for (int ix = nx - 1; ix >= 0; ix--)
                sb.AppendLine($"{NodeU(ix + 1, nyU)}  {NodeU(ix, nyU)}");
        }

        sb.AppendLine("! <BEGIN:Joints>");
        sb.AppendLine("! include true");
        sb.AppendLine("! njoint 1");
        sb.AppendLine($"! ninterface {nx}");
        sb.AppendLine("! joint weakplane \"JR-03 horizontal joint\"");
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
        File.WriteAllText(Path.Combine(decksDir, "jr03_medium_1mpa.dat"), BuildDat(Medium, -1000));
        File.WriteAllText(Path.Combine(decksDir, "jr03_medium_2mpa.dat"), BuildDat(Medium, -2000));
        File.WriteAllText(Path.Combine(decksDir, "jr03_coarse_1mpa.dat"), BuildDat(Coarse, -1000));
        File.WriteAllText(Path.Combine(decksDir, "jr03_fine_1mpa.dat"), BuildDat(Fine, -1000));
    }

    private static string Fmt(double v) => v.ToString("G10", CultureInfo.InvariantCulture);
}
