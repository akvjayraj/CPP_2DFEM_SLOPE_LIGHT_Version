using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace RS2Example01T3SrfCompare;

internal static class Program
{
    private const double W1 = 20.0;
    private const double S1 = 20.0;
    private const double W2 = 10.0;
    private const double H1 = 10.0;
    private const double H2 = 5.0;

    private const double PhiDeg = 19.6;
    private const double CohesionKPa = 3.0;
    private const double Gamma = 20.0;
    private const double YoungsModulusKPa = 100_000.0;
    private const double PoissonsRatio = 0.3;
    private const double K0 = 0.5;

    private const int Nx = 25;
    private const int Ny = 10;

    private static readonly double[] SrfValues = [0.50, 0.80, 0.90, 0.95, 0.99, 1.00, 1.05, 1.10, 1.20];

    private static int Main()
    {
        try
        {
            var repoRoot = FindRepoRoot();
            var outRoot = Path.Combine(repoRoot, "Verification", "slope", "RS2_Example01_simple_slope", "t3_srf_compare");
            var rootSolver = Path.Combine(repoRoot, "library", "solver", "slope_stability_T3_SRF.exe");
            var srfSolver = Path.Combine(repoRoot, "library", "solver", "SRF", "slope_stability_T3_SRF.exe");

            RequireFile(rootSolver);
            RequireFile(srfSolver);

            var rootRows = RunSweep(repoRoot, outRoot, "library_solver", rootSolver);
            var srfRows = RunSweep(repoRoot, outRoot, "library_solver_SRF", srfSolver);
            var summaryPath = Path.Combine(outRoot, "rs2_example01_t3_srf_compare_summary.md");
            File.WriteAllText(summaryPath, BuildSummary(rootSolver, srfSolver, rootRows, srfRows));

            Console.WriteLine($"Wrote {summaryPath}");
            Console.WriteLine("F     root max_disp      SRF max_disp       diff");
            foreach (var f in SrfValues)
            {
                var a = rootRows.Single(r => Math.Abs(r.F - f) < 1e-12);
                var b = srfRows.Single(r => Math.Abs(r.F - f) < 1e-12);
                Console.WriteLine($"{f,4:G3}  {a.MaxDisp,14:E6}  {b.MaxDisp,14:E6}  {Math.Abs(a.MaxDisp - b.MaxDisp):E3}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static List<ResultRow> RunSweep(string repoRoot, string outRoot, string label, string solverExe)
    {
        var outDir = Path.Combine(outRoot, label);
        Directory.CreateDirectory(outDir);
        var rows = new List<ResultRow>();

        foreach (var f in SrfValues)
        {
            var baseName = $"rs2_example01_t3_F{(int)Math.Round(f * 100):000}";
            var datPath = Path.Combine(outDir, baseName + ".dat");
            File.WriteAllText(datPath, BuildDat(f));

            var (exitCode, stdout, stderr) = RunSolver(solverExe, outDir, baseName);
            if (exitCode != 0)
                throw new InvalidOperationException($"{solverExe} failed for F={f:G3} with exit code {exitCode}\n{stdout}\n{stderr}");

            var jsonPath = Path.Combine(outDir, baseName + ".json");
            RequireFile(jsonPath);
            rows.Add(ParseResult(f, jsonPath, stdout + stderr));
        }

        return rows;
    }

    private static string BuildDat(double srfF)
    {
        var nodes = BuildNodes();
        var elements = BuildElements();
        var bcs = BuildBoundaryConditions(nodes);
        var outputNodes = new[] { NodeId(10, Ny), NodeId(20, Ny), NodeId(Nx, Ny) };

        var sb = new StringBuilder();
        sb.AppendLine("! <solver:p610_t3>");
        sb.AppendLine("! RS2 Example 1 converted from p64/Q8 explicit deck to native T3 mesh");
        sb.AppendLine("! Source: Verification/slope/RS2_Example01_simple_slope/rs2_example01_simple_slope.dat");
        sb.AppendLine("! Geometry: w1=20, s1=20, w2=10, h1=10, h2=5 m");
        sb.AppendLine("! Material: phi=19.6 deg, c=3 kPa, gamma=20 kN/m3, E=100000 kPa, nu=0.3");
        sb.AppendLine("! Boundary conditions: fixed base, roller left/right sides");
        sb.AppendLine("! <BEGIN:AnalysisOptions>");
        sb.AppendLine("! failure_criterion MohrCoulomb");
        sb.AppendLine("! material_type Isotropic");
        sb.AppendLine("! mesh_type T3");
        sb.AppendLine("! mesh_source explicit");
        sb.AppendLine("! solver_executable slope_stability_T3_SRF");
        sb.AppendLine("! srf_mode single");
        sb.AppendLine(C($"! srf_F {srfF}"));
        sb.AppendLine("! expected_FoS 0.99");
        sb.AppendLine("! expected_FoS_tolerance_pct 10");
        sb.AppendLine("! expected_FoS_mode srf");
        sb.AppendLine("! slope_geometry rs2_example01_simple_slope_t3");
        sb.AppendLine("! slope_angle_deg 26.5650511771");
        sb.AppendLine(C($"! slope_height_m {H1}"));
        sb.AppendLine("! <END:AnalysisOptions>");
        sb.AppendLine(C($"{elements.Count}  {nodes.Count}  1"));
        sb.AppendLine(C($"{PhiDeg}  {CohesionKPa}  0  {Gamma}  {YoungsModulusKPa}  {PoissonsRatio}  {K0}"));

        foreach (var n in nodes)
            sb.AppendLine(C($"{n.X}  {n.Y}"));
        foreach (var e in elements)
            sb.AppendLine($"{e.N1}  {e.N2}  {e.N3}");

        sb.AppendLine(bcs.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var bc in bcs)
            sb.AppendLine($"{bc.NodeId}  {bc.FixX}  {bc.FixY}");

        sb.AppendLine(outputNodes.Length.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine(string.Join("  ", outputNodes));
        sb.AppendLine("0.02  1000  20  1");
        sb.AppendLine("0");
        return sb.ToString();
    }

    private static List<NodeRow> BuildNodes()
    {
        var nodes = new List<NodeRow>((Nx + 1) * (Ny + 1));
        var totalWidth = W1 + S1 + W2;
        for (var i = 0; i <= Nx; i++)
        {
            var x = totalWidth * i / Nx;
            var top = TopY(x);
            for (var j = 0; j <= Ny; j++)
                nodes.Add(new NodeRow(NodeId(i, j), x, top * j / Ny));
        }
        return nodes;
    }

    private static List<ElementRow> BuildElements()
    {
        var elements = new List<ElementRow>(Nx * Ny * 2);
        var id = 1;
        for (var i = 0; i < Nx; i++)
        {
            for (var j = 0; j < Ny; j++)
            {
                var sw = NodeId(i, j);
                var se = NodeId(i + 1, j);
                var ne = NodeId(i + 1, j + 1);
                var nw = NodeId(i, j + 1);
                elements.Add(new ElementRow(id++, sw, se, ne));
                elements.Add(new ElementRow(id++, sw, ne, nw));
            }
        }
        return elements;
    }

    private static List<BcRow> BuildBoundaryConditions(IEnumerable<NodeRow> nodes)
    {
        var map = new SortedDictionary<int, BcRow>();
        foreach (var node in nodes)
        {
            var i = (node.Id - 1) / (Ny + 1);
            var j = (node.Id - 1) % (Ny + 1);
            if (j == 0)
                map[node.Id] = Merge(map, node.Id, 0, 0);
            if (i == 0 || i == Nx)
                map[node.Id] = Merge(map, node.Id, 0, 1);
        }
        return map.Values.ToList();
    }

    private static BcRow Merge(SortedDictionary<int, BcRow> map, int nodeId, int fixX, int fixY)
    {
        if (!map.TryGetValue(nodeId, out var existing))
            return new BcRow(nodeId, fixX, fixY);
        return new BcRow(
            nodeId,
            existing.FixX == 0 || fixX == 0 ? 0 : 1,
            existing.FixY == 0 || fixY == 0 ? 0 : 1);
    }

    private static double TopY(double x)
    {
        if (x <= W1)
            return H1 + H2;
        if (x <= W1 + S1)
            return H2 + H1 * (1.0 - (x - W1) / S1);
        return H2;
    }

    private static (int exitCode, string stdout, string stderr) RunSolver(string solverExe, string workDir, string baseName)
    {
        var psi = new ProcessStartInfo
        {
            FileName = solverExe,
            Arguments = baseName,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {solverExe}");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout, stderr);
    }

    private static ResultRow ParseResult(double f, string jsonPath, string log)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var root = doc.RootElement;
        var meta = root.GetProperty("meta").GetProperty("srf");
        return new ResultRow(
            f,
            root.GetProperty("node_count").GetInt32(),
            root.GetProperty("element_count").GetInt32(),
            meta.GetProperty("fos").GetDouble(),
            meta.GetProperty("max_disp").GetDouble(),
            meta.GetProperty("max_yield_ratio").GetDouble(),
            meta.GetProperty("max_plastic_strain").GetDouble(),
            meta.GetProperty("max_iters_used").GetInt32(),
            meta.GetProperty("all_increments_converged").GetBoolean(),
            log);
    }

    private static string BuildSummary(string rootSolver, string srfSolver, List<ResultRow> rootRows, List<ResultRow> srfRows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# RS2 Example 1 T3 SRF Solver Comparison");
        sb.AppendLine();
        sb.AppendLine("Converted from the RS2 Example 1 p64/Q8 deck to a native T3 explicit mesh.");
        sb.AppendLine();
        sb.AppendLine($"- Root solver: `{rootSolver}`");
        sb.AppendLine($"- SRF folder solver: `{srfSolver}`");
        sb.AppendLine("- Geometry: `w1=20`, `s1=20`, `w2=10`, `h1=10`, `h2=5` m");
        sb.AppendLine("- Material: `phi=19.6 deg`, `c=3 kPa`, `gamma=20 kN/m3`, `E=100000 kPa`, `nu=0.3`");
        var meshElements = rootRows.FirstOrDefault()?.ElementCount ?? Nx * Ny * 2;
        var meshNodes = rootRows.FirstOrDefault()?.NodeCount ?? (Nx + 1) * (Ny + 1);
        sb.AppendLine($"- Mesh: native T3, `{meshElements}` elements, `{meshNodes}` nodes");
        sb.AppendLine();
        sb.AppendLine("| F | root max disp | SRF max disp | abs diff | root yield | SRF yield | root iters | SRF iters | converged |");
        sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|:---:|");

        var maxDiff = 0.0;
        foreach (var a in rootRows)
        {
            var b = srfRows.Single(r => Math.Abs(r.F - a.F) < 1e-12);
            var diff = Math.Abs(a.MaxDisp - b.MaxDisp);
            maxDiff = Math.Max(maxDiff, diff);
            sb.AppendLine(C($"| {a.F} | {a.MaxDisp:E6} | {b.MaxDisp:E6} | {diff:E3} | {a.MaxYieldRatio:E6} | {b.MaxYieldRatio:E6} | {a.MaxItersUsed} | {b.MaxItersUsed} | {a.AllConverged && b.AllConverged} |"));
        }

        sb.AppendLine();
        sb.AppendLine(C($"Maximum absolute displacement difference between solver locations: `{maxDiff:E6}`."));
        sb.AppendLine(maxDiff <= 1e-12
            ? "Result: no numerical difference was detected between the two solver locations for this converted T3 case."
            : "Result: a numerical difference was detected between the two solver locations.");
        return sb.ToString();
    }

    private static int NodeId(int i, int j) => i * (Ny + 1) + j + 1;
    private static string C(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CMakeLists.txt"))
                && Directory.Exists(Path.Combine(dir.FullName, "Verification", "slope")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static void RequireFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Required file not found: {path}", path);
    }

    private sealed record NodeRow(int Id, double X, double Y);
    private sealed record ElementRow(int Id, int N1, int N2, int N3);
    private sealed record BcRow(int NodeId, int FixX, int FixY);
    private sealed record ResultRow(
        double F,
        int NodeCount,
        int ElementCount,
        double FoS,
        double MaxDisp,
        double MaxYieldRatio,
        double MaxPlasticStrain,
        int MaxItersUsed,
        bool AllConverged,
        string Log);
}
