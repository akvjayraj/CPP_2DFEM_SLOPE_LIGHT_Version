using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Persistence;
using GeotechSlopeApp.ViewModels;
using GeotechSlopeApp.Views;

namespace RS2Example06Table2Loads;

/// <summary>
/// Places PDF Table 2 surcharges on the Example 6 SRF project in axis metres
/// (stored as canvas), verifies mesh-edge export, and saves.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            var repoRoot = FindRepoRoot();
            var projectPath = Path.Combine(
                repoRoot,
                "Verification",
                "slope",
                "Groundwater",
                "RS2_Example06_slope_load_piezometric_water_table",
                "RS2 Example 6 - Slope with Load and Pore Pressure Defined by Water Table (ACADS 4)_SRF.xfem");
            if (!File.Exists(projectPath))
                throw new FileNotFoundException("SRF project not found", projectPath);

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
            var window = new MainWindow();
            window.Show();
            window.WindowState = WindowState.Minimized;

            if (window.DataContext is not MainWindowViewModel vm)
                throw new InvalidOperationException("MainWindow has no MainWindowViewModel.");

            window.PrepareForAutomationProjectLoad();
            Pump(app);

            XfemProjectPersistence.Load(vm, projectPath);
            vm.CurrentXfemProjectPath = projectPath;
            vm.ProjectWorkspace.UpdateActiveSlotFromVm(vm, projectPath);
            vm.RefreshStageFooterAfterProjectLoad();
            Pump(app);

            PlaceTable2Loads(vm);
            Pump(app);

            int edgeCount = DistributedEdgeLoadDatWriter
                .CollectGroups(vm.LoadConditionsVM, vm.MeshVM, vm.GeometryDefinitionVM)
                .Sum(g => g.Edges.Count);
            Console.WriteLine($"DistributedEdgeLoad export: {edgeCount} mesh edge(s)");
            if (edgeCount == 0)
                throw new InvalidOperationException("Table 2 loads did not map to any mesh boundary edges.");

            XfemProjectPersistence.Save(vm, projectPath);
            Console.WriteLine($"Saved {projectPath}");

            var outDir = Path.Combine(Path.GetDirectoryName(projectPath)!, "gui_automation");
            Directory.CreateDirectory(outDir);
            var report = new
            {
                GeneratedAt = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
                ProjectPath = projectPath,
                ExportedEdgeLoadCount = edgeCount,
                Loads = DescribeLoads(vm),
            };
            var jsonPath = Path.Combine(outDir, "rs2_example06_table2_loads.json");
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Wrote {jsonPath}");

            app.Shutdown();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void PlaceTable2Loads(MainWindowViewModel vm)
    {
        var geom = vm.GeometryDefinitionVM;
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
            || Math.Abs(midStart.Y - 27.75) > 0.05 || Math.Abs(midEnd.Y - 27.75) > 0.05)
            throw new InvalidOperationException(
                $"Mid-bench axis mismatch: ({midStart.X},{midStart.Y})-({midEnd.X},{midEnd.Y})");

        var crest = vm.LoadConditionsVM.Loads[1];
        var crestStart = geom.ConvertCanvasToAxis(new Point(crest.StartX, crest.StartY));
        var crestEnd = geom.ConvertCanvasToAxis(new Point(crest.EndX, crest.EndY));
        if (Math.Abs(crestStart.X - 70) > 0.05 || Math.Abs(crestEnd.X - 80) > 0.05
            || Math.Abs(crestStart.Y - 40) > 0.05 || Math.Abs(crestEnd.Y - 40) > 0.05)
            throw new InvalidOperationException(
                $"Crest axis mismatch: ({crestStart.X},{crestStart.Y})-({crestEnd.X},{crestEnd.Y})");

        Console.WriteLine("Table 2 surcharges placed:");
        foreach (var line in DescribeLoads(vm))
            Console.WriteLine("  " + line);
    }

    private static List<string> DescribeLoads(MainWindowViewModel vm)
    {
        var geom = vm.GeometryDefinitionVM;
        var list = new List<string>();
        foreach (var load in vm.LoadConditionsVM.Loads)
        {
            var a = geom.ConvertCanvasToAxis(new Point(load.StartX, load.StartY));
            var b = geom.ConvertCanvasToAxis(new Point(load.EndX, load.EndY));
            list.Add(
                $"{load.Name}: axis ({a.X:F2},{a.Y:F2})-({b.X:F2},{b.Y:F2}) " +
                $"q={load.Magnitude:G}-{load.EndMagnitude:G} kPa dir={load.Direction} " +
                $"ori={load.Orientation} enabled={load.IsEnabled}");
        }
        return list;
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
}
