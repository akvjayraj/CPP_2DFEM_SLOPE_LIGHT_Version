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

namespace RS2Example14UiAutomation;

internal static class Program
{
    private const double LiteratureFoS = 1.40;

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
                "RS2_Example14_simple_slope_assessment_II");
            var outputDir = Path.Combine(verificationDir, "gui_automation");
            Directory.CreateDirectory(outputDir);

            var projectPath = Path.Combine(
                verificationDir,
                "rs2_example14_simple_slope_assessment_II.xfem");

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
                ReferenceFoSBishop = 1.409,
                ReferenceFoSAraiTagyoBishop = 1.451,
                Cases = new List<MeshRunResult> { result },
            };

            var jsonPath = Path.Combine(outputDir, "rs2_example14_gui_run_summary.json");
            var markdownPath = Path.Combine(outputDir, "rs2_example14_gui_run_summary.md");
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
            return 0;
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
        if (!File.Exists(projectPath))
            throw new FileNotFoundException("XFEM project not found.", projectPath);

        Console.WriteLine($"Running RS2 Example 14 from {projectPath}");

        window.PrepareForAutomationProjectLoad();
        WaitForUiIdle(app);

        XfemProjectPersistence.Load(vm, projectPath);
        vm.CurrentXfemProjectPath = projectPath;
        vm.ProjectWorkspace.UpdateActiveSlotFromVm(vm, projectPath);
        vm.RefreshStageFooterAfterProjectLoad();
        WaitForUiIdle(app);

        ConfigureRunSettings(vm);
        EnsureSurfaceMesh(app, vm);
        EnsureSlopeBoundaryConditions(vm);

        bool scriptSynced = vm.ScriptVM.TrySyncDiscretizedMeshToScriptDat(ignoreLoadedDeckPreserve: true);
        WaitForUiIdle(app);

        string datContent = vm.ScriptVM.GetScriptContentForSolverRun();
        if (!P610AnalysisOptionsHelper.TryParse(datContent, out var options))
            throw new InvalidOperationException("Could not parse AnalysisOptions for RS2 Example 14.");

        string program = P610AnalysisOptionsHelper.ResolveRunnerExecutableForFeAnalysis(
            options,
            vm.MeshVM,
            srfAnalysisMode: vm.MaterialPropertiesVM?.IsSrfAnalysisMode == true);

        var runStartedUtc = DateTime.UtcNow;
        vm.FEAnalysisVM.RunAnalysisCommand.Execute(null);
        WaitForAnalysisCompletion(app, vm, TimeSpan.FromMinutes(90));
        WaitForUiIdle(app);

        double? fos = vm.ResultsVM.EstimatedSrfFoS;
        string status = vm.FEAnalysisVM.StatusMessage;
        string artifactSummary = vm.ResultsVM.FemCppArtifactSummary;
        string fosNote = vm.ResultsVM.SrfFoSNote;
        var trialRows = vm.ResultsVM.SrfTrialRows
            .Select(r => new TrialRow(
                r.Srf,
                r.MaxDisplacement,
                r.Iterations,
                !r.Failed))
            .ToList();

        var artifactDir = Path.Combine(outputDir, "t3");
        Directory.CreateDirectory(artifactDir);
        CopyRunArtifacts(program, runStartedUtc, artifactDir);

        var route = SrfLiteratureOptimalRouting.Resolve("T3");
        var runSummary = new MeshRunResult
        {
            MeshType = "T3",
            ProjectPath = projectPath,
            Program = program,
            BrandedProgram = route.Executable,
            SolverCore = route.Core.ToString(),
            DeckFormat = route.Deck.ToString(),
            ScriptSyncedFromMesh = scriptSynced,
            EstimatedFoS = fos,
            DeviationFromLiterature = fos.HasValue ? fos.Value - LiteratureFoS : null,
            DeviationPercent = fos.HasValue ? 100.0 * (fos.Value - LiteratureFoS) / LiteratureFoS : null,
            StatusMessage = status,
            FosNote = fosNote,
            ArtifactSummary = artifactSummary,
            TrialRows = trialRows,
            MeshRowCount = vm.MeshVM.MeshInformationRows.Count,
            NodeCount = vm.MeshVM.DiscretizationNodes.Count,
            ElementCount = vm.MeshVM.MeshElements.Count,
            ArtifactDirectory = artifactDir,
        };

        var caseJsonPath = Path.Combine(artifactDir, "rs2_example14_t3_result.json");
        File.WriteAllText(
            caseJsonPath,
            JsonSerializer.Serialize(runSummary, new JsonSerializerOptions { WriteIndented = true }));

        XfemProjectPersistence.Save(vm, projectPath);
        return runSummary;
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

        Console.WriteLine($"Applied boundary conditions to {applied} exterior node(s).");
    }

    private static void EnsureSurfaceMesh(Application app, MainWindowViewModel vm)
    {
        if (vm.MeshVM.IsDiscretized && vm.MeshVM.MeshInformationRows.Count > 0)
        {
            Console.WriteLine("Mesh already present; skipping surface mesh generation.");
            return;
        }

        vm.MeshVM.SelectedTwoDElementFamily = "Triangles";
        vm.MeshVM.SelectedTwoDNodeOption = "3-node";
        vm.MeshVM.MeshElementOrder = "3-node";
        vm.MeshVM.GlobalMeshSize = "2.0";
        vm.MeshVM.NearBoundaryMeshSize = "0.5";

        Console.WriteLine("Generating surface mesh...");
        vm.MeshVM.GenerateSurfaceMeshCommand.Execute(null);
        WaitForUiIdle(app, cycles: 20);

        if (!vm.MeshVM.IsDiscretized || vm.MeshVM.MeshInformationRows.Count == 0)
            throw new InvalidOperationException(
                $"Surface mesh generation failed: {vm.MeshVM.SurfaceMeshStatus}");

        Console.WriteLine(
            $"Mesh ready: {vm.MeshVM.MeshElements.Count} elements, {vm.MeshVM.DiscretizationNodes.Count} nodes " +
            $"({vm.MeshVM.SurfaceMeshStatus})");
    }

    private static void ConfigureRunSettings(MainWindowViewModel vm)
    {
        vm.MaterialPropertiesVM.SelectedAnalysisCategory = MaterialPropertiesViewModel.SrfAnalysisCategory;
        vm.FEAnalysisVM.SelectedSrfSearchMode = SrfSearchMode.Bracket;
        vm.FEAnalysisVM.AutoDetectSrfBracket = true;
        vm.FEAnalysisVM.SrfBracketSeed = 1.0;
        vm.FEAnalysisVM.SrfBracketExpansionFactor = 1.5;
        vm.FEAnalysisVM.SrfMaxExpansionSteps = 10;
        vm.FEAnalysisVM.SrfBracketTolerance = 0.01;
        vm.FEAnalysisVM.SrfMaxBracketIterations = 20;
        vm.FEAnalysisVM.ConvergenceTolerance = 1.0e-5;
        vm.FEAnalysisVM.MaxIterations = 1000;
        vm.FEAnalysisVM.LoadIncrements = 20;
    }

    private static void WaitForAnalysisCompletion(
        Application app,
        MainWindowViewModel vm,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        bool sawBusy = false;
        var lastStatus = string.Empty;
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

            var status = vm.FEAnalysisVM.StatusMessage ?? string.Empty;
            if (!string.Equals(status, lastStatus, StringComparison.Ordinal)
                && DateTime.UtcNow - lastPrintedUtc > TimeSpan.FromSeconds(5))
            {
                Console.WriteLine($"[T3] {status}");
                lastStatus = status;
                lastPrintedUtc = DateTime.UtcNow;
            }

            Thread.Sleep(100);
        }

        throw new TimeoutException(
            $"Timed out waiting for FE analysis completion. Last status: {vm.FEAnalysisVM.StatusMessage}");
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
    {
        app.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }

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

            var dest = Path.Combine(artifactDir, info.Name);
            File.Copy(path, dest, overwrite: true);
        }
    }

    private static string BuildMarkdown(AutomationSummary summary)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# RS2 Example 14 (Arai & Tagyo) GUI Automation Summary");
        sb.AppendLine();
        sb.AppendLine($"Generated: `{summary.GeneratedAt}`");
        sb.AppendLine($"RS2 SSR reference FoS: `{summary.LiteratureFoS:F2}`");
        sb.AppendLine();
        sb.AppendLine("| Mesh | FoS | Deviation | Deviation % |");
        sb.AppendLine("|---|---:|---:|---:|");
        foreach (var item in summary.Cases)
        {
            sb.AppendLine(
                $"| {item.MeshType} | {Fmt(item.EstimatedFoS)} | {Fmt(item.DeviationFromLiterature)} | {Fmt(item.DeviationPercent)} |");
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
        public double ReferenceFoSBishop { get; init; }
        public double ReferenceFoSAraiTagyoBishop { get; init; }
        public List<MeshRunResult> Cases { get; init; } = new();
    }

    private sealed class MeshRunResult
    {
        public string MeshType { get; init; } = "";
        public string ProjectPath { get; init; } = "";
        public string Program { get; init; } = "";
        public string BrandedProgram { get; init; } = "";
        public string SolverCore { get; init; } = "";
        public string DeckFormat { get; init; } = "";
        public bool ScriptSyncedFromMesh { get; init; }
        public double? EstimatedFoS { get; init; }
        public double? DeviationFromLiterature { get; init; }
        public double? DeviationPercent { get; init; }
        public string StatusMessage { get; init; } = "";
        public string FosNote { get; init; } = "";
        public string ArtifactSummary { get; init; } = "";
        public int MeshRowCount { get; init; }
        public int NodeCount { get; init; }
        public int ElementCount { get; init; }
        public string ArtifactDirectory { get; init; } = "";
        public List<TrialRow> TrialRows { get; init; } = new();
    }

    private sealed record TrialRow(double Srf, double MaxDisplacement, int Iterations, bool Converged);
}
