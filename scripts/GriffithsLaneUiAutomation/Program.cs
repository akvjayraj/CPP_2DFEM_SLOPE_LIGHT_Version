using System.Globalization;
using System.IO;
using System.Linq;
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

namespace GriffithsLaneUiAutomation;

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
                "Griffiths_Lane_Example1_2H1V");
            var outputDir = Path.Combine(verificationDir, "gui_automation");
            Directory.CreateDirectory(outputDir);

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
            var window = new MainWindow();
            window.Show();
            window.WindowState = WindowState.Minimized;

            if (window.DataContext is not MainWindowViewModel vm)
                throw new InvalidOperationException("MainWindow has no MainWindowViewModel.");

            var cases = new[]
            {
                new MeshCase("T3", Path.Combine(verificationDir, "griffiths_lane_example1_2h1v_t3.xfem")),
                new MeshCase("T6", Path.Combine(verificationDir, "griffiths_lane_example1_2h1v_t6.xfem")),
                new MeshCase("Q4", Path.Combine(verificationDir, "griffiths_lane_example1_2h1v_Q4.xfem")),
                new MeshCase("Q8", Path.Combine(verificationDir, "griffiths_lane_example1_2h1v_Q8.xfem")),
            };

            var results = new List<MeshRunResult>(cases.Length);
            foreach (var meshCase in cases)
            {
                var result = RunCase(app, window, vm, meshCase, outputDir);
                results.Add(result);
                Console.WriteLine(
                    $"Completed {result.MeshType}: FoS={(result.EstimatedFoS?.ToString("F3", CultureInfo.InvariantCulture) ?? "n/a")} " +
                    $"status={result.StatusMessage}");
            }

            var summary = new AutomationSummary
            {
                GeneratedAt = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
                LiteratureFoS = LiteratureFoS,
                Cases = results,
            };

            var jsonPath = Path.Combine(outputDir, "griffiths_lane_gui_run_summary.json");
            var markdownPath = Path.Combine(outputDir, "griffiths_lane_gui_run_summary.md");
            File.WriteAllText(
                jsonPath,
                JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(markdownPath, BuildMarkdown(summary));

            Console.WriteLine($"Wrote {jsonPath}");
            Console.WriteLine($"Wrote {markdownPath}");
            foreach (var result in results)
            {
                Console.WriteLine(
                    $"{result.MeshType}: FoS={(result.EstimatedFoS?.ToString("F3", CultureInfo.InvariantCulture) ?? "n/a")} " +
                    $"program={result.Program} status={result.StatusMessage}");
            }

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
        MeshCase meshCase,
        string outputDir)
    {
        if (!File.Exists(meshCase.ProjectPath))
            throw new FileNotFoundException("XFEM project not found.", meshCase.ProjectPath);

        Console.WriteLine($"Running {meshCase.MeshType} from {meshCase.ProjectPath}");

        // Script Editor tab does not spawn right-panel popups (avoids Owner-on-closed-window on reload).
        window.PrepareForAutomationProjectLoad();
        WaitForUiIdle(app);

        XfemProjectPersistence.Load(vm, meshCase.ProjectPath);
        vm.CurrentXfemProjectPath = meshCase.ProjectPath;
        vm.ProjectWorkspace.UpdateActiveSlotFromVm(vm, meshCase.ProjectPath);
        vm.RefreshStageFooterAfterProjectLoad();
        WaitForUiIdle(app);

        ConfigureRunSettings(vm);
        bool scriptSynced = vm.ScriptVM.TrySyncDiscretizedMeshToScriptDat(ignoreLoadedDeckPreserve: true);
        WaitForUiIdle(app);

        string datContent = vm.ScriptVM.GetScriptContentForSolverRun();
        if (!P610AnalysisOptionsHelper.TryParse(datContent, out var options))
            throw new InvalidOperationException($"Could not parse AnalysisOptions for {meshCase.MeshType}.");

        string program = P610AnalysisOptionsHelper.ResolveRunnerExecutableForFeAnalysis(
            options,
            vm.MeshVM,
            srfAnalysisMode: vm.MaterialPropertiesVM?.IsSrfAnalysisMode == true);

        var runStartedUtc = DateTime.UtcNow;
        vm.FEAnalysisVM.RunAnalysisCommand.Execute(null);
        WaitForAnalysisCompletion(app, vm, meshCase.MeshType, TimeSpan.FromMinutes(45));
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

        var artifactDir = Path.Combine(outputDir, meshCase.MeshType.ToLowerInvariant());
        Directory.CreateDirectory(artifactDir);
        CopyRunArtifacts(program, runStartedUtc, artifactDir);

        var route = SrfLiteratureOptimalRouting.Resolve(meshCase.MeshType);
        var runSummary = new MeshRunResult
        {
            MeshType = meshCase.MeshType,
            ProjectPath = meshCase.ProjectPath,
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
            ArtifactDirectory = artifactDir,
        };

        var caseJsonPath = Path.Combine(artifactDir, $"griffiths_lane_{meshCase.MeshType.ToLowerInvariant()}_result.json");
        File.WriteAllText(
            caseJsonPath,
            JsonSerializer.Serialize(runSummary, new JsonSerializerOptions { WriteIndented = true }));

        if (meshCase.MeshType.Equals("Q8", StringComparison.OrdinalIgnoreCase)
            && program.Equals("slope_stability_Q8_SRF", StringComparison.OrdinalIgnoreCase)
            && fos.HasValue
            && Math.Abs(fos.Value - LiteratureFoS) > 0.25)
        {
            throw new InvalidOperationException(
                $"Q8 FoS remained far from the Griffiths & Lane reference after fresh mesh->script sync: " +
                $"FoS={fos.Value:F3}, expected about {LiteratureFoS:F2}.");
        }

        return runSummary;
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
        string meshType,
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
                Console.WriteLine($"[{meshType}] {status}");
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
        string workDir;
        string baseName;
        if (program.Equals("p64", StringComparison.OrdinalIgnoreCase))
        {
            workDir = Path.Combine(Path.GetTempPath(), "GeotechSlopeApp_fem_cpp_script");
            baseName = "fe_script_run";
        }
        else if (program.Equals("slope_stability_Q8_SRF", StringComparison.OrdinalIgnoreCase))
        {
            workDir = Path.Combine(Path.GetTempPath(), "GeotechSlopeApp_slope_stability_Q8_SRF_script");
            baseName = "fe_script_run";
        }
        else
        {
            workDir = Path.Combine(Path.GetTempPath(), "GeotechSlopeApp_" + program + "_script");
            baseName = "p610t3_script_run";
        }

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
        sb.AppendLine("# Griffiths & Lane (1999) Example 1 GUI Automation Summary");
        sb.AppendLine();
        sb.AppendLine($"Generated: `{summary.GeneratedAt}`");
        sb.AppendLine($"Literature FoS reference: `{summary.LiteratureFoS:F2}`");
        sb.AppendLine();
        sb.AppendLine("| Mesh | Branded exe | Process exe | Core | FoS | Deviation | Deviation % |");
        sb.AppendLine("|---|---|---|---|---:|---:|---:|");
        foreach (var item in summary.Cases)
        {
            sb.AppendLine(
                $"| {item.MeshType} | {item.BrandedProgram} | {item.Program} | {item.SolverCore} | {Fmt(item.EstimatedFoS)} | {Fmt(item.DeviationFromLiterature)} | {Fmt(item.DeviationPercent)} |");
        }

        foreach (var item in summary.Cases)
        {
            sb.AppendLine();
            sb.AppendLine($"## {item.MeshType}");
            sb.AppendLine();
            sb.AppendLine($"Project: `{item.ProjectPath}`");
            sb.AppendLine($"Status: `{item.StatusMessage}`");
            if (!string.IsNullOrWhiteSpace(item.FosNote))
                sb.AppendLine($"FoS note: {item.FosNote}");
            if (item.TrialRows.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("| SRF | Max displacement | Iterations | Converged |");
                sb.AppendLine("|---:|---:|---:|:---:|");
                foreach (var row in item.TrialRows)
                {
                    sb.AppendLine(
                        $"| {row.Srf.ToString("F4", CultureInfo.InvariantCulture)} | {row.MaxDisplacement.ToString("G6", CultureInfo.InvariantCulture)} | {row.Iterations} | {(row.Converged ? "Yes" : "No")} |");
                }
            }
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

    private sealed record MeshCase(string MeshType, string ProjectPath);

    private sealed class AutomationSummary
    {
        public string GeneratedAt { get; init; } = "";
        public double LiteratureFoS { get; init; }
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
        public string ArtifactDirectory { get; init; } = "";
        public List<TrialRow> TrialRows { get; init; } = new();
    }

    private sealed record TrialRow(double Srf, double MaxDisplacement, int Iterations, bool Converged);
}
