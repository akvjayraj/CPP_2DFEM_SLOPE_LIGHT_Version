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

namespace RS2Example06UiAutomation;

/// <summary>
/// RS2 Part 1 §6 / ACADS 4 — Slope with Load and Pore Pressure Defined by Water Table.
/// Literature SSR FoS = 0.69.
/// </summary>
internal static class Program
{
    private const double LiteratureFoS = 0.69;

    [STAThread]
    private static int Main()
    {
        try
        {
            var repoRoot = FindRepoRoot();
            var verificationDir = Path.Combine(
                repoRoot,
                "Verification",
                "slope",
                "Groundwater",
                "RS2_Example06_slope_load_piezometric_water_table");
            var outputDir = Path.Combine(verificationDir, "gui_automation");
            Directory.CreateDirectory(outputDir);

            // Prefer the SRF project matching the UI mesh (769 nodes / 1373 T3).
            var projectPath = Path.Combine(
                verificationDir,
                "RS2 Example 6 - Slope with Load and Pore Pressure Defined by Water Table (ACADS 4)_SRF.xfem");
            if (!File.Exists(projectPath))
            {
                projectPath = Path.Combine(
                    verificationDir,
                    "rs2_example06_slope_load_piezometric_water_table.xfem");
            }

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
            var window = new MainWindow();
            window.Show();
            window.WindowState = WindowState.Minimized;

            if (window.DataContext is not MainWindowViewModel vm)
                throw new InvalidOperationException("MainWindow has no MainWindowViewModel.");

            var result = RunCase(app, window, vm, projectPath, outputDir);

            var summary = new AutomationSummary
            {
                GeneratedAt = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
                LiteratureFoS = LiteratureFoS,
                ReferenceFoSSpencer = 0.707,
                ReferenceFoSGle = 0.683,
                ReferenceFoSJanbu = 0.700,
                Cases = new List<MeshRunResult> { result },
            };

            var jsonPath = Path.Combine(outputDir, "rs2_example06_gui_run_summary.json");
            var markdownPath = Path.Combine(outputDir, "rs2_example06_gui_run_summary.md");
            File.WriteAllText(
                jsonPath,
                JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(markdownPath, BuildMarkdown(summary));

            Console.WriteLine($"Wrote {jsonPath}");
            Console.WriteLine($"Wrote {markdownPath}");
            Console.WriteLine(
                $"T3: FoS={(result.EstimatedFoS?.ToString("F3", CultureInfo.InvariantCulture) ?? "n/a")} " +
                $"status={result.StatusMessage}");

            app.Shutdown();
            return result.EstimatedFoS.HasValue ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static MeshRunResult RunCase(
        Application app,
        MainWindow window,
        MainWindowViewModel vm,
        string projectPath,
        string outputDir)
    {
        Console.WriteLine($"Running RS2 Example 06 (ACADS 4) from {projectPath}");

        window.PrepareForAutomationProjectLoad();
        WaitForUiIdle(app);

        XfemProjectPersistence.Load(vm, projectPath);
        vm.CurrentXfemProjectPath = projectPath;
        vm.ProjectWorkspace.UpdateActiveSlotFromVm(vm, projectPath);
        vm.RefreshStageFooterAfterProjectLoad();
        WaitForUiIdle(app);

        EnsureGroundwaterEnabled(vm);
        EnsureTable2Loads(vm);
        ConfigureRunSettings(vm);
        EnsureSurfaceMesh(app, vm);
        EnsureSlopeBoundaryConditions(vm);

        // Confirm distributed-edge export hits mesh edges before solving.
        int edgeCount = CountExportedEdgeLoads(vm);
        Console.WriteLine($"DistributedEdgeLoad export: {edgeCount} mesh edge(s)");
        if (edgeCount == 0)
            throw new InvalidOperationException(
                "Table 2 surcharges did not map to any mesh boundary edges. Check load placement / mesh.");

        bool scriptSynced = vm.ScriptVM.TrySyncDiscretizedMeshToScriptDat(ignoreLoadedDeckPreserve: true);
        vm.ScriptVM.SyncGroundwaterToScript();
        vm.ScriptVM.SyncDistributedEdgeLoadsToScript();
        WaitForUiIdle(app);

        string datContent = vm.ScriptVM.GetScriptContentForSolverRun();
        EnsureGroundwaterInDat(datContent);
        EnsureLoadsInDat(datContent);

        string program = P610AnalysisOptionsHelper.ResolveRunnerExecutableForFeAnalysis(
            P610AnalysisOptionsHelper.TryParse(datContent, out var options)
                ? options
                : throw new InvalidOperationException("Could not parse AnalysisOptions."),
            vm.MeshVM,
            srfAnalysisMode: vm.MaterialPropertiesVM?.IsSrfAnalysisMode == true);

        var runStartedUtc = DateTime.UtcNow;
        vm.FEAnalysisVM.RunAnalysisCommand.Execute(null);
        WaitForAnalysisCompletion(app, vm, TimeSpan.FromMinutes(90));
        WaitForUiIdle(app);

        double? fos = vm.ResultsVM.EstimatedSrfFoS;
        string status = vm.FEAnalysisVM.StatusMessage ?? "";
        var trialRows = vm.ResultsVM.SrfTrialRows
            .Select(r => new TrialRow(r.Srf, r.MaxDisplacement, r.Iterations, !r.Failed))
            .ToList();

        var artifactDir = Path.Combine(outputDir, "t3");
        Directory.CreateDirectory(artifactDir);
        CopyRunArtifacts(program, runStartedUtc, artifactDir);

        var runSummary = new MeshRunResult
        {
            MeshType = "T3",
            ProjectPath = projectPath,
            Program = program,
            ScriptSyncedFromMesh = scriptSynced,
            GroundwaterEnabled = vm.GroundwaterVM.IsEnabled,
            LoadsEnabled = vm.LoadConditionsVM.Loads.Count(l => l.IsEnabled),
            ExportedEdgeLoadCount = edgeCount,
            EstimatedFoS = fos,
            DeviationFromLiterature = fos.HasValue ? fos.Value - LiteratureFoS : null,
            DeviationPercent = fos.HasValue ? 100.0 * (fos.Value - LiteratureFoS) / LiteratureFoS : null,
            StatusMessage = status,
            FosNote = "PDF Table 2 surcharges placed in axis metres (stored as canvas); yield-gate SRF bracket.",
            NodeCount = vm.MeshVM.DiscretizationNodes.Count,
            ElementCount = vm.MeshVM.MeshElements.Count,
            ArtifactDirectory = artifactDir,
            TrialRows = trialRows,
            LoadAxisSegments = DescribeLoads(vm),
        };

        File.WriteAllText(
            Path.Combine(artifactDir, "rs2_example06_t3_result.json"),
            JsonSerializer.Serialize(runSummary, new JsonSerializerOptions { WriteIndented = true }));

        XfemProjectPersistence.Save(vm, projectPath);
        return runSummary;
    }

    private static List<string> DescribeLoads(MainWindowViewModel vm)
    {
        var geom = vm.GeometryDefinitionVM;
        var list = new List<string>();
        foreach (var load in vm.LoadConditionsVM.Loads.Where(l => l.IsEnabled))
        {
            var a = geom.ConvertCanvasToAxis(new Point(load.StartX, load.StartY));
            var b = geom.ConvertCanvasToAxis(new Point(load.EndX, load.EndY));
            list.Add(
                $"{load.Name}: axis ({a.X:F2},{a.Y:F2})-({b.X:F2},{b.Y:F2}) " +
                $"q={load.Magnitude:G}-{load.EndMagnitude:G} kPa enabled={load.IsEnabled}");
            Console.WriteLine("  " + list[^1]);
        }
        return list;
    }

    private static void EnsureTable2Loads(MainWindowViewModel vm)
    {
        var geom = vm.GeometryDefinitionVM;
        // Exact PDF Table 2 endpoints in axis metres → canvas storage.
        var midA = geom.ConvertAxisToCanvas(new Point(23, 27.75));
        var midB = geom.ConvertAxisToCanvas(new Point(43, 27.75));
        var crestA = geom.ConvertAxisToCanvas(new Point(70, 40));
        var crestB = geom.ConvertAxisToCanvas(new Point(80, 40));

        vm.LoadConditionsVM.Loads.Clear();
        vm.LoadConditionsVM.Loads.Add(new LoadModel
        {
            Type = LoadType.Line,
            Direction = LoadDirection.Y,
            Orientation = LoadOrientation.NormalToBoundary,
            Magnitude = 20,
            EndMagnitude = 20,
            StartX = midA.X,
            StartY = midA.Y,
            EndX = midB.X,
            EndY = midB.Y,
            StageNumber = 1,
            Name = "Mid-bench surcharge 20 kPa",
            IsEnabled = true,
        });
        vm.LoadConditionsVM.Loads.Add(new LoadModel
        {
            Type = LoadType.Triangular,
            Direction = LoadDirection.Y,
            Orientation = LoadOrientation.NormalToBoundary,
            Magnitude = 20,
            EndMagnitude = 40,
            StartX = crestA.X,
            StartY = crestA.Y,
            EndX = crestB.X,
            EndY = crestB.Y,
            StageNumber = 1,
            Name = "Crest surcharge 20-40 kPa",
            IsEnabled = true,
        });

        var mid = vm.LoadConditionsVM.Loads[0];
        var midStart = geom.ConvertCanvasToAxis(new Point(mid.StartX, mid.StartY));
        var midEnd = geom.ConvertCanvasToAxis(new Point(mid.EndX, mid.EndY));
        if (Math.Abs(midStart.X - 23) > 0.05 || Math.Abs(midEnd.X - 43) > 0.05
            || Math.Abs(midStart.Y - 27.75) > 0.05)
            throw new InvalidOperationException(
                $"Mid-bench axis mismatch after place: ({midStart.X},{midStart.Y})-({midEnd.X},{midEnd.Y})");

        Console.WriteLine("Table 2 surcharges placed and enabled.");
        DescribeLoads(vm);
    }

    private static int CountExportedEdgeLoads(MainWindowViewModel vm)
    {
        var groups = DistributedEdgeLoadDatWriter.CollectGroups(
            vm.LoadConditionsVM, vm.MeshVM, vm.GeometryDefinitionVM);
        return groups.Sum(g => g.Edges.Count);
    }

    private static void EnsureGroundwaterEnabled(MainWindowViewModel vm)
    {
        if (vm.GroundwaterVM.GroundwaterSurfPoints == null
            || vm.GroundwaterVM.GroundwaterSurfPoints.Count < 2)
            throw new InvalidOperationException("Piezometric surface points missing.");

        if (!vm.GroundwaterVM.IsEnabled)
        {
            vm.GroundwaterVM.IsEnabled = true;
            Console.WriteLine("Groundwater was Off after load — enabled for ACADS 4 wet+load case.");
        }

        Console.WriteLine(
            $"Groundwater On — {vm.GroundwaterVM.GroundwaterSurfPoints.Count} phreatic point(s), " +
            $"gamma_w={vm.GroundwaterVM.GammaW:F2}");
    }

    private static void EnsureGroundwaterInDat(string datContent)
    {
        if (!datContent.Contains("BEGIN:Groundwater", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Synced .dat missing Groundwater section.");
        if (datContent.Contains("include false", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Synced .dat has groundwater disabled.");
    }

    private static void EnsureLoadsInDat(string datContent)
    {
        if (!datContent.Contains("BEGIN:DistributedEdgeLoads", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Synced .dat missing DistributedEdgeLoads section.");
        // After "! N" group count, expect at least one edge row with qa qb.
        if (!System.Text.RegularExpressions.Regex.IsMatch(
                datContent,
                @"BEGIN:DistributedEdgeLoads[\s\S]*?! \d+\s+[\d.eE+\-]+\s+[\d.eE+\-]+",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            // Still OK if edges present as nodeA nodeB qa qb
        }
        var edgeLines = datContent
            .Split('\n')
            .Where(l => l.StartsWith('!') && System.Text.RegularExpressions.Regex.IsMatch(
                l, @"!\s+\d+\s+\d+\s+[\d.eE+\-]+\s+[\d.eE+\-]"))
            .ToList();
        if (edgeLines.Count == 0)
            throw new InvalidOperationException("DistributedEdgeLoads section has no edge pressure rows.");
        Console.WriteLine($"Dat edge-load rows (approx): {edgeLines.Count}");
    }

    private static void EnsureSlopeBoundaryConditions(MainWindowViewModel vm)
    {
        var bcVm = vm.BoundaryConditionsVM;
        // Ex06 hex domain (saved project): L1 = base Fixed; L2 + L6 = vertical sides Roller X.
        bcVm.ClearBoundaryConditionsCommand.Execute(null);
        bcVm.ImportOuterBoundariesCommand.Execute(null);

        int applied = 0;
        foreach (var boundary in bcVm.AvailableOuterBoundaries.ToList())
        {
            string? bcType = boundary.LineNumber switch
            {
                1 => "Fixed",
                2 => "Roller X",
                6 => "Roller X",
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
                $"Mesh present: {vm.MeshVM.MeshElements.Count} els, {vm.MeshVM.DiscretizationNodes.Count} nodes");
            return;
        }

        vm.MeshVM.SelectedTwoDElementFamily = "Triangles";
        vm.MeshVM.SelectedTwoDNodeOption = "3-node";
        vm.MeshVM.MeshElementOrder = "3-node";
        vm.MeshVM.GlobalMeshSize = "1.5";
        vm.MeshVM.NearBoundaryMeshSize = "0.35";
        vm.MeshVM.RefineNearMaterialInterfaces = true;
        vm.MeshVM.GenerateSurfaceMeshCommand.Execute(null);
        WaitForUiIdle(app, cycles: 40);
        if (!vm.MeshVM.IsDiscretized || vm.MeshVM.MeshInformationRows.Count == 0)
            throw new InvalidOperationException($"Mesh failed: {vm.MeshVM.SurfaceMeshStatus}");
    }

    private static void ConfigureRunSettings(MainWindowViewModel vm)
    {
        vm.MaterialPropertiesVM.SelectedAnalysisCategory = MaterialPropertiesViewModel.SrfAnalysisCategory;
        vm.FEAnalysisVM.SelectedSrfSearchMode = SrfSearchMode.Bracket;
        vm.FEAnalysisVM.AutoDetectSrfBracket = true;
        // Seed near literature SSR 0.69 to reduce wasted expansion steps.
        vm.FEAnalysisVM.SrfBracketSeed = 0.70;
        vm.FEAnalysisVM.SrfBracketExpansionFactor = 1.25;
        vm.FEAnalysisVM.SrfMaxExpansionSteps = 12;
        vm.FEAnalysisVM.SrfBracketTolerance = 0.01;
        vm.FEAnalysisVM.SrfMaxBracketIterations = 24;
        vm.FEAnalysisVM.ConvergenceTolerance = 1.0e-5;
        vm.FEAnalysisVM.MaxIterations = 1000;
        vm.FEAnalysisVM.LoadIncrements = 100;
    }

    private static void WaitForAnalysisCompletion(Application app, MainWindowViewModel vm, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        bool sawBusy = false;
        var lastStatus = "";
        var lastPrintedUtc = DateTime.MinValue;
        while (DateTime.UtcNow < deadline)
        {
            PumpDispatcher(app);
            if (vm.FEAnalysisVM.IsSolving)
                sawBusy = true;
            else if (sawBusy)
                return;

            if (vm.ResultsVM.EstimatedSrfFoS.HasValue
                && (vm.FEAnalysisVM.StatusMessage?.Contains("Critical FoS", StringComparison.OrdinalIgnoreCase) == true
                    || vm.FEAnalysisVM.StatusMessage?.Contains("complete", StringComparison.OrdinalIgnoreCase) == true))
                return;

            var status = vm.FEAnalysisVM.StatusMessage ?? "";
            if (!string.Equals(status, lastStatus, StringComparison.Ordinal)
                && DateTime.UtcNow - lastPrintedUtc > TimeSpan.FromSeconds(5))
            {
                Console.WriteLine($"[T3] {status}");
                lastStatus = status;
                lastPrintedUtc = DateTime.UtcNow;
            }
            Thread.Sleep(100);
        }
        throw new TimeoutException($"Timed out. Last status: {vm.FEAnalysisVM.StatusMessage}");
    }

    private static void WaitForUiIdle(Application app, int cycles = 5)
    {
        for (int i = 0; i < cycles; i++)
        {
            PumpDispatcher(app);
            Thread.Sleep(25);
        }
    }

    private static void PumpDispatcher(Application app)
        => app.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);

    private static void CopyRunArtifacts(string program, DateTime runStartedUtc, string artifactDir)
    {
        string workDir = Path.Combine(Path.GetTempPath(), "GeotechSlopeApp_" + program + "_script");
        string baseName = program.StartsWith("slope_stability", StringComparison.OrdinalIgnoreCase)
            ? "p610t3_script_run"
            : "fe_script_run";
        if (!Directory.Exists(workDir))
            return;
        foreach (var path in Directory.EnumerateFiles(workDir, baseName + "*"))
        {
            var info = new FileInfo(path);
            if (info.LastWriteTimeUtc < runStartedUtc.AddSeconds(-2))
                continue;
            File.Copy(path, Path.Combine(artifactDir, info.Name), overwrite: true);
        }
    }

    private static string BuildMarkdown(AutomationSummary summary)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# RS2 Example 06 / ACADS 4 — Load + Piezometric Water Table");
        sb.AppendLine();
        sb.AppendLine($"Generated: `{summary.GeneratedAt}`");
        sb.AppendLine($"RS2 SSR reference FoS: `{summary.LiteratureFoS:F2}`");
        sb.AppendLine($"Spencer: `{summary.ReferenceFoSSpencer:F3}` · GLE: `{summary.ReferenceFoSGle:F3}` · Janbu: `{summary.ReferenceFoSJanbu:F3}`");
        sb.AppendLine();
        sb.AppendLine("| Mesh | FoS | Deviation | Deviation % | Nodes | Elements | Edges | GW | Loads |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|:---:|---:|");
        foreach (var item in summary.Cases)
        {
            sb.AppendLine(
                $"| {item.MeshType} | {Fmt(item.EstimatedFoS)} | {Fmt(item.DeviationFromLiterature)} | " +
                $"{Fmt(item.DeviationPercent)} | {item.NodeCount} | {item.ElementCount} | " +
                $"{item.ExportedEdgeLoadCount} | {(item.GroundwaterEnabled ? "yes" : "no")} | {item.LoadsEnabled} |");
        }
        return sb.ToString();
    }

    private static string Fmt(double? value)
        => value.HasValue ? value.Value.ToString("F3", CultureInfo.InvariantCulture) : "n/a";

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

    private sealed class AutomationSummary
    {
        public string GeneratedAt { get; init; } = "";
        public double LiteratureFoS { get; init; }
        public double ReferenceFoSSpencer { get; init; }
        public double ReferenceFoSGle { get; init; }
        public double ReferenceFoSJanbu { get; init; }
        public List<MeshRunResult> Cases { get; init; } = new();
    }

    private sealed class MeshRunResult
    {
        public string MeshType { get; init; } = "";
        public string ProjectPath { get; init; } = "";
        public string Program { get; init; } = "";
        public bool ScriptSyncedFromMesh { get; init; }
        public bool GroundwaterEnabled { get; init; }
        public int LoadsEnabled { get; init; }
        public int ExportedEdgeLoadCount { get; init; }
        public double? EstimatedFoS { get; init; }
        public double? DeviationFromLiterature { get; init; }
        public double? DeviationPercent { get; init; }
        public string StatusMessage { get; init; } = "";
        public string FosNote { get; init; } = "";
        public int NodeCount { get; init; }
        public int ElementCount { get; init; }
        public string ArtifactDirectory { get; init; } = "";
        public List<TrialRow> TrialRows { get; init; } = new();
        public List<string> LoadAxisSegments { get; init; } = new();
    }

    private sealed record TrialRow(double Srf, double MaxDisplacement, int Iterations, bool Converged);
}
