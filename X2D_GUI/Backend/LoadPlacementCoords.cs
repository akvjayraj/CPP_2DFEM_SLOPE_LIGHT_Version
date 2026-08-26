using System;
using System.Windows;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Load start/end are stored as canvas pixels at runtime (click placement) but verification
/// .xfem files and the Load Conditions grid speak axis metres. Detect and convert so every
/// applied load can be drawn on the Load Conditions canvas.
/// </summary>
public static class LoadPlacementCoords
{
    internal const double MinSegmentLengthCanvasSq = 4.0;

    public static bool TryResolveLineCanvas(
        LoadModel load,
        GeometryDefinitionViewModel? geometry,
        out Point startCanvas,
        out Point endCanvas)
    {
        startCanvas = new Point(load.StartX, load.StartY);
        endCanvas = new Point(load.EndX, load.EndY);
        if (geometry != null && LooksLikeAxisSegment(startCanvas, endCanvas, geometry))
        {
            startCanvas = geometry.ConvertAxisToCanvas(startCanvas);
            endCanvas = geometry.ConvertAxisToCanvas(endCanvas);
        }

        double dx = endCanvas.X - startCanvas.X;
        double dy = endCanvas.Y - startCanvas.Y;
        return dx * dx + dy * dy >= MinSegmentLengthCanvasSq;
    }

    public static Point ResolvePointCanvas(LoadModel load, GeometryDefinitionViewModel? geometry)
    {
        var p = new Point(load.StartX, load.StartY);
        if (geometry != null && LooksLikeAxisPoint(p, geometry))
            return geometry.ConvertAxisToCanvas(p);
        return p;
    }

    public static void NormalizeToCanvas(LoadModel load, GeometryDefinitionViewModel? geometry)
    {
        if (geometry == null) return;
        if (load.IsDistributedLineLoad)
        {
            var start = new Point(load.StartX, load.StartY);
            var end = new Point(load.EndX, load.EndY);
            if (!LooksLikeAxisSegment(start, end, geometry))
                return;
            var cs = geometry.ConvertAxisToCanvas(start);
            var ce = geometry.ConvertAxisToCanvas(end);
            load.StartX = cs.X;
            load.StartY = cs.Y;
            load.EndX = ce.X;
            load.EndY = ce.Y;
            return;
        }

        if (!load.IsPointLoad)
            return;
        var p = new Point(load.StartX, load.StartY);
        if (!LooksLikeAxisPoint(p, geometry))
            return;
        var c = geometry.ConvertAxisToCanvas(p);
        load.StartX = c.X;
        load.StartY = c.Y;
        load.SyncPointEndFromStart();
    }

    public static void WriteAxisForPersist(
        LoadModel load,
        GeometryDefinitionViewModel? geometry,
        out double startX,
        out double startY,
        out double endX,
        out double endY)
    {
        startX = load.StartX;
        startY = load.StartY;
        endX = load.EndX;
        endY = load.EndY;
        if (geometry == null)
            return;

        var start = new Point(startX, startY);
        var end = new Point(endX, endY);
        if (load.IsDistributedLineLoad)
        {
            if (LooksLikeAxisSegment(start, end, geometry))
                return;
            start = geometry.ConvertCanvasToAxis(start);
            end = geometry.ConvertCanvasToAxis(end);
            startX = start.X;
            startY = start.Y;
            endX = end.X;
            endY = end.Y;
            return;
        }

        if (LooksLikeAxisPoint(start, geometry))
            return;
        start = geometry.ConvertCanvasToAxis(start);
        startX = start.X;
        startY = start.Y;
        endX = start.X;
        endY = start.Y;
    }

    public static bool LooksLikeAxisSegment(Point start, Point end, GeometryDefinitionViewModel geometry)
        => LooksLikeAxisPoint(start, geometry) && LooksLikeAxisPoint(end, geometry);

    public static bool LooksLikeAxisPoint(Point p, GeometryDefinitionViewModel geometry)
    {
        double xSpan = Math.Abs(geometry.AxisXMax - geometry.AxisXMin);
        double ySpan = Math.Abs(geometry.AxisYMax - geometry.AxisYMin);
        double pad = 0.05 * Math.Max(Math.Max(xSpan, ySpan), 1.0);
        if (p.X < geometry.AxisXMin - pad || p.X > geometry.AxisXMax + pad)
            return false;
        if (p.Y < geometry.AxisYMin - pad || p.Y > geometry.AxisYMax + pad)
            return false;

        double canvasHint = Math.Max(geometry.CanvasWidth, geometry.CanvasHeight) * 0.15;
        if (canvasHint < 40)
            canvasHint = 40;
        return Math.Max(Math.Abs(p.X), Math.Abs(p.Y)) < canvasHint;
    }
}
