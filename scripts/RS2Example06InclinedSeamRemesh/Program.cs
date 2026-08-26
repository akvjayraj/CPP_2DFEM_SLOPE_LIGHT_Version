using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using GeotechSlopeApp.Persistence;
using GeotechSlopeApp.ViewModels;
using GeotechSlopeApp.Views;

namespace RS2Example06InclinedSeamRemesh;

/// <summary>
/// Patches RS2 Example 6 SRF project: base y=15 + inclined Soil #2 seam from PDF Fig.1,
/// remeshes T3, re-applies BCs, saves, and verifies mat2 band geometry.
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

            var backupPath = projectPath + ".bak_pre_inclined_seam";
            if (!File.Exists(backupPath))
                File.Copy(projectPath, backupPath);

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
            var window = new MainWindow();
            window.Show();
            window.WindowState = WindowState.Minimized;

            if (window.DataContext is not MainWindowViewModel vm)
                throw new InvalidOperationException("MainWindow has no MainWindowViewModel.");

            window.PrepareForAutomationProjectLoad();
            WaitForUiIdle(app);

            XfemProjectPersistence.Load(vm, projectPath);
            vm.CurrentXfemProjectPath = projectPath;
            vm.ProjectWorkspace.UpdateActiveSlotFromVm(vm, projectPath);
            vm.RefreshStageFooterAfterProjectLoad();
            WaitForUiIdle(app);

            ApplyInclinedSeamGeometry(vm);
            WaitForUiIdle(app);

            RemeshT3(app, vm);
            ApplySlopeBoundaryConditions(vm);
            WaitForUiIdle(app);

            var report = VerifyInclinedSeamBand(vm);
            Console.WriteLine(report.Summary);

            XfemProjectPersistence.Save(vm, projectPath);
            Console.WriteLine($"Saved {projectPath}");

            var outDir = Path.Combine(Path.GetDirectoryName(projectPath)!, "gui_automation");
            Directory.CreateDirectory(outDir);
            var jsonPath = Path.Combine(outDir, "rs2_example06_inclined_seam_remesh.json");
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Wrote {jsonPath}");

            app.Shutdown();
            return report.Ok ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void ApplyInclinedSeamGeometry(MainWindowViewModel vm)
    {
        var g = vm.GeometryDefinitionVM;

        // Outer polygon: lower base from y=20 → y=15 (PDF Fig.1).
        int lowered = 0;
        foreach (var v in g.GeometryVertexRows)
        {
            if (Math.Abs(v.AxisY - 20.0) < 1e-6 && (Math.Abs(v.AxisX - 20.0) < 1e-6 || Math.Abs(v.AxisX - 84.0) < 1e-6))
            {
                v.AxisY = 15.0;
                lowered++;
            }
        }
        if (lowered < 2)
            throw new InvalidOperationException($"Expected to lower 2 base vertices; lowered {lowered}.");

        g.RebuildGeometryAfterProjectLoad();

        // Inclined Soil #2 seam (~0.62 m thick).
        var cuts = new List<IReadOnlyList<(double X, double Y)>>
        {
            new List<(double X, double Y)> { (20.0, 18.26), (84.0, 36.2) },
            new List<(double X, double Y)> { (20.0, 18.88), (84.0, 36.8) },
        };
        g.SetNonHorizontalLayerCutsFromRestore(cuts);

        g.ReplaceLayerDisplayNamesFrom(new Dictionary<int, string>
        {
            [1] = "Soil #1 (below seam)",
            [2] = "Soil #2 (inclined seam)",
            [3] = "Soil #1 (above seam)",
        });

        // Keep L1=mat1, L2=mat2, L3=mat1.
        g.ReplaceLayerMaterialAssignmentsFrom(new Dictionary<int, int>
        {
            [1] = 1,
            [2] = 2,
            [3] = 1,
        });

        Console.WriteLine("Geometry: base y=15; inclined cuts (20,18.26)-(84,36.2) and (20,18.88)-(84,36.8).");
        foreach (var cut in g.NonHorizontalLayerCutsSnapshot)
        {
            var pts = string.Join(" → ", cut.Select(p =>
                $"({p.X.ToString(CultureInfo.InvariantCulture)},{p.Y.ToString(CultureInfo.InvariantCulture)})"));
            Console.WriteLine($"  cut: {pts}");
        }
    }

    private static void RemeshT3(Application app, MainWindowViewModel vm)
    {
        vm.MeshVM.ClearMeshCommand.Execute(null);
        WaitForUiIdle(app);

        vm.MeshVM.SelectedTwoDElementFamily = "Triangles";
        vm.MeshVM.SelectedTwoDNodeOption = "3-node";
        vm.MeshVM.MeshElementOrder = "3-node";
        // Slightly finer near interfaces so the ~0.62 m seam is resolved.
        vm.MeshVM.GlobalMeshSize = "1.5";
        vm.MeshVM.NearBoundaryMeshSize = "0.25";
        vm.MeshVM.RefineNearMaterialInterfaces = true;
        vm.MeshVM.GenerateSurfaceMeshCommand.Execute(null);
        WaitForUiIdle(app, cycles: 60);

        if (!vm.MeshVM.IsDiscretized || vm.MeshVM.MeshInformationRows.Count == 0)
            throw new InvalidOperationException($"Mesh failed: {vm.MeshVM.SurfaceMeshStatus}");

        Console.WriteLine(
            $"Mesh: {vm.MeshVM.MeshInformationRows.Count} els, {vm.MeshVM.DiscretizationNodes.Count} nodes. " +
            $"Status={vm.MeshVM.SurfaceMeshStatus}");
    }

    private static void ApplySlopeBoundaryConditions(MainWindowViewModel vm)
    {
        var bcVm = vm.BoundaryConditionsVM;
        bcVm.ClearBoundaryConditionsCommand.Execute(null);
        bcVm.ImportOuterBoundariesCommand.Execute(null);

        int applied = 0;
        foreach (var boundary in bcVm.AvailableOuterBoundaries.ToList())
        {
            // Hex domain: L1 base Fixed; vertical sides Roller X (line numbers may be 2/6).
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

    private static SeamReport VerifyInclinedSeamBand(MainWindowViewModel vm)
    {
        // Seam strip: between bottom cut (20,18.26)-(84,36.2) and top (20,18.88)-(84,36.8).
        static double CutY(double x, double x0, double y0, double x1, double y1)
            => y0 + (y1 - y0) * (x - x0) / (x1 - x0);

        int mat1 = 0, mat2 = 0, other = 0;
        int mat2InBand = 0, mat2Outside = 0;
        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;

        foreach (var row in vm.MeshVM.MeshInformationRows)
        {
            if (row.Etype == 1) mat1++;
            else if (row.Etype == 2) mat2++;
            else other++;

            if (row.Etype != 2)
                continue;

            double cx = (row.N1X + row.N2X + row.N3X) / 3.0;
            double cy = (row.N1Y + row.N2Y + row.N3Y) / 3.0;
            minX = Math.Min(minX, cx);
            maxX = Math.Max(maxX, cx);
            minY = Math.Min(minY, cy);
            maxY = Math.Max(maxY, cy);

            double yBot = CutY(cx, 20, 18.26, 84, 36.2);
            double yTop = CutY(cx, 20, 18.88, 84, 36.8);
            // Small tolerance for centroid vs cut (mesh interface ~ half-element).
            const double tol = 0.4;
            if (cy >= yBot - tol && cy <= yTop + tol)
                mat2InBand++;
            else
                mat2Outside++;
        }

        bool ok = mat2 >= 20
                  && mat2Outside <= Math.Max(3, mat2 / 10)
                  && minX < 30 && maxX > 70
                  && minY < 22 && maxY > 30;

        var summary =
            $"mat1={mat1} mat2={mat2} other={other}; " +
            $"mat2 in-band={mat2InBand} outside={mat2Outside}; " +
            $"mat2 centroid span x=[{minX:F2},{maxX:F2}] y=[{minY:F2},{maxY:F2}]; ok={ok}";

        return new SeamReport
        {
            Ok = ok,
            Summary = summary,
            Mat1Count = mat1,
            Mat2Count = mat2,
            Mat2InBand = mat2InBand,
            Mat2OutsideBand = mat2Outside,
            Mat2CentroidXMin = minX,
            Mat2CentroidXMax = maxX,
            Mat2CentroidYMin = minY,
            Mat2CentroidYMax = maxY,
            NodeCount = vm.MeshVM.DiscretizationNodes.Count,
            ElementCount = vm.MeshVM.MeshInformationRows.Count,
            Cuts = vm.GeometryDefinitionVM.NonHorizontalLayerCutsSnapshot
                .Select(c => c.Select(p => new[] { p.X, p.Y }).ToList())
                .ToList(),
            BaseVertices = vm.GeometryDefinitionVM.GeometryVertexRows
                .Select(v => new[] { v.AxisX, v.AxisY })
                .ToList(),
        };
    }

    private static void WaitForUiIdle(Application app, int cycles = 5)
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

    private sealed class SeamReport
    {
        public bool Ok { get; init; }
        public string Summary { get; init; } = "";
        public int Mat1Count { get; init; }
        public int Mat2Count { get; init; }
        public int Mat2InBand { get; init; }
        public int Mat2OutsideBand { get; init; }
        public double Mat2CentroidXMin { get; init; }
        public double Mat2CentroidXMax { get; init; }
        public double Mat2CentroidYMin { get; init; }
        public double Mat2CentroidYMax { get; init; }
        public int NodeCount { get; init; }
        public int ElementCount { get; init; }
        public List<List<double[]>> Cuts { get; init; } = new();
        public List<double[]> BaseVertices { get; init; } = new();
    }
}
