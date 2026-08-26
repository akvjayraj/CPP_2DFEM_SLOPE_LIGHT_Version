using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Persistence;
using GeotechSlopeApp.ViewModels;
using GeotechSlopeApp.Views;

namespace RS2Example14RuUiAutomation;

/// <summary>
/// RS2 Part 1 §14 — Simple Slope with Ru (Spencer/Baker).
/// Meshes the .xfem, exports a p610 Ru deck, runs discrete SRF trials.
/// Target literature SSR FoS ≈ 0.98.
/// </summary>
internal static class Program
{
    private const double LiteratureFoS = 0.98;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            double[] trials = args.Length > 0
                ? args.Select(a => double.Parse(a, CultureInfo.InvariantCulture)).ToArray()
                : new[] { 0.55, 0.85, 0.95, 1.00, 1.05, 1.15 };

            var repoRoot = FindRepoRoot();
            var verificationDir = Path.Combine(
                repoRoot,
                "Verification",
                "slope",
                "Groundwater",
                "RS2_Example14_simple_slope_with_Ru");
            var projectPath = Path.Combine(verificationDir, "rs2_example14_simple_slope_with_Ru.xfem");
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
            vm.ProjectSettingsVM.GroundwaterStaticSettings.IncludeRuCoefficients = true;
            vm.GroundwaterVM.Method = GroundwaterMethod.Ru;
            vm.GroundwaterVM.Ru = 0.5;
            Console.WriteLine($"Groundwater On — method={vm.GroundwaterVM.Method}, Ru={vm.GroundwaterVM.Ru:F2}");

            vm.MaterialPropertiesVM.SelectedAnalysisCategory = MaterialPropertiesViewModel.SrfAnalysisCategory;
            EnsureSurfaceMesh(app, vm);
            EnsureSlopeBoundaryConditions(vm);

            bool synced = vm.ScriptVM.TrySyncDiscretizedMeshToScriptDat(ignoreLoadedDeckPreserve: true);
            vm.ScriptVM.SyncGroundwaterToScript();
            Pump(app);
            Console.WriteLine($"Script synced={synced}");

            string dat = vm.ScriptVM.GetScriptContentForSolverRun();
            if (!dat.Contains("! method ru", StringComparison.OrdinalIgnoreCase)
                && !dat.Contains("method ru", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Exported .dat missing Ru groundwater method.");
            if (!dat.Contains("! Ru 0.5", StringComparison.OrdinalIgnoreCase)
                && !dat.Contains("Ru 0.5", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Exported .dat missing Ru 0.5.");

            string solver = Path.Combine(repoRoot, "library", "solver", "slope_stability_T3_SRF.exe");
            if (!File.Exists(solver))
                throw new FileNotFoundException("slope_stability_T3_SRF.exe not found.", solver);

            string workRoot = Path.Combine(outputDir, "t3_trials");
            Directory.CreateDirectory(workRoot);
            File.WriteAllText(Path.Combine(outputDir, "ex14_ru_base.dat"), dat);

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
                    trialRows.Add(new TrialResult(f, false, 0, null, null, null, sw.Elapsed.TotalSeconds));
                    continue;
                }

                using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
                var root = doc.RootElement;
                double? yield = null, umax = null;
                int iters = 1000;
                bool? allInc = null;
                // p610 branded SRF writes diagnostics under meta.srf (not meta root).
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

                Console.WriteLine(
                    $"F={f:F3}: gate={gate} iters={iters} umax={umax?.ToString("G4") ?? "n/a"} " +
                    $"yield={yield?.ToString("G4") ?? "n/a"} allInc={allInc?.ToString() ?? "n/a"} " +
                    $"({sw.Elapsed.TotalSeconds:F1}s)");

                trialRows.Add(new TrialResult(f, gate, iters, umax, yield, allInc, sw.Elapsed.TotalSeconds));
            }

            double? fos = EstimateFoSFromTrials(trialRows);
            var summary = new
            {
                GeneratedAt = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
                LiteratureFoS,
                EstimatedFoS = fos,
                DeviationPercent = fos.HasValue ? 100.0 * (fos.Value - LiteratureFoS) / LiteratureFoS : (double?)null,
                NodeCount = vm.MeshVM.DiscretizationNodes.Count,
                ElementCount = vm.MeshVM.MeshElements.Count,
                Trials = trialRows,
            };

            string jsonOut = Path.Combine(outputDir, "rs2_example14_ru_gui_run_summary.json");
            string mdOut = Path.Combine(outputDir, "rs2_example14_ru_gui_run_summary.md");
            File.WriteAllText(jsonOut, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(mdOut, BuildMarkdown(summary.LiteratureFoS, fos, trialRows));
            Console.WriteLine($"Wrote {jsonOut}");
            Console.WriteLine($"T3 Ru: FoS={(fos?.ToString("F3", CultureInfo.InvariantCulture) ?? "n/a")} (lit {LiteratureFoS:F2})");

            XfemProjectPersistence.Save(vm, projectPath);
            app.Shutdown();

            // Accept 0.85–1.15 (~±15% of 0.98) as verification pass.
            return fos is double v && v >= 0.85 && v <= 1.15 ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static double? EstimateFoSFromTrials(List<TrialResult> trials)
    {
        // Highest converged F and lowest failed F → mid-bracket estimate.
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
            return onlyOk; // all converged — FoS above range
        if (lastOk is null && firstFail is double onlyFail)
            return onlyFail; // all failed — FoS at/below lowest trial
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
            // L1 left wall, L5 right wall → Roller X; L6 base → Fixed
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
        // Coarser than 2.0 to keep SRF trials tractable.
        vm.MeshVM.GlobalMeshSize = "3.0";
        vm.MeshVM.NearBoundaryMeshSize = "1.0";

        Console.WriteLine("Generating surface mesh...");
        vm.MeshVM.GenerateSurfaceMeshCommand.Execute(null);
        Pump(app, cycles: 30);

        if (!vm.MeshVM.IsDiscretized || vm.MeshVM.MeshInformationRows.Count == 0)
            throw new InvalidOperationException($"Surface mesh generation failed: {vm.MeshVM.SurfaceMeshStatus}");

        Console.WriteLine(
            $"Mesh ready: {vm.MeshVM.MeshElements.Count} elements, {vm.MeshVM.DiscretizationNodes.Count} nodes");
    }

    private static string BuildMarkdown(double lit, double? fos, List<TrialResult> trials)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# RS2 Example 14 Ru (Spencer/Baker) SRF Trial Summary");
        sb.AppendLine();
        sb.AppendLine($"Literature SSR FoS: `{lit:F2}`");
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

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed record TrialResult(
        double F,
        bool GateConverged,
        int Iterations,
        double? MaxDisplacement,
        double? MaxYieldRatio,
        bool? AllIncrementsConverged,
        double Seconds);
}
