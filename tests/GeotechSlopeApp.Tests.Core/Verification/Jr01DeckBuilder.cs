using System.Globalization;
using System.IO;
using System.Text;

namespace GeotechSlopeApp.Tests.Core.Verification;

/// <summary>
/// Structured T3 decks for JR-01: two unit-square rock blocks, split joint at y=1,
/// top traction (tension-positive syy), base rollers. Units: kPa, m.
/// </summary>
public static class Jr01DeckBuilder
{
    public const double KnKpaPerM = 1.0e5;
    public const double KsKpaPerM = 1.0e5;
    public const double EKpa = 3.0e7;
    public const double Nu = 0.25;
    public const double WidthM = 1.0;

    public sealed record MeshSpec(string Name, int Nx, int NyQuads);

    public static readonly MeshSpec Coarse = new("coarse", 4, 8);
    public static readonly MeshSpec Medium = new("medium", 8, 16);
    public static readonly MeshSpec Fine = new("fine", 16, 32);

    public static double AnalyticalClosureM(double sigmaKpa) => sigmaKpa / KnKpaPerM;

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
        sb.AppendLine($"! JR-01 single joint normal compression  mesh={mesh.Name}  syy={Fmt(syyTensionPositiveKpa)} kPa");
        sb.AppendLine("! <BEGIN:AnalysisOptions>");
        sb.AppendLine("! failure_criterion Elastic");
        sb.AppendLine("! material_type Isotropic");
        sb.AppendLine("! mesh_type T3");
        sb.AppendLine("! solver_executable p610_t3_elastoplastic");
        sb.AppendLine("! <END:AnalysisOptions>");
        sb.AppendLine($"{nels}  {nn}  1");
        sb.AppendLine($"89  1.0e10  0  0  {Fmt(EKpa)}  {Fmt(Nu)}  0");

        for (int iy = 0; iy <= nyL; iy++)
        {
            double y = iy / (double)nyL;
            for (int ix = 0; ix <= nx; ix++)
                sb.AppendLine($"{Fmt(ix / (double)nx)}  {Fmt(y)}");
        }
        for (int iy = 0; iy <= nyU; iy++)
        {
            double y = 1.0 + iy / (double)nyU;
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

        var watch = new List<int> { NodeL(0, 0), NodeL(nx, 0), NodeL(0, nyL), NodeL(nx, nyL), NodeU(0, 0), NodeU(nx, 0), NodeU(0, nyU), NodeU(nx, nyU) };
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
        sb.AppendLine("! joint weakplane \"JR-01 horizontal joint\"");
        sb.AppendLine($"!   kn {Fmt(KnKpaPerM)}");
        sb.AppendLine($"!   ks {Fmt(KsKpaPerM)}");
        sb.AppendLine("!   cohesion 0");
        sb.AppendLine("!   friction 0");
        sb.AppendLine("!   tension 0");
        sb.AppendLine("!   ends both_open");
        sb.AppendLine("!   install_stage 1");
        sb.AppendLine("!   nvert 2");
        sb.AppendLine("!     0  1");
        sb.AppendLine("!     1  1");
        sb.AppendLine("! interfaces");
        for (int ix = 0; ix < nx; ix++)
        {
            int n1 = NodeL(ix, nyL);
            int n2 = NodeL(ix + 1, nyL);
            int n3 = NodeU(ix + 1, 0);
            int n4 = NodeU(ix, 0);
            sb.AppendLine($"!   {n1} {n2} {n3} {n4}  {Fmt(KnKpaPerM)} {Fmt(KsKpaPerM)} 0 0 0  both_open 1 0 0");
        }
        sb.AppendLine("! <END:Joints>");
        return sb.ToString();
    }

    public static void WriteStandardDecks(string decksDir)
    {
        Directory.CreateDirectory(decksDir);
        var stresses = new (string Tag, double SyyKpa)[]
        {
            ("1mpa", -1000),
            ("2mpa", -2000),
            ("5mpa", -5000),
            ("10mpa", -10000)
        };
        foreach (var (tag, syy) in stresses)
            File.WriteAllText(Path.Combine(decksDir, $"jr01_medium_{tag}.dat"), BuildDat(Medium, syy));

        File.WriteAllText(Path.Combine(decksDir, "jr01_coarse_10mpa.dat"), BuildDat(Coarse, -10000));
        File.WriteAllText(Path.Combine(decksDir, "jr01_fine_10mpa.dat"), BuildDat(Fine, -10000));

        foreach (var (tag, syy) in stresses)
            File.WriteAllText(Path.Combine(decksDir, $"jr01_medium_unload_{tag}.dat"), BuildDat(Medium, syy));
        File.WriteAllText(Path.Combine(decksDir, "jr01_medium_unload_0mpa.dat"), BuildDat(Medium, 0, increments: 1));
        File.WriteAllText(Path.Combine(decksDir, "jr01_medium_tension.dat"), BuildDat(Medium, 100, increments: 5));
    }

    private static string Fmt(double v) => v.ToString("G10", CultureInfo.InvariantCulture);
}
