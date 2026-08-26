using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Persistence;
using GeotechSlopeApp.ViewModels;
using GeotechSlopeApp.Views;

namespace RS2Example06SingleTrial;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            double[] trials = args.Length > 0
                ? args.Select(a => double.Parse(a, CultureInfo.InvariantCulture)).ToArray()
                : new[] { 0.1, 0.5, 1.0 };

            var repoRoot = FindRepoRoot();
            var projectPath = Path.Combine(
                repoRoot,
                "Verification",
                "slope",
                "Groundwater",
                "RS2_Example06_slope_load_piezometric_water_table",
                "RS2 Example 6 - Slope with Load and Pore Pressure Defined by Water Table (ACADS 4)_SRF.xfem");

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
            var window = new MainWindow();
            window.Show();
            window.WindowState = WindowState.Minimized;
            if (window.DataContext is not MainWindowViewModel vm)
                throw new InvalidOperationException("No VM");

            window.PrepareForAutomationProjectLoad();
            Pump(app);
            XfemProjectPersistence.Load(vm, projectPath);
            vm.CurrentXfemProjectPath = projectPath;
            vm.ProjectWorkspace.UpdateActiveSlotFromVm(vm, projectPath);
            vm.RefreshStageFooterAfterProjectLoad();
            Pump(app);

            // Confirm Overview axis conversion for crest
            var geom = vm.GeometryDefinitionVM;
            foreach (var load in vm.LoadConditionsVM.Loads)
            {
                var a = geom.ConvertCanvasToAxis(new Point(load.StartX, load.StartY));
                var b = geom.ConvertCanvasToAxis(new Point(load.EndX, load.EndY));
                Console.WriteLine(
                    $"LOAD {load.Name}: axis ({a.X:G4},{a.Y:G4})->({b.X:G4},{b.Y:G4}) " +
                    $"axisBox Y={geom.AxisYMin:G4}..{geom.AxisYMax:G4}");
            }

            EnsureBcs(vm);
            vm.ScriptVM.TrySyncDiscretizedMeshToScriptDat(ignoreLoadedDeckPreserve: true);
            vm.ScriptVM.SyncGroundwaterToScript();
            vm.ScriptVM.SyncDistributedEdgeLoadsToScript();
            Pump(app);

            string dat = vm.ScriptVM.GetScriptContentForSolverRun();
            Console.WriteLine(
                $"Deck: nodes={vm.MeshVM.DiscretizationNodes.Count} els={vm.MeshVM.MeshInformationRows.Count} " +
                $"bcs={vm.BoundaryConditionsVM.BoundaryConditions.Count} " +
                $"edgeLoads={DistributedEdgeLoadDatWriter.CollectGroups(vm.LoadConditionsVM, vm.MeshVM, geom).Sum(g => g.Edges.Count)}");

            string solver = Path.Combine(repoRoot, "library", "solver", "slope_stability_T3_SRF.exe");
            string workRoot = Path.Combine(Path.GetTempPath(), "ex06_single_trials");
            Directory.CreateDirectory(workRoot);

            var results = new List<object>();
            foreach (double f in trials)
            {
                if (!P610AnalysisOptionsHelper.TryPatchSrfFSingleTrial(dat, f, out var trialDat, out _))
                    throw new InvalidOperationException($"patch failed F={f}");

                string tag = "f_" + f.ToString("G4", CultureInfo.InvariantCulture).Replace('.', '_');
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
                using var proc = Process.Start(psi) ?? throw new InvalidOperationException("start failed");
                proc.StandardOutput.ReadToEnd();
                proc.StandardError.ReadToEnd();
                proc.WaitForExit();
                sw.Stop();

                string jsonPath = Path.Combine(dir, "run.json");
                if (!File.Exists(jsonPath))
                {
                    Console.WriteLine($"F={f:G4}: NO JSON exit={proc.ExitCode}");
                    results.Add(new { F = f, Ok = false });
                    continue;
                }

                using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
                var root = doc.RootElement;
                double? yield = null, umax = null, iters = null;
                bool? allInc = null;
                if (root.TryGetProperty("meta", out var meta))
                {
                    if (meta.TryGetProperty("max_yield_ratio", out var y) && y.ValueKind == JsonValueKind.Number)
                        yield = y.GetDouble();
                    if (meta.TryGetProperty("max_disp", out var d) && d.ValueKind == JsonValueKind.Number)
                        umax = d.GetDouble();
                    if (meta.TryGetProperty("max_iters_used", out var it) && it.ValueKind == JsonValueKind.Number)
                        iters = it.GetDouble();
                    if (meta.TryGetProperty("all_increments_converged", out var ac))
                        allInc = ac.ValueKind == JsonValueKind.True;
                }

                int itersI = (int)Math.Round(iters ?? 1000);
                bool gate = SlopeSrfTrialExecutor.ClassifyTrialConverged(
                    itersI,
                    1000,
                    umax ?? double.NaN,
                    umax is null || !double.IsFinite(umax.Value),
                    allInc,
                    yield);

                Console.WriteLine(
                    $"F={f:G4}: gate={gate} iters={itersI} umax={umax?.ToString("G4") ?? "n/a"} " +
                    $"yield={yield?.ToString("G4") ?? "n/a"} (cap={SlopeSrfTrialExecutor.MaxStableYieldRatio}) " +
                    $"allInc={allInc?.ToString() ?? "n/a"} ({sw.Elapsed.TotalSeconds:F1}s)");

                results.Add(new
                {
                    F = f,
                    GateConverged = gate,
                    Iterations = itersI,
                    MaxDisplacement = umax,
                    MaxYieldRatio = yield,
                    AllIncrementsConverged = allInc,
                    Seconds = sw.Elapsed.TotalSeconds,
                });
            }

            var outPath = Path.Combine(Path.GetDirectoryName(projectPath)!, "gui_automation", "rs2_example06_single_trial_diag.json");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.WriteAllText(outPath, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Wrote {outPath}");
            app.Shutdown();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void EnsureBcs(MainWindowViewModel vm)
    {
        var bcVm = vm.BoundaryConditionsVM;
        if (bcVm.BoundaryConditions.Count > 0)
            return;
        bcVm.ClearBoundaryConditionsCommand.Execute(null);
        bcVm.ImportOuterBoundariesCommand.Execute(null);
        foreach (var boundary in bcVm.AvailableOuterBoundaries.ToList())
        {
            string? bcType = boundary.LineNumber switch
            {
                1 => "Fixed",
                2 => "Roller X",
                6 => "Roller X",
                _ => null
            };
            if (bcType == null || boundary.NodeIds.Count == 0) continue;
            bcVm.SelectedOuterBoundary = boundary;
            bcVm.SelectedBCTypeForBoundary = bcType;
            bcVm.BcValueForBoundary = "0";
            bcVm.NodesText = string.Empty;
            bcVm.ApplyBCToSelectedBoundaryCommand.Execute(null);
        }
    }

    private static void Pump(Application app, int n = 5)
    {
        for (int i = 0; i < n; i++)
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
                && Directory.Exists(Path.Combine(dir.FullName, "X2D_GUI")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("repo root not found");
    }
}
