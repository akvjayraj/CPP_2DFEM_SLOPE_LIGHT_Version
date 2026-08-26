using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using GeotechSlopeApp.Models.FiniteElement;

namespace GeotechSlopeApp.Views.Controls;

/// <summary>
/// Shared Q8/T6/Q4 mesh wireframe drawing for FE result canvases.
/// </summary>
internal static class FEMeshWireframeRenderer
{
    internal static (double x, double y) NodePosition(Node node, double exaggeration)
    {
        double ux = node.Displacements.Length > 0 ? node.Displacements[0] : 0;
        double uy = node.Displacements.Length > 1 ? node.Displacements[1] : 0;
        return (node.X + exaggeration * ux, node.Y + exaggeration * uy);
    }

    internal static bool ElementHasDisplacement(ElementBase element, IReadOnlyList<Node> nodes, double minMag = 1e-14)
    {
        foreach (int localIdx in EnumerateLocalNodeIndices(element))
        {
            if (localIdx < 0 || localIdx >= element.NodeIds.Length) continue;
            int ni = element.NodeIds[localIdx];
            if (ni < 0 || ni >= nodes.Count) continue;
            var d = nodes[ni].Displacements;
            if (d.Length >= 2 && System.Math.Sqrt(d[0] * d[0] + d[1] * d[1]) > minMag)
                return true;
        }
        return false;
    }

    private static IEnumerable<int> EnumerateLocalNodeIndices(ElementBase element)
    {
        for (int i = 0; i < element.NodeIds.Length; i++)
            yield return i;
    }

    internal static void AddWireframe(
        Panel canvas,
        IReadOnlyList<ElementBase> elements,
        IReadOnlyList<Node> nodes,
        double coordScale,
        Point offset,
        double exaggeration,
        Brush stroke,
        double thickness,
        bool dashed,
        bool forceFullQuadraticPerimeter,
        bool smithEmb2dQ8LocalOrder,
        Func<double, double, double, double> modelYToCanvas)
    {
        var dash = dashed ? new DoubleCollection { 4, 3 } : null;

        foreach (var element in elements)
        {
            bool useFull = forceFullQuadraticPerimeter
                || (exaggeration > 1.0 + 1e-6 && ElementHasDisplacement(element, nodes));
            var walk = FEMeshOutlineHelper.GetOutlineNodeIndices(element, useFull, smithEmb2dQ8LocalOrder);
            if (walk.Count < 2) continue;

            var points = new PointCollection();
            bool ok = true;
            foreach (int localIdx in walk)
            {
                if (localIdx < 0 || localIdx >= element.NodeIds.Length) { ok = false; break; }
                int ni = element.NodeIds[localIdx];
                if (ni < 0 || ni >= nodes.Count) { ok = false; break; }
                var p = NodePosition(nodes[ni], exaggeration);
                points.Add(new Point(
                    p.x * coordScale + offset.X,
                    modelYToCanvas(p.y, coordScale, offset.Y)));
            }
            if (!ok || points.Count < 2) continue;
            points.Add(points[0]);

            canvas.Children.Add(new Polyline
            {
                Points = points,
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeDashArray = dash,
                Fill = Brushes.Transparent
            });
        }
    }
}
