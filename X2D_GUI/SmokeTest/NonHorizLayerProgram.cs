using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using GeotechSlopeApp.ViewModels;

namespace NonHorizLayerSmoke;

// Headless automation of Material Properties -> Layering controls -> Non-Horizontal.
// Drives the GeometryDefinitionViewModel non-horizontal flow directly (no GUI clicking)
// for both a STRAIGHT cut and a CURVED/wavy cut, so the debug instrumentation in the
// view model emits runtime evidence to debug-48af0d.log.
internal static class NonHorizLayerProgram
{
    private const string LogPath = @"C:\Users\akv\.cursor\worktrees\CPP_2DFEM_SLOPE\debug-48af0d.log";

    private static void HarnessLog(string scenario, string msg, string data)
    {
        var line = "{\"sessionId\":\"48af0d\",\"runId\":\"run1\",\"hypothesisId\":\"HARNESS\",\"location\":\"NonHorizLayerProgram\",\"message\":\"" +
                   scenario + " " + msg + "\",\"data\":" + data + ",\"timestamp\":" +
                   DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "}";
        File.AppendAllText(LogPath, line + "\n");
    }

    private static int Main()
    {
        int exitCode = 0;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                // Monotonic-in-x wavy cut (works in earlier run).
                RunScenario("CURVED_MONO", RectBoundary, new[]
                {
                    (2.0, 8.0), (8.0, 14.0), (14.0, 6.0), (20.0, 14.0), (28.0, 9.0)
                });

                // NON-MONOTONIC cut: x doubles back (2 -> 16 -> 8 -> 22 -> 28).
                // This makes ClipBelow/ClipAbove produce a self-intersecting region polygon.
                RunScenario("CURVED_NONMONO", RectBoundary, new[]
                {
                    (2.0, 8.0), (16.0, 14.0), (8.0, 6.0), (22.0, 14.0), (28.0, 9.0)
                });

                // Sloped (non-convex) boundary, like the user's real slope, with a wavy cut.
                RunScenario("SLOPE_CURVED", SlopeBoundary, new[]
                {
                    (2.0, 6.0), (10.0, 11.0), (16.0, 5.0), (24.0, 10.0), (30.0, 7.0)
                });

                // TWO curved cuts -> 3 zones. The MIDDLE zone's outline is made only of cut
                // points (no original boundary vertex), reproducing the live-app drop.
                RunMultiCutScenario("SLOPE_2CUTS", SlopeBoundary, new[]
                {
                    new[] { (1.0, 4.0), (9.0, 7.0), (17.0, 3.5), (25.0, 6.5), (31.0, 4.5) },
                    new[] { (1.0, 11.0), (9.0, 13.5), (17.0, 10.5), (25.0, 7.6), (31.0, 7.2) }
                });
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

    // Closed rectangular boundary (axis coords).
    private static readonly (double x, double y)[] RectBoundary =
    {
        (2, 2), (28, 2), (28, 20), (2, 20)
    };

    // Sloped, non-convex boundary (bench/slope profile).
    private static readonly (double x, double y)[] SlopeBoundary =
    {
        (0, 0), (32, 0), (32, 8), (20, 8), (8, 16), (0, 16)
    };

    private static void RunScenario(string scenario, (double x, double y)[] boundary, (double x, double y)[] cutPoints)
        => RunMultiCutScenario(scenario, boundary, new[] { cutPoints });

    private static void RunMultiCutScenario(string scenario, (double x, double y)[] boundary, (double x, double y)[][] cuts)
    {
        var vm = new GeometryDefinitionViewModel
        {
            CanvasWidth = 600,
            CanvasHeight = 400,
            AxisXMin = 0,
            AxisXMax = 34,
            AxisYMin = 0,
            AxisYMax = 22
        };

        for (var i = 0; i < boundary.Length; i++)
        {
            vm.GeometryVertexRows.Add(new GeometryDefinitionViewModel.GeometryVertexDisplay
            {
                Index = i,
                AxisX = boundary[i].x,
                AxisY = boundary[i].y,
                StablePointId = i + 1
            });
        }
        vm.RebuildGeometryAfterProjectLoad();

        vm.UseLayerNamesOnMaterialCanvas = true;
        vm.IsNonHorizontalLayerMode = true;

        foreach (var cut in cuts)
        {
            vm.StartNonHorizontalLayerDraw();
            foreach (var (x, y) in cut)
                vm.HandleNonHorizontalLayerCanvasClick(vm.ConvertAxisToCanvas(new Point(x, y)));
            vm.FinishNonHorizontalLayerDraw();
        }

        var data = "{\"summaries\":" + vm.LayerCoordinateSummaries.Count +
                   ",\"overlays\":" + vm.SurfaceRegionOverlays.Count +
                   ",\"cutLines\":" + vm.NonHorizontalLayerCanvasLines.Count + "}";
        HarnessLog(scenario, "result", data);
        Console.WriteLine($"{scenario}: summaries={vm.LayerCoordinateSummaries.Count} overlays={vm.SurfaceRegionOverlays.Count}");
    }
}
