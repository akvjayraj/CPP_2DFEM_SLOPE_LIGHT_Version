using System;
using System.Windows;
using System.Windows.Media;

namespace GeotechSlopeApp.Models;

/// <summary>
/// Shared mesh outline resolution and axis→canvas conversion for Mesh, Load, and BC canvases.
/// </summary>
public static class MeshCanvasDisplayConverter
{
    /// <summary>Axis-space element outline (Q8 → four corners, T6/Q4 from info row when needed).</summary>
    public static PointCollection ResolveAxisOutline(MeshElementDisplay el, MeshInformationRow? infoRow)
    {
        bool isQ8Element = el.IsQuadraticQuad || infoRow?.IsQ8 == true;

        PointCollection axisOutline;
        if (infoRow is { IsT6: true } && (el.Nodes == null || el.Nodes.Count < 6))
            axisOutline = BuildT6AxisOutlineFromInformationRow(infoRow);
        else if (infoRow is { IsQ8: true } && (infoRow.SmithEmb2dLocalOrder || el.Q8SmithEmb2dLocalOrder
                 || el.Nodes == null || el.Nodes.Count < 8))
            axisOutline = Q8MeshDisplayHelper.BuildCornerQuadOutline(infoRow);
        else if (infoRow is { IsQ4: true } && (el.Nodes == null || el.Nodes.Count < 4))
            axisOutline = BuildQ4AxisOutlineFromInformationRow(infoRow);
        else
            axisOutline = el.Nodes ?? new PointCollection();

        if (isQ8Element && axisOutline.Count >= 8 && !el.PreserveQ8MidEdgeOutline)
        {
            bool smith = infoRow?.SmithEmb2dLocalOrder == true || el.Q8SmithEmb2dLocalOrder;
            axisOutline = Q8CornerQuadFromStoredOutline(axisOutline, smith);
        }

        return axisOutline;
    }

    /// <summary>Build a canvas-space <see cref="MeshElementDisplay"/> copy for drawing on slope/load canvases.</summary>
    public static MeshElementDisplay ToCanvasElement(
        MeshElementDisplay source,
        MeshInformationRow? infoRow,
        Func<Point, Point> axisToCanvas)
    {
        var axisOutline = ResolveAxisOutline(source, infoRow);
        var nodes = new PointCollection();
        foreach (Point p in axisOutline)
            nodes.Add(axisToCanvas(p));

        bool isT6 = source.IsQuadraticTriangle
                    || (infoRow?.IsT6 ?? false)
                    || (axisOutline.Count == 6 && !(infoRow?.IsQuad ?? false));
        bool isQ8 = source.IsQuadraticQuad || infoRow?.IsQ8 == true;
        bool isQ4 = source.IsQuadrilateral || (infoRow?.IsQuad ?? false) || isQ8;

        var cx = axisToCanvas(new Point(source.CenterX, source.CenterY));
        return new MeshElementDisplay
        {
            Nodes = nodes,
            MidsideMarkerPoints = new PointCollection(),
            CenterX = cx.X,
            CenterY = cx.Y,
            DisplayId = source.DisplayId,
            IsActive = source.IsActive,
            IsHighlighted = source.IsHighlighted,
            IsQuadraticTriangle = isT6,
            IsQuadrilateral = isQ4,
            IsQuadraticQuad = isQ8,
            Q8SmithEmb2dLocalOrder = source.Q8SmithEmb2dLocalOrder,
            PreserveQ8MidEdgeOutline = source.PreserveQ8MidEdgeOutline,
            IsUnmatchedTriangle = source.IsUnmatchedTriangle,
        };
    }

    private static PointCollection BuildT6AxisOutlineFromInformationRow(MeshInformationRow row)
    {
        var t6Outline = new PointCollection
        {
            new(row.N1X, row.N1Y), new(row.N4X, row.N4Y), new(row.N2X, row.N2Y),
            new(row.N5X, row.N5Y), new(row.N3X, row.N3Y), new(row.N6X, row.N6Y)
        };
        EnsureCounterClockwisePolygon(t6Outline);
        return t6Outline;
    }

    private static PointCollection BuildQ4AxisOutlineFromInformationRow(MeshInformationRow row)
    {
        var q4Outline = new PointCollection
        {
            new(row.N1X, row.N1Y), new(row.N2X, row.N2Y),
            new(row.N3X, row.N3Y), new(row.N4X, row.N4Y)
        };
        EnsureCounterClockwisePolygon(q4Outline);
        return q4Outline;
    }

    private static PointCollection Q8CornerQuadFromStoredOutline(PointCollection pts, bool smithEmb2dLocalOrder)
    {
        if (pts.Count < 8)
            return pts;
        var quad = smithEmb2dLocalOrder
            ? new PointCollection { pts[0], pts[2], pts[4], pts[6] }
            : new PointCollection { pts[0], pts[1], pts[2], pts[3] };
        EnsureCounterClockwisePolygon(quad);
        return quad;
    }

    private static void EnsureCounterClockwisePolygon(PointCollection pts)
    {
        if (pts == null || pts.Count < 3) return;
        double s = 0;
        int n = pts.Count;
        for (int i = 0; i < n; i++)
        {
            var p = pts[i];
            var q = pts[(i + 1) % n];
            s += p.X * q.Y - q.X * p.Y;
        }
        if (s >= 0) return;
        var arr = new Point[n];
        for (int i = 0; i < n; i++)
            arr[i] = pts[i];
        Array.Reverse(arr);
        pts.Clear();
        foreach (var pt in arr)
            pts.Add(pt);
    }
}
