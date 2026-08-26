using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Text;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Persistence;
using GeotechSlopeApp.ViewModels;

namespace AttachedSlopeUiAutomation;

internal static class Program
{
    private const double W1 = 20.0;
    private const double S1 = 20.0;
    private const double W2 = 20.0;
    private const double H1 = 10.0;
    private const double H2 = 5.0;
    private const double PhiDeg = 20.0;
    private const double CohesionKPa = 15.0;
    private const double Gamma = 20.0;
    private const double YoungsModulusKPa = 100_000.0;
    private const double PoissonsRatio = 0.3;
    private const double K0 = 0.5;
    private const int Nx = 60;
    private const int Ny = 15;

    [STAThread]
    private static int Main()
    {
        try
        {
            var repoRoot = FindRepoRoot();
            var outDir = Path.Combine(repoRoot, "Verification", "slope", "AttachedSlope_T3_SRF");
            Directory.CreateDirectory(outDir);

            var sweep = new[] { 0.50, 0.80, 1.00, 1.10, 1.20, 1.40, 1.60 };
            FemCppRunResult? projectRun = null;
            string? projectDat = null;

            var rows = new List<SweepRow>();
            foreach (var f in sweep)
            {
                var baseName = $"attached_slope_t3_srf_F{(int)Math.Round(f * 100):000}";
                var dat = BuildDat(f);
                File.WriteAllText(Path.Combine(outDir, baseName + ".dat"), dat);

                var run = RunDirectT3Srf(repoRoot, outDir, baseName);

                rows.Add(new SweepRow(
                    f,
                    MaxDisplacement(run.JsonResult),
                    run.JsonResult.Meta?.SrfFoS,
                    run.JsonResult.Meta?.SrfMaxItersUsed,
                    run.JsonResult.Meta?.SrfMaxDisplacement,
                    run.ResSummary.Trials.Count,
                    run.Stdout.Trim()));

                if (Math.Abs(f - 1.0) < 1e-9)
                {
                    projectRun = run;
                    projectDat = dat;
                }
            }

            if (projectRun == null || projectDat == null)
                throw new InvalidOperationException("Internal error: F=1.0 project run was not captured.");

            var vm = new MainWindowViewModel();
            vm.ProjectSettingsVM.Project.ProjectName = "Attached slope T3 SRF";
            vm.ProjectSettingsVM.Project.Units = "SI (m, kPa, kN/m3)";

            using (vm.ScriptVM.SuspendDatRefresh())
            {
                vm.ScriptVM.ApplySyncedScriptDeck(projectDat, "T3");
                vm.ScriptVM.SyncFromScript();
            }

            var imported = P610T3DatImporter.Import(
                projectDat,
                vm.MaterialPropertiesVM,
                vm.BoundaryConditionsVM,
                null,
                vm.GeometryDefinitionVM,
                vm.FEAnalysisVM);

            if (imported == null)
                throw new InvalidOperationException("Generated T3 deck did not import through P610T3DatImporter.");

            if (!DatMeshBoundaryImporter.TryImportToViewModels(
                    projectDat,
                    vm.MeshVM.UserDefinedMeshVM,
                    vm.BoundaryConditionsVM,
                    out var meshSummary))
                throw new InvalidOperationException("Generated T3 deck did not import through DatMeshBoundaryImporter.");

            vm.GeometryDefinitionVM.ImportBoundaryFromMesh();
            vm.MaterialPropertiesVM.SelectedAnalysisCategory = MaterialPropertiesViewModel.SrfAnalysisCategory;
            vm.ResultsVM.LoadP610T3Result(
                projectRun.JsonResult,
                $"Attached slope SRF run restored from automation ({meshSummary.NNodes} nodes, {meshSummary.NElements} T3 elements).",
                projectDat,
                vm.MeshVM);
            vm.ResultsVM.SetFemCppArtifactSummary(projectRun.Stdout);
            vm.ResultsVM.ApplyP64ResSummary(projectRun.ResSummary, projectDat);

            var xfemPath = Path.Combine(outDir, "attached_slope_t3_srf.xfem");
            XfemProjectPersistence.Save(vm, xfemPath);

            var summaryPath = Path.Combine(outDir, "attached_slope_t3_srf_summary.md");
            File.WriteAllText(summaryPath, BuildSummary(rows, xfemPath));

            Console.WriteLine($"Saved {xfemPath}");
            Console.WriteLine($"Wrote {summaryPath}");
            foreach (var row in rows)
                Console.WriteLine($"F={row.F:G3} max|u|={row.MaxDisplacement:E4} trials={row.TrialCount}");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static string BuildDat(double srfF)
    {
        var nodes = BuildNodes();
        var elements = BuildElements();
        var bcs = BuildBoundaryConditions(nodes);
        var outputNodes = new[]
        {
            NodeId(20, Ny),
            NodeId(40, Ny),
            NodeId(60, Ny)
        };

        var sb = new StringBuilder();
        sb.AppendLine("! <solver:p610_t3>");
        sb.AppendLine("! Attached slope stability problem - UI automation generated native T3 mesh");
        sb.AppendLine("! Geometry: w1=20, s1=20, w2=20, h1=10, h2=5 m; polygon (0,0)-(60,0)-(60,5)-(40,5)-(20,15)-(0,15)");
        sb.AppendLine("! Material: phi=20 deg, c=15 kPa, gamma=20 kN/m3, E=100000 kPa, nu=0.3");
        sb.AppendLine("! Boundary conditions: fixed base, roller left/right sides");
        sb.AppendLine("! <BEGIN:AnalysisOptions>");
        sb.AppendLine("! failure_criterion MohrCoulomb");
        sb.AppendLine("! material_type Isotropic");
        sb.AppendLine("! mesh_type T3");
        sb.AppendLine("! mesh_source explicit");
        sb.AppendLine("! solver_executable slope_stability_T3_SRF");
        sb.AppendLine("! srf_mode single");
        sb.AppendLine(C($"! srf_F {srfF}"));
        sb.AppendLine("! expected_FoS_mode srf");
        sb.AppendLine("! slope_geometry attached_w1_20_s1_20_w2_20_h1_10_h2_5_t3");
        sb.AppendLine("! slope_angle_deg 26.5650511771");
        sb.AppendLine(C($"! slope_height_m {H1}"));
        sb.AppendLine("! <END:AnalysisOptions>");
        sb.AppendLine(C($"{elements.Count}  {nodes.Count}  1"));
        sb.AppendLine(C($"{PhiDeg}  {CohesionKPa}  0  {Gamma}  {YoungsModulusKPa}  {PoissonsRatio}  {K0}"));

        foreach (var node in nodes)
            sb.AppendLine(C($"{node.X}  {node.Y}"));
        foreach (var element in elements)
            sb.AppendLine($"{element.N1}  {element.N2}  {element.N3}");

        sb.AppendLine(bcs.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var bc in bcs)
            sb.AppendLine($"{bc.NodeId}  {bc.FixX}  {bc.FixY}");

        sb.AppendLine(outputNodes.Length.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine(string.Join("  ", outputNodes));
        sb.AppendLine("0.02  1000  20  1");
        sb.AppendLine("0");
        return sb.ToString();
    }

    private static FemCppRunResult RunDirectT3Srf(string repoRoot, string workDir, string baseName)
    {
        var exe = Path.Combine(repoRoot, "library", "solver", "SRF", "slope_stability_T3_SRF.exe");
        if (!File.Exists(exe))
            throw new FileNotFoundException("T3 SRF solver executable was not found.", exe);

        var startInfo = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = baseName,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start T3 SRF solver.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        var combined = string.IsNullOrWhiteSpace(stderr) ? stdout : stdout + Environment.NewLine + stderr;

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"slope_stability_T3_SRF failed with exit code {process.ExitCode}.{Environment.NewLine}{combined}");

        var datPath = Path.Combine(workDir, baseName + ".dat");
        var resPath = Path.Combine(workDir, baseName + ".res");
        var jsonPath = Path.Combine(workDir, baseName + ".json");
        if (!File.Exists(resPath))
            throw new InvalidOperationException($"Result file not created: {resPath}");
        if (!File.Exists(jsonPath))
            throw new InvalidOperationException($"JSON result file not created: {jsonPath}");

        return new FemCppRunResult
        {
            ProgramId = "p610_t3",
            BaseName = baseName,
            WorkDir = workDir,
            Stdout = combined,
            JsonResult = P610T3JsonParser.ParseFile(jsonPath),
            ResSummary = P64ResFileParser.ParseFile(resPath),
        };
    }

    private static List<NodeRow> BuildNodes()
    {
        var nodes = new List<NodeRow>((Nx + 1) * (Ny + 1));
        for (var i = 0; i <= Nx; i++)
        {
            var x = i;
            var top = TopY(x);
            for (var j = 0; j <= Ny; j++)
            {
                var y = top * j / Ny;
                nodes.Add(new NodeRow(NodeId(i, j), x, y));
            }
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

    private static List<BcRow> BuildBoundaryConditions(IReadOnlyList<NodeRow> nodes)
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

    private static int NodeId(int i, int j) => i * (Ny + 1) + j + 1;

    private static double MaxDisplacement(P610T3RunResult result)
    {
        double max = 0;
        foreach (var stage in result.Stages)
        {
            foreach (var (ux, uy) in stage.DisplacementsByNodeIndex)
                max = Math.Max(max, Math.Sqrt(ux * ux + uy * uy));
        }
        return max;
    }

    private static string BuildSummary(IReadOnlyList<SweepRow> rows, string xfemPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Attached Slope T3 SRF Automation Summary");
        sb.AppendLine();
        sb.AppendLine("Geometry from the attached sketch: `w1=20`, `s1=20`, `w2=20`, `h1=10`, `h2=5` m.");
        sb.AppendLine("Material: Mohr-Coulomb, `phi=20 deg`, `c=15 kPa`, `gamma=20 kN/m3`, `E=100000 kPa`, `nu=0.3`.");
        sb.AppendLine("Mesh: native `T3`, 60 by 15 mapped structured divisions, fixed base and roller sides.");
        sb.AppendLine();
        sb.AppendLine($"Saved project: `{xfemPath}`");
        sb.AppendLine();
        sb.AppendLine("| SRF F | JSON FoS | max displacement | SRF max disp | max iters | result trials |");
        sb.AppendLine("|---:|---:|---:|---:|---:|---:|");
        foreach (var row in rows)
            sb.AppendLine(C($"| {row.F} | {row.JsonFoS ?? double.NaN} | {row.MaxDisplacement:E6} | {row.SrfMaxDisplacement ?? double.NaN:E6} | {row.SrfMaxItersUsed ?? double.NaN} | {row.TrialCount} |"));
        return sb.ToString();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CMakeLists.txt"))
                && Directory.Exists(Path.Combine(dir.FullName, "X2D_GUI")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static string C(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);

    private sealed record NodeRow(int Id, double X, double Y);
    private sealed record ElementRow(int Id, int N1, int N2, int N3);
    private sealed record BcRow(int NodeId, int FixX, int FixY);
    private sealed record SweepRow(
        double F,
        double MaxDisplacement,
        double? JsonFoS,
        double? SrfMaxItersUsed,
        double? SrfMaxDisplacement,
        int TrialCount,
        string Stdout);
}
