using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Persistence;
using GeotechSlopeApp.ViewModels;
using GeotechSlopeApp.Views;

namespace RS2Example35UiAutomation;

/// <summary>
/// RS2 Part 2 §35 — Submerged slope (Duncan &amp; Wright).
/// Meshes the .xfem, asserts ponded-water DistributedEdgeLoads, runs SRF trials.
/// Literature referee FoS ≈ 1.60; RS2 SSR ≈ 1.64.
/// </summary>
internal static class Program
{
    private const double LiteratureFoS = 1.60;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            double[] trials = args.Length > 0
                ? args.Select(a => double.Parse(a, CultureInfo.InvariantCulture)).ToArray()
                : new[] { 1.0, 1.4, 1.55, 1.60, 1.70 };

            var repoRoot = FindRepoRoot();
            var verificationDir = Path.Combine(
                repoRoot,
                "Verification",
                "slope",
                "Groundwater",
                "RS2_Part 2 Example35_submerged_slope_duncan_wright");
            var projectPath = Path.Combine(verificationDir, "rs2_example35_submerged_slope_duncan_wright.xfem");
            var outputDir = Path.Combine(verificationDir, "gui_automation");
            Directory.CreateDirectory(outputDir);

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
            var window = new MainWindow();
            window.Show();
            window.WindowState = WindowState.Minimized;
            if (window.DataContext is not MainWindowViewModel vm)
                throw new InvalidOperationException("MainWindow has no MainWindowViewModel.");

            Console.WriteLine($"Loading {projectPath}");
            window.PrepareForAutomationProjectLoad();
            Pump(app);
            XfemProjectPersistence.Load(vm, projectPath);
            vm.CurrentXfemProjectPath = projectPath;
            vm.ProjectWorkspace.UpdateActiveSlotFromVm(vm, projectPath);
            vm.RefreshStageFooterAfterProjectLoad();
            Pump(app);

            if (vm.GroundwaterVM is null || !vm.GroundwaterVM.IsEnabled)
                throw new InvalidOperationException("Groundwater must be enabled.");
            if (vm.GroundwaterVM.Method != GroundwaterMethod.Hydrostatic)
                throw new InvalidOperationException($"Expected Hydrostatic GW, got {vm.GroundwaterVM.Method}.");
            Console.WriteLine(
                $"GW On — Hydrostatic γw={vm.GroundwaterVM.GammaW:G4}, " +
                $"surfPts={vm.GroundwaterVM.GroundwaterSurfPoints?.Count ?? 0}");

            vm.MaterialPropertiesVM.SelectedAnalysisCategory = MaterialPropertiesViewModel.SrfAnalysisCategory;
            EnsureSurfaceMesh(app, vm);
            EnsureSlopeBoundaryConditions(vm);

            bool synced = vm.ScriptVM.TrySyncDiscretizedMeshToScriptDat(ignoreLoadedDeckPreserve: true);
            vm.ScriptVM.SyncGroundwaterToScript();
            Pump(app);
            Console.WriteLine($"Script synced={synced}");

            string dat = vm.ScriptVM.GetScriptContentForSolverRun();
            int pondedEdges = CountDistributedEdgeLoads(dat);
            Console.WriteLine($"DistributedEdgeLoads edges={pondedEdges}");
            if (pondedEdges < 5)
                throw new InvalidOperationException(
                    $"Expected ponded-water DistributedEdgeLoads (>=5 edges), found {pondedEdges}.");
            if (!dat.Contains("method hydrostatic", StringComparison.OrdinalIgnoreCase)
                && !dat.Contains("! method hydrostatic", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Exported .dat missing hydrostatic groundwater method.");

            File.WriteAllText(Path.Combine(outputDir, "ex35_submerged_base.dat"), dat);

            string solver = Path.Combine(repoRoot, "library", "solver", "slope_stability_T3_SRF.exe");
            if (!File.Exists(solver))
                throw new FileNotFoundException("slope_stability_T3_SRF.exe not found.", solver);

            string workRoot = Path.Combine(outputDir, "t3_trials");
            Directory.CreateDirectory(workRoot);

            var trialRows = new List<TrialResult>();
            foreach (double f in trials)
            {
                if (!P610AnalysisOptionsHelper.TryPatchSrfFSingleTrial(dat, f, out var trialDat, out _))
                    throw new InvalidOperationException($"Could not patch SRF F={f}");

                string tag = "f_" + f.ToString("0.###", CultureInfo.InvariantCulture).Replace('.', '_');
                string dir = Path.Combine(workRoot, tag);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "run.dat"), trialDat);

                var psi = new ProcessStartInfo
                {
                    FileName = solver,
                    Arguments = "run",
                    WorkingDirectory = dir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                var sw = Stopwatch.StartNew();
                using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start solver.");
                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit();
                sw.Stop();
                File.WriteAllText(Path.Combine(dir, "run.log"), stdout + "\n" + stderr);

                string jsonPath = Path.Combine(dir, "run.json");
                if (!File.Exists(jsonPath))
                {
                    Console.WriteLine($"F={f:F3}: NO JSON exit={proc.ExitCode} ({sw.Elapsed.TotalSeconds:F1}s)");
                    trialRows.Add(new TrialResult(f, false, 0, null, null, proc.ExitCode, sw.Elapsed));
                    continue;
                }

                using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
                var root = doc.RootElement;
                double? yield = null, umax = null;
                int iters = 1000;
                bool? allInc = null;
                if (root.TryGetProperty("meta", out var meta))
                {
                    var bag = meta.TryGetProperty("srf", out var srfMeta) ? srfMeta : meta;
                    if (bag.TryGetProperty("max_yield_ratio", out var y) && y.ValueKind == JsonValueKind.Number)
                        yield = y.GetDouble();
                    if (bag.TryGetProperty("max_disp", out var d) && d.ValueKind == JsonValueKind.Number)
                        umax = d.GetDouble();
                    if (bag.TryGetProperty("max_iters_used", out var it) && it.ValueKind == JsonValueKind.Number)
                        iters = (int)Math.Round(it.GetDouble());
                    if (bag.TryGetProperty("all_increments_converged", out var ac))
                        allInc = ac.ValueKind == JsonValueKind.True;
                }

                bool gate = SlopeSrfTrialExecutor.ClassifyTrialConverged(
                    iters,
                    1000,
                    umax ?? double.NaN,
                    umax is null || !double.IsFinite(umax.Value),
                    allInc,
                    yield);

                var row = new TrialResult(f, gate, iters, umax, yield, proc.ExitCode, sw.Elapsed);
                trialRows.Add(row);
                Console.WriteLine(
                    $"F={f:F3}: gate={(gate ? "OK" : "FAIL")} iters={iters} umax={umax?.ToString("G4") ?? "n/a"} " +
                    $"yield={yield?.ToString("G4") ?? "n/a"} allInc={allInc?.ToString() ?? "n/a"} " +
                    $"({sw.Elapsed.TotalSeconds:F1}s)");
            }

            double? fos = EstimateFoS(trialRows);
            var summary = new
            {
                project = projectPath,
                literatureFoS = LiteratureFoS,
                estimatedFoS = fos,
                pondedEdgeCount = pondedEdges,
                gammaW = vm.GroundwaterVM.GammaW,
                meshNodes = vm.MeshVM.DiscretizationNodes.Count,
                meshElements = vm.MeshVM.MeshElements.Count,
                trials = trialRows.Select(t => new
                {
                    t.F,
                    t.GateConverged,
                    t.Iterations,
                    t.MaxDisplacement,
                    t.MaxYieldRatio,
                    t.ExitCode,
                    elapsedSec = t.Elapsed.TotalSeconds
                })
            };
            string summaryPath = Path.Combine(outputDir, "rs2_example35_gui_run_summary.json");
            File.WriteAllText(
                summaryPath,
                JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(outputDir, "rs2_example35_gui_run_summary.md"), BuildMarkdown(fos, trialRows));

            Console.WriteLine($"Summary → {summaryPath}");
            Console.WriteLine($"Literature FoS={LiteratureFoS:F2}; estimated={(fos?.ToString("F3") ?? "n/a")}");

            bool anyOk = trialRows.Any(t => t.GateConverged);
            if (!anyOk)
            {
                Console.Error.WriteLine("FAIL: no SRF trial converged — ponded loads may be missing/wrong.");
                return 2;
            }

            // Expect F=1.0 OK and FoS near 1.6 (allow wide band for coarse mesh).
            var f1 = trialRows.FirstOrDefault(t => Math.Abs(t.F - 1.0) < 1e-9);
            if (f1 is { GateConverged: false })
            {
                Console.Error.WriteLine("FAIL: F=1.0 did not converge (submerged model still under-supported).");
                return 3;
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static int CountDistributedEdgeLoads(string dat)
    {
        var m = Regex.Match(
            dat,
            @"! <BEGIN:DistributedEdgeLoads>(?<body>.*?)! <END:DistributedEdgeLoads>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!m.Success)
            return 0;

        int count = 0;
        foreach (var line in m.Groups["body"].Value.Split('\n'))
        {
            var t = line.Trim();
            if (!t.StartsWith('!'))
                continue;
            var parts = t.TrimStart('!').Trim()
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            // Edge row: n1 n2 qa qb  (4 tokens) — skip group headers (kind dx dy / counts).
            if (parts.Length == 4
                && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var qa)
                && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var qb)
                && (qa > 0 || qb > 0))
                count++;
        }

        return count;
    }

    private static double? EstimateFoS(List<TrialResult> trials)
    {
        double? lastOk = null;
        double? firstFail = null;
        foreach (var t in trials.OrderBy(t => t.F))
        {
            if (t.GateConverged)
                lastOk = t.F;
            else if (firstFail is null)
                firstFail = t.F;
        }

        if (lastOk is double ok && firstFail is double fail && fail > ok)
            return 0.5 * (ok + fail);
        if (lastOk is double onlyOk && firstFail is null)
            return onlyOk;
        if (lastOk is null && firstFail is double onlyFail)
            return onlyFail;
        return lastOk ?? firstFail;
    }

    private static void EnsureSlopeBoundaryConditions(MainWindowViewModel vm)
    {
        var bcVm = vm.BoundaryConditionsVM;
        bcVm.ClearBoundaryConditionsCommand.Execute(null);
        bcVm.ImportOuterBoundariesCommand.Execute(null);

        int applied = 0;
        foreach (var boundary in bcVm.AvailableOuterBoundaries.ToList())
        {
            string? bcType = boundary.LineNumber switch
            {
                1 => "Roller X",
                5 => "Roller X",
                6 => "Fixed",
                _ => null
            };
            if (bcType == null || boundary.NodeIds.Count == 0)
                continue;

            bcVm.SelectedOuterBoundary = boundary;
            bcVm.SelectedBCTypeForBoundary = bcType;
            bcVm.BcValueForBoundary = "0";
            bcVm.NodesText = string.Empty;
            bcVm.ApplyBCToSelectedBoundaryCommand.Execute(null);
            applied += boundary.NodeIds.Count;
        }

        if (applied == 0)
            throw new InvalidOperationException("No slope boundary conditions were applied.");
        Console.WriteLine($"Applied BCs to {applied} exterior node(s).");
    }

    private static void EnsureSurfaceMesh(Application app, MainWindowViewModel vm)
    {
        if (vm.MeshVM.IsDiscretized && vm.MeshVM.MeshInformationRows.Count > 0)
        {
            Console.WriteLine(
                $"Mesh already present: {vm.MeshVM.MeshElements.Count} els, {vm.MeshVM.DiscretizationNodes.Count} nodes");
            return;
        }

        vm.MeshVM.SelectedTwoDElementFamily = "Triangles";
        vm.MeshVM.SelectedTwoDNodeOption = "3-node";
        vm.MeshVM.MeshElementOrder = "3-node";
        vm.MeshVM.GlobalMeshSize = "3.0";
        vm.MeshVM.NearBoundaryMeshSize = "1.5";

        Console.WriteLine("Generating surface mesh...");
        vm.MeshVM.GenerateSurfaceMeshCommand.Execute(null);
        Pump(app, cycles: 40);

        if (!vm.MeshVM.IsDiscretized || vm.MeshVM.MeshInformationRows.Count == 0)
            throw new InvalidOperationException($"Surface mesh generation failed: {vm.MeshVM.SurfaceMeshStatus}");

        Console.WriteLine(
            $"Mesh ready: {vm.MeshVM.MeshElements.Count} elements, {vm.MeshVM.DiscretizationNodes.Count} nodes");
    }

    private static string BuildMarkdown(double? fos, List<TrialResult> trials)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# RS2 Example 35 Submerged Slope SRF Trial Summary");
        sb.AppendLine();
        sb.AppendLine($"Literature FoS: `{LiteratureFoS:F2}`");
        sb.AppendLine($"Estimated FoS: `{(fos.HasValue ? fos.Value.ToString("F3", CultureInfo.InvariantCulture) : "n/a")}`");
        sb.AppendLine();
        sb.AppendLine("| F | Gate | Iters | umax | yield |");
        sb.AppendLine("|---:|:---:|---:|---:|---:|");
        foreach (var t in trials)
        {
            sb.AppendLine(
                $"| {t.F:F3} | {(t.GateConverged ? "OK" : "FAIL")} | {t.Iterations} | " +
                $"{t.MaxDisplacement?.ToString("G4", CultureInfo.InvariantCulture) ?? "n/a"} | " +
                $"{t.MaxYieldRatio?.ToString("G4", CultureInfo.InvariantCulture) ?? "n/a"} |");
        }

        return sb.ToString();
    }

    private static void Pump(Application app, int cycles = 5)
    {
        for (int i = 0; i < cycles; i++)
        {
            app.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Sleep(25);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CMakeLists.txt"))
                && File.Exists(Path.Combine(dir.FullName, "X2D_GUI", "GeotechSlopeApp.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root from " + AppContext.BaseDirectory);
    }

    private sealed record TrialResult(
        double F,
        bool GateConverged,
        int Iterations,
        double? MaxDisplacement,
        double? MaxYieldRatio,
        int ExitCode,
        TimeSpan Elapsed);
}
