using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Persistence;
using GeotechSlopeApp.ViewModels;
using GeotechSlopeApp.ViewModels.Dialogs;
using GeotechSlopeApp.Views;

namespace LoadXfemSmoke;

internal static class LoadXfemProgram
{
    private static int Main(string[] args)
    {
        var xfemPath = args.Length > 0
            ? args[0]
            : Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "Verification", "slope", "Ex2_twolayerslope", "project_multilayer_7.xfem"));

        if (!File.Exists(xfemPath))
        {
            Console.Error.WriteLine($"XFEM not found: {xfemPath}");
            return 1;
        }

        int exitCode = 0;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var window = new MainWindow();
                window.Show();

                if (window.DataContext is not MainWindowViewModel vm)
                    throw new InvalidOperationException("MainWindow has no MainWindowViewModel.");

                // Simulate dirty check before File → Open (new code path).
                vm.GeometryDefinitionVM.GeometryXInput = "99";
                var before = XfemProjectPersistence.SerializeSnapshotForDirtyCheck(vm);
                var after = XfemProjectPersistence.SerializeSnapshotForDirtyCheck(vm);
                if (before == after)
                    Console.WriteLine("WARN: dirty check saw no change after edit.");

                XfemProjectPersistence.Load(vm, xfemPath);
                vm.CurrentXfemProjectPath = xfemPath;
                vm.ProjectWorkspace.UpdateActiveSlotFromVm(vm, xfemPath);
                vm.RefreshStageFooterAfterProjectLoad();
                Console.WriteLine("Stage footer refreshed.");
                XfemProjectPersistence.SerializeSnapshotForDirtyCheck(vm);

                VerifyGroundwaterOverlaysIfPresent(vm);
                Console.WriteLine("GW overlay verification passed.");

                VerifyPondedFreeWaterIfPresent(vm, xfemPath);
                Console.WriteLine("Ponded free-water verification passed.");

                VerifySaturatedZoneAirbornePeakRegression(vm);
                Console.WriteLine("GW airborne WT peak regression passed.");

                VerifyProjectSettingsAnalysisSubtabs(vm);
                Console.WriteLine("Project Settings analysis subtab verification passed.");

                VerifyGroundwaterStaticSubtab(vm);
                Console.WriteLine("Groundwater Static subtab verification passed.");

                var temp = Path.Combine(Path.GetTempPath(), "load_xfem_smoke_roundtrip.xfem");
                var expectedShowSaturatedZone = vm.GroundwaterVM.ShowSaturatedMaterialZone;
                var expectedShowUnsaturatedZone = vm.GroundwaterVM.ShowUnsaturatedMaterialZone;
                var expectedShowPonded = vm.GroundwaterVM.ShowPondedFreeWaterRegion;
                var expectedShowPondedArrows = vm.GroundwaterVM.ShowPondedFacePressureArrows;
                var expectedShowPondedIsobars = vm.GroundwaterVM.ShowPondedHeadIsobars;
                var expectedPondedMethod = vm.GroundwaterVM.PondedWaterLevelMethod;
                var expectedPondedHead = vm.GroundwaterVM.PondedTotalHead;
                XfemProjectPersistence.Save(vm, temp);
                XfemProjectPersistence.Load(vm, temp);
                XfemProjectPersistence.SerializeSnapshotForDirtyCheck(vm);
                if (vm.GroundwaterVM.ShowSaturatedMaterialZone != expectedShowSaturatedZone
                    || vm.GroundwaterVM.ShowUnsaturatedMaterialZone != expectedShowUnsaturatedZone
                    || vm.GroundwaterVM.ShowPondedFreeWaterRegion != expectedShowPonded
                    || vm.GroundwaterVM.ShowPondedFacePressureArrows != expectedShowPondedArrows
                    || vm.GroundwaterVM.ShowPondedHeadIsobars != expectedShowPondedIsobars
                    || vm.GroundwaterVM.PondedWaterLevelMethod != expectedPondedMethod
                    || Math.Abs(vm.GroundwaterVM.PondedTotalHead - expectedPondedHead) > 1e-9)
                {
                    throw new InvalidOperationException(
                        "Groundwater material-zone / ponded display flags did not roundtrip.");
                }

                VerifySeepageSettingsRoundtrip(vm);
                Console.WriteLine("Seepage settings roundtrip verification passed.");

                // Render the Analysis Type master-detail panel so its XAML is exercised.
                vm.SelectedTabIndex = 0;
                vm.ProjectSettingsVM.SelectSettingsTab(ProjectSettingsTabKind.AnalysisType);

                for (int i = 0; i < 5; i++)
                    app.Dispatcher.Invoke(DispatcherPriority.Background, () => { });

                // Also render the seepage options detail panel (p72/p73/p74 types).
                vm.ProjectSettingsVM.MaterialPropertiesVM!.SelectedAnalysisCategory =
                    MaterialPropertiesViewModel.GroundwaterAnalysisCategory;
                vm.ProjectSettingsVM.MaterialPropertiesVM.SelectedDetailedAnalysisType =
                    MaterialPropertiesViewModel.GroundwaterSeepageFreeSurfaceType;
                for (int i = 0; i < 5; i++)
                    app.Dispatcher.Invoke(DispatcherPriority.Background, () => { });
                if (!vm.ProjectSettingsVM.IsSeepageAnalysisSelected)
                    throw new InvalidOperationException("IsSeepageAnalysisSelected is false for the free-surface type.");
                Console.WriteLine("Analysis Type master-detail panel rendered.");

                app.Shutdown();
            }
            catch (Exception ex)
            {
                failure = ex;
                exitCode = 2;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
        {
            Console.Error.WriteLine("FAILED:");
            Console.Error.WriteLine(failure);
            return exitCode;
        }

        Console.WriteLine("OK");
        return 0;
    }

    /// <summary>Seepage (p72/p73/p74) options must survive an .xfem save + load.</summary>
    private static void VerifySeepageSettingsRoundtrip(MainWindowViewModel vm)
    {
        var s = vm.ProjectSettingsVM.SeepageSettings;
        s.UpstreamHead = 12.5;
        s.DownstreamHead = 3.25;
        s.Axisymmetric = true;
        s.FreeSurfaceTolerance = 0.005;
        s.FreeSurfaceMaxIterations = 35;

        var temp = Path.Combine(Path.GetTempPath(), "load_xfem_smoke_seepage.xfem");
        XfemProjectPersistence.Save(vm, temp);

        s.UpstreamHead = 0;
        s.DownstreamHead = 0;
        s.Axisymmetric = false;
        s.FreeSurfaceTolerance = 0;
        s.FreeSurfaceMaxIterations = 0;

        XfemProjectPersistence.Load(vm, temp);
        s = vm.ProjectSettingsVM.SeepageSettings;
        if (s.UpstreamHead != 12.5 || s.DownstreamHead != 3.25 || !s.Axisymmetric
            || s.FreeSurfaceTolerance != 0.005 || s.FreeSurfaceMaxIterations != 35)
        {
            throw new InvalidOperationException(
                $"Seepage settings did not roundtrip: H1={s.UpstreamHead}, H2={s.DownstreamHead}, "
                + $"axisym={s.Axisymmetric}, tol={s.FreeSurfaceTolerance}, iters={s.FreeSurfaceMaxIterations}.");
        }
    }

    private static void VerifyGroundwaterOverlaysIfPresent(MainWindowViewModel vm)
    {
        if (!ReferenceEquals(vm.GroundwaterVM.Materials, vm.MaterialPropertiesVM.Materials))
            throw new InvalidOperationException("Groundwater material summary is not wired to the live material library.");

        if (!vm.GroundwaterVM.IsEnabled)
            return;

        var geo = vm.GeometryDefinitionVM;
        var pointCount = geo.GroundwaterSurfPoints.Count;
        if (pointCount == 0)
            return;

        if (geo.GroundwaterSurfPointMarkers.Count != pointCount)
            throw new InvalidOperationException(
                $"GW marker count {geo.GroundwaterSurfPointMarkers.Count} != surf point count {pointCount}");
        if (geo.GroundwaterPhreaticPolyline.Count != pointCount)
            throw new InvalidOperationException(
                $"GW polyline count {geo.GroundwaterPhreaticPolyline.Count} != surf point count {pointCount}");
        if (geo.GroundwaterLines.Count != pointCount - 1)
            throw new InvalidOperationException(
                $"GW segment count {geo.GroundwaterLines.Count} != expected {pointCount - 1}");

        // Phreatic WT must be visible on Geometry (and any) tab when GW is enabled —
        // not only after switching to the Groundwater tab.
        vm.ApplyGeometryCanvasDecorationsForTab(1);
        if (!geo.IsGroundwaterPhreaticVisible)
            throw new InvalidOperationException(
                "GW phreatic overlay not visible on Geometry tab with groundwater enabled.");
        if (geo.GroundwaterPhreaticPolyline.Count == 0)
            throw new InvalidOperationException(
                "GW phreatic polyline empty on Geometry tab with groundwater enabled.");
        if (geo.ShowGroundwaterZoneOverlaysOnCanvas)
            throw new InvalidOperationException(
                "GW zone overlays should stay Groundwater-tab-only on Geometry tab.");
        Console.WriteLine("GW phreatic visible on Geometry tab OK.");

        vm.ApplyGeometryCanvasDecorationsForTab(6);
        if (!geo.IsGroundwaterPhreaticVisible)
            throw new InvalidOperationException("GW phreatic overlay not visible on groundwater tab.");
        if (!geo.ShowGroundwaterZoneOverlaysOnCanvas)
            throw new InvalidOperationException("GW zone overlays not enabled on groundwater tab.");

        try
        {
            VerifySaturatedZoneAlignsWithPhreatic(geo);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"GW saturated zone alignment failed: {ex.Message}", ex);
        }
        Console.WriteLine("GW saturated zone alignment OK.");

        VerifyGroundwaterZoneDisplayToggles(vm);

        var selected = geo.GroundwaterSurfPoints[0];
        vm.GroundwaterVM.SelectedSurfPoint = selected;
        if (!geo.GroundwaterSurfPointMarkers[0].IsHighlighted)
            throw new InvalidOperationException("GW selected surf point marker not highlighted.");

        var markerXBefore = geo.GroundwaterSurfPointMarkers[0].X;
        selected.AxisX += 0.25;
        if (Math.Abs(geo.GroundwaterSurfPointMarkers[0].X - markerXBefore) < 1e-9)
            throw new InvalidOperationException("GW marker did not move after DataGrid coordinate edit.");

        vm.GroundwaterVM.IsEnabled = false;
        if (geo.GroundwaterSurfPointMarkers.Count != 0)
            throw new InvalidOperationException("GW markers not cleared when groundwater disabled.");

        vm.GroundwaterVM.IsEnabled = true;
        if (geo.GroundwaterSurfPointMarkers.Count != pointCount)
            throw new InvalidOperationException("GW markers not rebuilt when groundwater re-enabled.");

        geo.RemoveGroundwaterSurfPoint(geo.GroundwaterSurfPoints[^1]);
        if (geo.GroundwaterSurfPointMarkers.Count != pointCount - 1)
            throw new InvalidOperationException("GW markers not updated after point removal.");
    }

    private static void VerifyGroundwaterZoneDisplayToggles(MainWindowViewModel vm)
    {
        var gw = vm.GroundwaterVM;
        var geo = vm.GeometryDefinitionVM;
        var oldShowSaturated = gw.ShowSaturatedMaterialZone;
        var oldShowUnsaturated = gw.ShowUnsaturatedMaterialZone;
        var phreaticPointCount = geo.GroundwaterPhreaticPolyline.Count;

        try
        {
            gw.ShowSaturatedMaterialZone = true;
            gw.ShowUnsaturatedMaterialZone = true;
            var saturatedCount = geo.GroundwaterZoneOverlays.Count(
                z => z.Kind == GroundwaterZoneKind.Saturated);
            var unsaturatedCount = geo.GroundwaterZoneOverlays.Count(
                z => z.Kind == GroundwaterZoneKind.Unsaturated);

            if (geo.LastSaturatedZoneAxisPolygonForTests.Count >= 3 && saturatedCount == 0)
                throw new InvalidOperationException("Saturated material-zone toggle did not show its overlay.");

            gw.ShowSaturatedMaterialZone = false;
            if (geo.GroundwaterZoneOverlays.Any(z => z.Kind == GroundwaterZoneKind.Saturated))
                throw new InvalidOperationException("Saturated material-zone overlay remained visible when unchecked.");
            if (geo.GroundwaterZoneOverlays.Count(z => z.Kind == GroundwaterZoneKind.Unsaturated) != unsaturatedCount)
                throw new InvalidOperationException("Saturated toggle changed the unsaturated material-zone overlay.");
            if (geo.LastSaturatedZoneAxisPolygonForTests.Count < 3)
                throw new InvalidOperationException("Hiding saturated wash removed verification geometry.");

            gw.ShowUnsaturatedMaterialZone = false;
            if (geo.GroundwaterZoneOverlays.Any(z => z.Kind == GroundwaterZoneKind.Unsaturated))
                throw new InvalidOperationException("Unsaturated material-zone overlay remained visible when unchecked.");
            if (geo.GroundwaterPhreaticPolyline.Count != phreaticPointCount)
                throw new InvalidOperationException("Material-zone toggles changed the phreatic polyline.");
        }
        finally
        {
            gw.ShowSaturatedMaterialZone = oldShowSaturated;
            gw.ShowUnsaturatedMaterialZone = oldShowUnsaturated;
        }
    }

    private static void VerifySaturatedZoneAlignsWithPhreatic(GeometryDefinitionViewModel geo)
    {
        var axisPoly = geo.LastSaturatedZoneAxisPolygonForTests;
        if (axisPoly.Count < 3)
            throw new InvalidOperationException("GW saturated zone axis polygon missing.");

        const double tol = 0.05;
        var soil = geo.GeometryVertexRows.Select(v => (x: v.AxisX, y: v.AxisY)).ToList();

        // WPF boolean flatten may not preserve exact surf vertices; probe top-of-fill vs phreatic Y
        // only where the WT lies at or below the soil surface (airborne peaks follow ground instead).
        foreach (var surf in geo.GroundwaterSurfPoints)
        {
            double groundY;
            try
            {
                groundY = MaxBoundaryYAtX(soil, surf.AxisX);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            if (surf.AxisY > groundY + tol)
                continue;

            double topY;
            try
            {
                topY = MaxBoundaryYAtX(axisPoly, surf.AxisX);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            if (Math.Abs(topY - surf.AxisY) > tol)
                throw new InvalidOperationException(
                    $"GW saturated top y={topY:F2} at x={surf.AxisX:F2} expected phreatic y~{surf.AxisY:F2}.");
        }

        // Example16-style bench + sloping face (only when that WT is present).
        if (geo.GroundwaterSurfPoints.Any(p =>
                Math.Abs(p.AxisX - 10.0) <= 0.5 && Math.Abs(p.AxisY - 15.0) <= 0.5))
        {
            var benchTopY = MaxBoundaryYAtX(axisPoly, x: 10);
            if (Math.Abs(benchTopY - 15.0) > tol)
                throw new InvalidOperationException(
                    $"GW saturated bench top y={benchTopY:F2} expected ~15.00 (phreatic).");

            var slopeTopY = MaxBoundaryYAtX(axisPoly, x: 30);
            var expectedSlopeY = 15.0 + (30.0 - 18.0) * (35.0 - 15.0) / (48.0 - 18.0);
            if (Math.Abs(slopeTopY - expectedSlopeY) > tol)
                throw new InvalidOperationException(
                    $"GW saturated slope top y={slopeTopY:F2} expected ~{expectedSlopeY:F2} (phreatic).");

            if (slopeTopY < 20.0)
                throw new InvalidOperationException(
                    $"GW saturated slope top y={slopeTopY:F2} still capped below slope phreatic (old convex-clip bug).");
        }

        VerifySaturatedZoneStaysInsideSoilWhenWtAboveGround(geo);
    }

    /// <summary>
    /// Always-on regression (even when the loaded .xfem has groundwater disabled).
    /// </summary>
    private static void VerifySaturatedZoneAirbornePeakRegression(MainWindowViewModel vm)
    {
        var geo = vm.GeometryDefinitionVM;
        if (geo.GeometryVertexRows.Count < 3)
            return;

        var wasEnabled = vm.GroundwaterVM.IsEnabled;
        try
        {
            vm.GroundwaterVM.IsEnabled = true;
            VerifySaturatedZoneStaysInsideSoilWhenWtAboveGround(geo);
        }
        finally
        {
            vm.GroundwaterVM.IsEnabled = wasEnabled;
        }
    }

    /// <summary>
    /// Submerged slopes (Ex35): ponded free-water region exists above ground; Ru projects skip it.
    /// </summary>
    private static void VerifyPondedFreeWaterIfPresent(MainWindowViewModel vm, string xfemPath)
    {
        var gw = vm.GroundwaterVM;
        var geo = vm.GeometryDefinitionVM;
        var isEx35 = xfemPath.Contains("Example35", StringComparison.OrdinalIgnoreCase)
                     || xfemPath.Contains("example35", StringComparison.OrdinalIgnoreCase);

        // Ru mode must never show a ponded free-water region (no free-water head above ground).
        if (gw.IsEnabled && gw.IsRuMode)
        {
            geo.RefreshGroundwaterOverlays();
            if (geo.PondedWaterOverlays.Count != 0 || geo.LastPondedZoneAxisPolygonForTests.Count >= 3)
                throw new InvalidOperationException("Ru mode must not build a ponded free-water overlay.");
            return;
        }

        if (!gw.IsEnabled || !gw.IsHydrostaticMode || !gw.IsPondedFreeWaterAllowed)
            return;

        gw.ShowPondedFreeWaterRegion = true;
        // Ponded depth field is Groundwater-tab only (same gate as saturated wash).
        geo.ShowGroundwaterZoneOverlaysOnCanvas = false;
        geo.RefreshGroundwaterOverlays();
        if (geo.IsPondedFreeWaterOverlayVisible)
            throw new InvalidOperationException("Ponded depth field must be hidden off the Groundwater tab.");

        geo.ShowGroundwaterZoneOverlaysOnCanvas = true;
        geo.RefreshGroundwaterOverlays();

        if (isEx35)
        {
            if (geo.LastPondedZoneAxisPolygonForTests.Count < 3)
                throw new InvalidOperationException("Ex35: expected ponded free-water polygon above crest.");
            if (geo.PondedWaterOverlays.Count == 0)
                throw new InvalidOperationException("Ex35: expected ponded depth-field overlay.");
            if (!geo.IsPondedFreeWaterOverlayVisible)
                throw new InvalidOperationException("Ex35: ponded overlay should be visible on Groundwater tab.");

            gw.ShowPondedHeadIsobars = true;
            geo.RefreshGroundwaterOverlays();
            if (geo.PondedWaterIsobars.Count == 0 || !geo.IsPondedHeadIsobarsVisible)
                throw new InvalidOperationException("Ex35: expected head isobars when ShowPondedHeadIsobars is on.");

            gw.ShowPondedHeadIsobars = false;
            geo.RefreshGroundwaterOverlays();
            if (geo.IsPondedHeadIsobarsVisible)
                throw new InvalidOperationException("Ex35: head isobars must hide when checkbox is off.");
            gw.ShowPondedHeadIsobars = true;

            gw.ShowPondedFacePressureArrows = true;
            geo.RefreshGroundwaterOverlays();
            if (vm.MeshVM.IsDiscretized)
            {
                if (geo.PondedFacePressureOverlays.Count == 0 || !geo.IsPondedFacePressureArrowsVisible)
                    throw new InvalidOperationException(
                        "Ex35: expected DistributedEdgeLoads face-pressure arrows on a discretized mesh.");
            }

            // Must not paint under the foundation / through the soil mass.
            var soil = geo.GeometryVertexRows.Select(v => (AxisX: v.AxisX, AxisY: v.AxisY)).ToList();
            var yMin = soil.Min(p => p.AxisY);
            var ySpan = Math.Max(1e-6, soil.Max(p => p.AxisY) - yMin);
            var baseBand = 0.05 * ySpan;
            foreach (var p in geo.LastPondedZoneAxisPolygonForTests)
            {
                if (p.y < yMin + baseBand)
                    throw new InvalidOperationException(
                        $"Ex35 ponded vertex ({p.x:F2},{p.y:F2}) is under/near the foundation — must stay above the slope surface.");
            }

            // Total Head method at y=75 should also produce a ponded region.
            gw.PondedWaterLevelMethod = PondedWaterLevelMethod.TotalHead;
            gw.PondedTotalHead = 75.0;
            geo.RefreshGroundwaterOverlays();
            if (geo.LastPondedZoneAxisPolygonForTests.Count < 3)
                throw new InvalidOperationException("Ex35 Total Head: expected ponded free-water polygon.");

            gw.PondedWaterLevelMethod = PondedWaterLevelMethod.PhreaticLine;
            geo.RefreshGroundwaterOverlays();

            // Switching to Ru clears ponded free-water viz (plan: Ru → no ponded UI / loads).
            var wasSurfaces = vm.ProjectSettingsVM.GroundwaterStaticSettings.IncludeWaterSurfaces;
            var wasRu = vm.ProjectSettingsVM.GroundwaterStaticSettings.IncludeRuCoefficients;
            var savedSurf = geo.GroundwaterSurfPoints.Select(p => (p.AxisX, p.AxisY)).ToList();
            try
            {
                vm.ProjectSettingsVM.GroundwaterStaticSettings.IncludeRuCoefficients = true;
                gw.Method = GroundwaterMethod.Ru;
                geo.RefreshGroundwaterOverlays();
                if (geo.PondedWaterOverlays.Count != 0)
                    throw new InvalidOperationException("Ex35: ponded overlay must clear in Ru mode.");
            }
            finally
            {
                gw.Method = GroundwaterMethod.Hydrostatic;
                vm.ProjectSettingsVM.GroundwaterStaticSettings.IncludeWaterSurfaces = wasSurfaces;
                vm.ProjectSettingsVM.GroundwaterStaticSettings.IncludeRuCoefficients = wasRu;
                geo.ApplyGroundwaterSurfPoints(savedSurf);
                geo.RefreshGroundwaterOverlays();
            }
        }

        // When Static ponded is off, overlays and status go inactive.
        var wasPonded = vm.ProjectSettingsVM.GroundwaterStaticSettings.IncludePondedFreeWater;
        try
        {
            vm.ProjectSettingsVM.GroundwaterStaticSettings.IncludePondedFreeWater = false;
            geo.RefreshGroundwaterOverlays();
            if (geo.PondedWaterOverlays.Count != 0)
                throw new InvalidOperationException("Ponded overlays remained after Static ponded OFF.");
        }
        finally
        {
            vm.ProjectSettingsVM.GroundwaterStaticSettings.IncludePondedFreeWater = wasPonded;
            geo.RefreshGroundwaterOverlays();
        }
    }

    /// <summary>
    /// Regression: an airborne WT peak must not invent saturated fill outside the soil outline.
    /// </summary>
    private static void VerifySaturatedZoneStaysInsideSoilWhenWtAboveGround(GeometryDefinitionViewModel geo)
    {
        if (geo.GeometryVertexRows.Count < 3)
            return;

        var soil = geo.GeometryVertexRows.Select(v => (AxisX: v.AxisX, AxisY: v.AxisY)).ToList();
        var xMin = soil.Min(p => p.AxisX);
        var xMax = soil.Max(p => p.AxisX);
        var yMin = soil.Min(p => p.AxisY);
        var yMax = soil.Max(p => p.AxisY);
        var peakX = 0.5 * (xMin + xMax);
        var peakY = yMax + Math.Max(5.0, (xMax - xMin) * 0.1);
        var midY = 0.5 * (yMin + yMax);

        var original = geo.GroundwaterSurfPoints.Select(p => (p.AxisX, p.AxisY)).ToList();
        try
        {
            geo.ApplyGroundwaterSurfPoints(new[]
            {
                (xMin, midY),
                (peakX, peakY),
                (xMax, midY)
            });
            geo.RefreshGroundwaterOverlays();

            var sat = geo.LastSaturatedZoneAxisPolygonForTests;
            if (sat.Count < 3)
                throw new InvalidOperationException("GW saturated zone missing for above-ground WT peak.");

            const double outsideTol = 0.25;
            foreach (var p in sat)
            {
                if (!AxisPointInsideOrOnPolygon(p.x, p.y, soil, outsideTol))
                    throw new InvalidOperationException(
                        $"GW saturated vertex ({p.x:F2},{p.y:F2}) lies outside soil for airborne WT peak.");
            }

            double groundAtPeak;
            try
            {
                groundAtPeak = MaxBoundaryYAtX(
                    soil.Select(p => (p.AxisX, p.AxisY)).ToList(), peakX);
            }
            catch (InvalidOperationException)
            {
                return;
            }

            // Ponded exterior region may (and should) occupy free water above ground.
            var ponded = geo.LastPondedZoneAxisPolygonForTests;
            if (ponded.Count >= 3
                && !ponded.Any(p => !AxisPointInsideOrOnPolygon(p.x, p.y, soil, outsideTol)))
            {
                throw new InvalidOperationException(
                    "Expected ponded free-water polygon to extend outside the soil for airborne WT peak.");
            }

            var fillTopAtPeak = MaxBoundaryYAtX(sat, peakX);
            if (fillTopAtPeak > groundAtPeak + outsideTol)
                throw new InvalidOperationException(
                    $"GW saturated top y={fillTopAtPeak:F2} at peak x exceeds ground y={groundAtPeak:F2}.");
        }
        finally
        {
            geo.ApplyGroundwaterSurfPoints(original);
            geo.RefreshGroundwaterOverlays();
        }
    }

    private static bool AxisPointInsideOrOnPolygon(
        double x,
        double y,
        IReadOnlyList<(double AxisX, double AxisY)> poly,
        double tol)
    {
        // Ray cast with a soft outside tolerance via distance-to-boundary.
        var inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var xi = poly[i].AxisX;
            var yi = poly[i].AxisY;
            var xj = poly[j].AxisX;
            var yj = poly[j].AxisY;
            var denom = yj - yi;
            if (Math.Abs(denom) < 1e-14)
                continue;
            var intersect = ((yi > y) != (yj > y))
                && (x < (xj - xi) * (y - yi) / denom + xi);
            if (intersect) inside = !inside;
        }

        if (inside) return true;

        for (var i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            if (DistancePointToSegment(x, y, a.AxisX, a.AxisY, b.AxisX, b.AxisY) <= tol)
                return true;
        }

        return false;
    }

    private static double DistancePointToSegment(
        double px, double py, double ax, double ay, double bx, double by)
    {
        var dx = bx - ax;
        var dy = by - ay;
        var len2 = dx * dx + dy * dy;
        if (len2 < 1e-24)
        {
            var ex = px - ax;
            var ey = py - ay;
            return Math.Sqrt(ex * ex + ey * ey);
        }

        var t = Math.Clamp(((px - ax) * dx + (py - ay) * dy) / len2, 0.0, 1.0);
        var qx = ax + t * dx - px;
        var qy = ay + t * dy - py;
        return Math.Sqrt(qx * qx + qy * qy);
    }

    private static double MaxBoundaryYAtX(IReadOnlyList<(double x, double y)> axisPoly, double x)
    {
        var maxY = double.NegativeInfinity;
        for (var i = 0; i < axisPoly.Count; i++)
        {
            var a = axisPoly[i];
            var b = axisPoly[(i + 1) % axisPoly.Count];
            var xLo = Math.Min(a.x, b.x);
            var xHi = Math.Max(a.x, b.x);
            if (x < xLo - 1e-9 || x > xHi + 1e-9 || Math.Abs(b.x - a.x) < 1e-12)
                continue;
            var t = (x - a.x) / (b.x - a.x);
            var y = a.y + t * (b.y - a.y);
            maxY = Math.Max(maxY, y);
        }

        if (double.IsNegativeInfinity(maxY))
            throw new InvalidOperationException($"GW saturated zone boundary has no edge crossing x={x}.");
        return maxY;
    }

    private static void VerifyProjectSettingsAnalysisSubtabs(MainWindowViewModel vm)
    {
        var ps = vm.ProjectSettingsVM;
        if (ps.SettingsTabs.Count != 3)
            throw new InvalidOperationException($"Expected exactly 3 Project Settings tabs, got {ps.SettingsTabs.Count}.");

        ps.SelectSettingsTab(ProjectSettingsTabKind.GridAxes);
        var gridIndex = ps.SelectedSettingsTabIndex;
        if (ps.SettingsTabs[gridIndex].Kind != ProjectSettingsTabKind.GridAxes)
            throw new InvalidOperationException("SelectSettingsTab(GridAxes) did not land on Grid & Axes.");

        if (ps.AnalysisTree.Count == 0)
            throw new InvalidOperationException("Analysis tree is empty after project load.");

        var mat = vm.MaterialPropertiesVM;
        if (ps.AnalysisTree.Count != mat.AnalysisCategories.Count)
            throw new InvalidOperationException(
                $"Analysis tree has {ps.AnalysisTree.Count} categories, expected {mat.AnalysisCategories.Count}.");

        // Selecting a tree type node must drive the MaterialProperties selection.
        var structural = ps.AnalysisTree.First(c => c.Name == "Structural – static elasticity");
        var firstType = structural.Types.FirstOrDefault()
            ?? throw new InvalidOperationException("Structural category has no type children in the Analysis tree.");
        ps.SelectedAnalysisTreeNode = firstType;
        if (!string.Equals(mat.SelectedAnalysisCategory, structural.Name, StringComparison.Ordinal)
            || !string.Equals(mat.SelectedDetailedAnalysisType, firstType.Name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Selecting an Analysis tree node did not update MaterialPropertiesVM selection.");
        }

        // Conversely, changing the MaterialProperties selection must sync the tree.
        mat.SelectedAnalysisCategory = MaterialPropertiesViewModel.GroundwaterAnalysisCategory;
        mat.SelectedDetailedAnalysisType = MaterialPropertiesViewModel.GroundwaterStaticType;
        var selected = ps.SelectedAnalysisTreeNode
            ?? throw new InvalidOperationException("Analysis tree selection did not sync from MaterialPropertiesVM.");
        if (!string.Equals(selected.Category, MaterialPropertiesViewModel.GroundwaterAnalysisCategory, StringComparison.Ordinal)
            || !string.Equals(selected.Name, MaterialPropertiesViewModel.GroundwaterStaticType, StringComparison.Ordinal)
            || !selected.IsSelected)
        {
            throw new InvalidOperationException("Analysis tree selection does not match MaterialPropertiesVM selection.");
        }

        // Selecting the GW category node opens the category detail panel (Include Groundwater / γw).
        ps.SelectGroundwaterCategoryNode();
        if (!ps.IsGroundwaterCategorySelected)
            throw new InvalidOperationException("SelectGroundwaterCategoryNode did not select the GW category panel.");
        if (ps.SelectedAnalysisTreeNode != null)
            throw new InvalidOperationException("GW category selection should clear the type-node selection.");
        if (ps.SelectedAnalysisDetail is not AnalysisCategoryNode cat
            || !MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(cat.Name))
        {
            throw new InvalidOperationException("SelectedAnalysisDetail is not the GW category node.");
        }
        if (ps.GroundwaterVM == null)
            throw new InvalidOperationException("GroundwaterVM is not wired into ProjectSettingsViewModel.");

        // Include Groundwater is the master switch; Static options become active only when it is on.
        ps.GroundwaterVM.IsEnabled = false;
        if (ps.AreGroundwaterTypeOptionsEnabled)
            throw new InvalidOperationException("AreGroundwaterTypeOptionsEnabled should be false when Include Groundwater is off.");
        ps.GroundwaterVM.IsEnabled = true;
        if (!ps.AreGroundwaterTypeOptionsEnabled)
            throw new InvalidOperationException("AreGroundwaterTypeOptionsEnabled should be true when Include Groundwater is on.");

        // Water pressure grids are gated by the Static checkboxes; dialog Grid Type list follows flags.
        var gw = vm.GroundwaterVM;
        ps.GroundwaterStaticSettings.IncludeWaterPressureGridTotalHead = true;
        ps.GroundwaterStaticSettings.IncludeWaterPressureGridPressureHead = false;
        ps.GroundwaterStaticSettings.IncludeWaterPressureGridPorePressure = true;
        if (!gw.IsTotalHeadGridAllowed || gw.IsPressureHeadGridAllowed || !gw.IsPorePressureGridAllowed)
            throw new InvalidOperationException("Water pressure grid visibility flags do not match Static checkboxes.");
        if (!gw.IsAnyWaterPressureGridAllowed)
            throw new InvalidOperationException("IsAnyWaterPressureGridAllowed should be true when any Static WPG flag is on.");

        gw.WaterPressureGrids.Clear();
        var thGrid = new WaterPressureGrid("TH Grid", WaterPressureGridType.TotalHead);
        thGrid.Points.Add(new WaterPressureGridPoint(1, 1.0, 2.0, 10.0));
        gw.WaterPressureGrids.Add(thGrid);
        var ppGrid = new WaterPressureGrid("PP Grid", WaterPressureGridType.PorePressure);
        ppGrid.Points.Add(new WaterPressureGridPoint(1, 3.0, 4.0, 20.0));
        gw.WaterPressureGrids.Add(ppGrid);

        var allowedTypes = new System.Collections.Generic.List<WaterPressureGridType>();
        if (gw.IsTotalHeadGridAllowed) allowedTypes.Add(WaterPressureGridType.TotalHead);
        if (gw.IsPressureHeadGridAllowed) allowedTypes.Add(WaterPressureGridType.PressureHead);
        if (gw.IsPorePressureGridAllowed) allowedTypes.Add(WaterPressureGridType.PorePressure);
        var dlgVm = new WaterPressureGridDialogViewModel(gw.WaterPressureGrids, allowedTypes, WaterPressureGridType.TotalHead);
        if (dlgVm.AllowedGridTypes.Any(o => o.Type == WaterPressureGridType.PressureHead))
            throw new InvalidOperationException("Pressure Head must not appear in dialog Grid Type when its Static flag is off.");
        if (!dlgVm.AllowedGridTypes.Any(o => o.Type == WaterPressureGridType.TotalHead)
            || !dlgVm.AllowedGridTypes.Any(o => o.Type == WaterPressureGridType.PorePressure))
            throw new InvalidOperationException("Dialog Grid Type list missing enabled Total Head / Pore Pressure.");

        var wpgPath = Path.Combine(Path.GetTempPath(), "load_xfem_smoke_wpg_named.xfem");
        XfemProjectPersistence.Save(vm, wpgPath);
        gw.WaterPressureGrids.Clear();
        XfemProjectPersistence.Load(vm, wpgPath);
        if (gw.WaterPressureGrids.Count != 2)
            throw new InvalidOperationException($"Named water pressure grids did not round-trip (count={gw.WaterPressureGrids.Count}).");
        if (!gw.WaterPressureGrids.Any(g =>
                g.Name == "TH Grid"
                && g.GridType == WaterPressureGridType.TotalHead
                && g.Points.Any(p => Math.Abs(p.X - 1.0) < 1e-9 && Math.Abs(p.Value - 10.0) < 1e-9)))
            throw new InvalidOperationException("Total Head named grid did not round-trip through .xfem.");
        if (!gw.WaterPressureGrids.Any(g =>
                g.Name == "PP Grid"
                && g.GridType == WaterPressureGridType.PorePressure
                && g.Points.Any(p => Math.Abs(p.X - 3.0) < 1e-9 && Math.Abs(p.Value - 20.0) < 1e-9)))
            throw new InvalidOperationException("Pore Pressure named grid did not round-trip through .xfem.");

        // Legacy flat-list load → one Default Grid per non-empty type.
        var legacyModel = new GroundwaterModel
        {
            IsEnabled = true,
            TotalHeadGridPoints = new System.Collections.Generic.List<(double X, double Y, double Value)> { (5, 6, 7) }
        };
        gw.ApplyImportedModel(legacyModel);
        if (gw.WaterPressureGrids.Count != 1
            || gw.WaterPressureGrids[0].Name != "Default Grid"
            || gw.WaterPressureGrids[0].GridType != WaterPressureGridType.TotalHead
            || gw.WaterPressureGrids[0].Points.Count != 1)
        {
            throw new InvalidOperationException("Legacy flat TotalHeadGridPoints did not migrate to a Default Grid.");
        }

        _ = ps.GroundwaterVM.GammaW;
        if (!string.Equals(mat.SelectedAnalysisCategory, MaterialPropertiesViewModel.GroundwaterAnalysisCategory, StringComparison.Ordinal))
            throw new InvalidOperationException("GW category selection did not keep Flow-Seepage/Groundwater as the analysis category.");
    }

    private static void VerifyGroundwaterStaticSubtab(MainWindowViewModel vm)
    {
        var ps = vm.ProjectSettingsVM;
        var mat = vm.MaterialPropertiesVM;
        mat.SelectedAnalysisCategory = MaterialPropertiesViewModel.GroundwaterAnalysisCategory;
        mat.SelectedDetailedAnalysisType = MaterialPropertiesViewModel.GroundwaterStaticType;

        if (!ps.IsGroundwaterStaticSelected)
            throw new InvalidOperationException("IsGroundwaterStaticSelected is false with GW Static selected.");
        if (!ps.IsGroundwaterStaticDetailSelected)
            throw new InvalidOperationException("IsGroundwaterStaticDetailSelected is false with GW Static type node selected.");
        if (ps.IsGroundwaterCategorySelected)
            throw new InvalidOperationException("Category panel should not be active when Static type is selected.");

        // Master switch must be on for Static options / GW tab inputs to be usable.
        if (ps.GroundwaterVM != null)
            ps.GroundwaterVM.IsEnabled = true;
        if (!ps.AreGroundwaterTypeOptionsEnabled)
            throw new InvalidOperationException("Static options should be active when Include Groundwater is checked.");

        var settings = ps.GroundwaterStaticSettings;
        settings.ApplyDefaults();
        if (settings.IncludeWaterSurfaces || settings.IncludeRuCoefficients
            || settings.IncludeWaterPressureGridTotalHead
            || settings.IncludeWaterPressureGridPressureHead
            || settings.IncludeWaterPressureGridPorePressure)
        {
            throw new InvalidOperationException("Groundwater Static settings did not default to all unchecked.");
        }
        if (!settings.IncludePondedFreeWater)
            throw new InvalidOperationException("Ponded free-water pressures should default ON.");

        var outputsUnchecked = vm.ResultsVM.AvailableOutputTypes;
        if (outputsUnchecked.Contains(ResultsViewModel.OutputWaterPressureGridPressureHead)
            || outputsUnchecked.Contains(ResultsViewModel.OutputWaterPressureGridTotalHead)
            || outputsUnchecked.Contains(ResultsViewModel.OutputWaterPressureGridPorePressure))
        {
            throw new InvalidOperationException("Results output list shows grid modes while all Static options are unchecked.");
        }

        settings.IncludeWaterSurfaces = true;
        settings.IncludeWaterPressureGridTotalHead = true;
        var staticPath = Path.Combine(Path.GetTempPath(), "load_xfem_smoke_gw_static.xfem");
        XfemProjectPersistence.Save(vm, staticPath);
        settings.ApplyDefaults();
        XfemProjectPersistence.Load(vm, staticPath);
        if (!ps.GroundwaterStaticSettings.IncludeWaterSurfaces
            || !ps.GroundwaterStaticSettings.IncludeWaterPressureGridTotalHead
            || ps.GroundwaterStaticSettings.IncludeRuCoefficients)
        {
            throw new InvalidOperationException("Static flags did not round-trip through .xfem.");
        }
        if (!ps.IsGroundwaterStaticSelected)
            throw new InvalidOperationException("GW Static selection did not round-trip through .xfem.");

        if (!vm.GroundwaterVM.IsWaterSurfacesAllowed)
            throw new InvalidOperationException("GroundwaterVM blocks water surfaces although the Static flag is on.");
        if (vm.GroundwaterVM.IsRuCoefficientsAllowed)
            throw new InvalidOperationException("GroundwaterVM still allows Ru coefficients when Static flag is off.");
        Console.WriteLine("GW Ru inputs correctly gated off while water surfaces stay enabled.");

        var outputs = vm.ResultsVM.AvailableOutputTypes;
        if (!outputs.Contains(ResultsViewModel.OutputWaterPressureGridTotalHead))
            throw new InvalidOperationException("Results output list missing the enabled Total Head grid mode.");
        if (outputs.Contains(ResultsViewModel.OutputWaterPressureGridPressureHead)
            || outputs.Contains(ResultsViewModel.OutputWaterPressureGridPorePressure))
        {
            throw new InvalidOperationException("Results output list shows grid modes whose Static options are unchecked.");
        }

        VerifyLegacyGroundwaterStaticFallback(vm, staticPath);
    }

    /// <summary>Old .xfem files stored GW Static flags on an AnalysisTypeSubtabs record; loading one must still restore them.</summary>
    private static void VerifyLegacyGroundwaterStaticFallback(MainWindowViewModel vm, string savedPath)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(savedPath))
            ?? throw new InvalidOperationException("Could not parse saved .xfem JSON.");
        var settingsNode = root["projectSettings"]
            ?? throw new InvalidOperationException("Saved .xfem has no projectSettings node.");

        var settingsObj = settingsNode.AsObject();
        var geometryObj = root["geometry"]?.AsObject()
            ?? throw new InvalidOperationException("Saved .xfem has no geometry node.");
        settingsObj.Remove("gwStaticIncludeWaterSurfaces");
        settingsObj.Remove("gwStaticIncludePondedFreeWater");
        settingsObj.Remove("gwStaticIncludeRuCoefficients");
        settingsObj.Remove("gwStaticIncludeWaterPressureGridTotalHead");
        settingsObj.Remove("gwStaticIncludeWaterPressureGridPressureHead");
        settingsObj.Remove("gwStaticIncludeWaterPressureGridPorePressure");
        geometryObj.Remove("groundwaterShowSaturatedMaterialZone");
        geometryObj.Remove("groundwaterShowUnsaturatedMaterialZone");
        geometryObj.Remove("groundwaterShowPondedFreeWaterRegion");
        geometryObj.Remove("groundwaterPondedWaterLevelMethod");
        geometryObj.Remove("groundwaterPondedTotalHead");
        settingsObj["analysisTypeSubtabs"] = new System.Text.Json.Nodes.JsonArray(
            new System.Text.Json.Nodes.JsonObject
            {
                ["analysisCategory"] = MaterialPropertiesViewModel.GroundwaterAnalysisCategory,
                ["detailedAnalysisType"] = MaterialPropertiesViewModel.GroundwaterStaticType,
                ["header"] = MaterialPropertiesViewModel.GroundwaterStaticType,
                ["includeWaterSurfaces"] = true,
                ["includeRuCoefficients"] = false
            });

        var legacyPath = Path.Combine(Path.GetTempPath(), "load_xfem_smoke_gw_static_legacy.xfem");
        File.WriteAllText(legacyPath, root.ToJsonString());

        var ps = vm.ProjectSettingsVM;
        ps.GroundwaterStaticSettings.ApplyDefaults();
        XfemProjectPersistence.Load(vm, legacyPath);

        if (ps.GroundwaterStaticSettings.IncludeRuCoefficients)
            throw new InvalidOperationException("Legacy AnalysisTypeSubtabs flags were not restored.");
        if (!ps.GroundwaterStaticSettings.IncludeWaterSurfaces)
            throw new InvalidOperationException("Legacy explicit includeWaterSurfaces=true was not restored.");
        if (ps.GroundwaterStaticSettings.IncludeWaterPressureGridTotalHead)
            throw new InvalidOperationException("Legacy fallback should default unspecified flags to unchecked.");
        if (!ps.IsGroundwaterStaticSelected)
            throw new InvalidOperationException("Legacy GW Static subtab did not select GW Static analysis type.");
        if (!vm.GroundwaterVM.ShowSaturatedMaterialZone
            || vm.GroundwaterVM.ShowUnsaturatedMaterialZone)
        {
            throw new InvalidOperationException(
                "Legacy groundwater zone-display defaults were not restored.");
        }
    }
}
