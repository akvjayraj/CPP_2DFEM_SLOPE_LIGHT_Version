using System;
using System.Windows;
using System.Windows.Media;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>Builds RS2D-style point load overlay geometry from a <see cref="LoadModel"/> row.</summary>
public static class PointLoadOverlayBuilder
{
    private const double ArrowLengthCanvas = 16.0;
    private const double LabelOffsetCanvas = 8.0;
    private const double HeadOutwardOffsetCanvas = 5.0;
    private const double LabelExtraMargin = 16.0;

    public static PointLoadOverlayDisplay? Build(
        LoadModel load,
        MeshViewModel? mesh,
        GeometryDefinitionViewModel geometry,
        ProblemDimension dimension)
    {
        if (!load.IsPointLoad || !IsPointLoadPlaced(load))
            return null;

        var contact = LoadPlacementCoords.ResolvePointCanvas(load, geometry);
        var dir = ResolvePointInwardDirection(load, geometry, mesh, contact, dimension);
        if (dir.Length < 1e-6)
            return null;

        dir.Normalize();
        var outward = new Vector(-dir.X, -dir.Y);

        double fitScale = ComputeFitScale(contact, outward, geometry);
        double headOffset = HeadOutwardOffsetCanvas * fitScale;
        double arrowLen = ArrowLengthCanvas * fitScale;
        double labelOffset = LabelOffsetCanvas * fitScale;
        double headSize = Math.Max(2.5, 5.0 * fitScale);

        var head = new Point(
            contact.X + outward.X * headOffset,
            contact.Y + outward.Y * headOffset);
        var tail = new Point(head.X + outward.X * arrowLen, head.Y + outward.Y * arrowLen);
        var labelAnchor = new Point(
            tail.X + outward.X * labelOffset,
            tail.Y + outward.Y * labelOffset);

        var headDir = new Vector(head.X - tail.X, head.Y - tail.Y);
        if (headDir.Length > 1e-6)
            headDir.Normalize();
        else
            headDir = dir;
        var perp = new Vector(-headDir.Y, headDir.X);
        var headLeft = new Point(
            head.X - headDir.X * headSize + perp.X * headSize * 0.45,
            head.Y - headDir.Y * headSize + perp.Y * headSize * 0.45);
        var headRight = new Point(
            head.X - headDir.X * headSize - perp.X * headSize * 0.45,
            head.Y - headDir.Y * headSize - perp.Y * headSize * 0.45);

        double fontSize = LoadOverlayShared.ComputeLabelFontSize(arrowLen + labelOffset, 1, fitScale);
        var label = LoadOverlayShared.CreateLabel(labelAnchor, LoadOverlayShared.FormatMagnitude(load.Magnitude), fontSize);
        LoadOverlayShared.ClampLabelsInsideCanvas(new[] { label }, geometry);

        return new PointLoadOverlayDisplay
        {
            Arrow = new LineLoadArrowDisplay
            {
                HeadX = head.X,
                HeadY = head.Y,
                TailX = tail.X,
                TailY = tail.Y,
                HeadLeftX = headLeft.X,
                HeadLeftY = headLeft.Y,
                HeadRightX = headRight.X,
                HeadRightY = headRight.Y,
                ArrowHeadPoints = new PointCollection { head, headLeft, headRight },
            },
            Label = label,
        };
    }

    private static bool IsPointLoadPlaced(LoadModel load)
        => load.AppliedToNodeId is > 0
           || Math.Abs(load.StartX) > 1e-6
           || Math.Abs(load.StartY) > 1e-6;

    private static double ComputeFitScale(
        Point contact,
        Vector outward,
        GeometryDefinitionViewModel geometry)
    {
        double requiredExtent = HeadOutwardOffsetCanvas
            + ArrowLengthCanvas
            + LabelOffsetCanvas
            + LabelExtraMargin;

        double clearance = LoadOverlayShared.ComputeOutwardClearance(
            contact,
            outward,
            geometry.CanvasWidth,
            geometry.CanvasHeight,
            LoadOverlayShared.CanvasEdgeMargin);

        if (!double.IsFinite(clearance) || clearance >= requiredExtent)
            return 1.0;

        return Math.Clamp(clearance / requiredExtent, 0.04, 1.0);
    }

    private static Vector ResolvePointInwardDirection(
        LoadModel load,
        GeometryDefinitionViewModel geometry,
        MeshViewModel? mesh,
        Point contact,
        ProblemDimension dimension)
    {
        if (dimension == ProblemDimension.OneD)
        {
            Vector axisDir = load.Direction switch
            {
                LoadDirection.Y => new Vector(0, load.Magnitude >= 0 ? 1 : -1),
                LoadDirection.X => new Vector(load.Magnitude >= 0 ? 1 : -1, 0),
                _ => new Vector(load.Magnitude >= 0 ? 1 : -1, 0),
            };
            var refAxis = geometry.ConvertCanvasToAxis(contact);
            var axisTarget = new Point(refAxis.X + axisDir.X, refAxis.Y + axisDir.Y);
            var canvasTarget = geometry.ConvertAxisToCanvas(axisTarget);
            return new Vector(canvasTarget.X - contact.X, canvasTarget.Y - contact.Y);
        }

        var axisPt = geometry.ConvertCanvasToAxis(contact);
        int nodeId = LoadExportDirectionResolver.ResolvePointNodeId(load, mesh, geometry);
        if (nodeId > 0)
            axisPt = LoadExportDirectionResolver.ResolveNodeAxisPoint(mesh, geometry, nodeId, load);

        if (LoadExportDirectionResolver.TryResolvePointAxisDirection(load, geometry, mesh, axisPt, out var ux, out var uy))
        {
            var axisTarget = new Point(axisPt.X + ux, axisPt.Y + uy);
            var canvasTarget = geometry.ConvertAxisToCanvas(axisTarget);
            var canvasDir = new Vector(canvasTarget.X - contact.X, canvasTarget.Y - contact.Y);
            if (load.Magnitude < 0 && canvasDir.Length > 1e-6)
                canvasDir = new Vector(-canvasDir.X, -canvasDir.Y);
            if (canvasDir.Length > 1e-6)
                return canvasDir;
        }

        return new Vector(0, load.Magnitude >= 0 ? 1 : -1);
    }
}
