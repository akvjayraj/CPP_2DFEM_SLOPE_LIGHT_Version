using System.Collections.Generic;
using System.Linq;
using System.Windows;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Models;

/// <summary>Fits the shared geometry axis viewport to a discretized mesh (Smith y-down safe).</summary>
public static class MeshCanvasAxisFitter
{
    public static void FitToPoints(GeometryDefinitionViewModel? geometry, IEnumerable<Point> axisPoints)
    {
        if (geometry == null) return;
        var list = axisPoints as IList<Point> ?? axisPoints.ToList();
        if (list.Count == 0) return;

        double xMin = list.Min(p => p.X), xMax = list.Max(p => p.X);
        double yMin = list.Min(p => p.Y), yMax = list.Max(p => p.Y);
        geometry.FitAxisBoundsToWorldExtents(xMin, xMax, yMin, yMax);
        geometry.EnsureAxisBoundsIncludeContent();
        if (geometry.UseLayerNamesOnMaterialCanvas)
            geometry.RefreshMaterialLayerCanvasOverlays();
    }

    public static void FitToDiscretizationNodes(
        GeometryDefinitionViewModel? geometry,
        IEnumerable<MeshDiscretizationNodeDisplay> nodes)
    {
        if (geometry == null) return;
        FitToPoints(geometry, nodes.Select(n => n.Location));
    }
}
