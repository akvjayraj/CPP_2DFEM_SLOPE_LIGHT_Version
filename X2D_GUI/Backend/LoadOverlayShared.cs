using System;
using System.Globalization;
using System.Windows;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>Shared geometry helpers for load canvas overlays.</summary>
internal static class LoadOverlayShared
{
    public const double CanvasEdgeMargin = 18.0;
    public const double MinLabelFontSize = 9.0;
    public const double MaxLabelFontSize = 13.0;

    public static string FormatMagnitude(double magnitude)
    {
        if (Math.Abs(magnitude - Math.Round(magnitude)) < 1e-6)
            return Math.Round(magnitude).ToString(CultureInfo.InvariantCulture);
        return magnitude.ToString("0.##", CultureInfo.InvariantCulture);
    }

    public static double EstimateTextWidth(string text, double fontSize)
        => Math.Max(10.0, text.Length * fontSize * 0.58);

    public static LineLoadLabelDisplay CreateLabel(Point anchor, string text, double fontSize)
    {
        double width = EstimateTextWidth(text, fontSize);
        return new LineLoadLabelDisplay
        {
            X = anchor.X,
            Y = anchor.Y,
            OffsetX = -width * 0.5,
            OffsetY = -fontSize * 1.15,
            Text = text,
            FontSize = fontSize,
        };
    }

    public static double ComputeLabelFontSize(double spacingCanvas, int labelCount, double fitScale)
    {
        double baseSize = 10.0 * Math.Sqrt(Math.Clamp(fitScale, 0.04, 1.0));
        if (labelCount <= 1)
            return Math.Clamp(baseSize, MinLabelFontSize, MaxLabelFontSize);

        double spacing = spacingCanvas / Math.Max(1, labelCount - 1);
        double spacingLimited = spacing / 3.8;
        return Math.Clamp(Math.Min(baseSize, spacingLimited), MinLabelFontSize, MaxLabelFontSize);
    }

    public static void ClampLabelsInsideCanvas(
        IList<LineLoadLabelDisplay> labels,
        GeometryDefinitionViewModel geometry,
        double margin = CanvasEdgeMargin)
    {
        double maxX = geometry.CanvasWidth - margin;
        double maxY = geometry.CanvasHeight - margin;
        foreach (var label in labels)
        {
            double left = label.X + label.OffsetX;
            double top = label.Y + label.OffsetY;
            double width = EstimateTextWidth(label.Text, label.FontSize);
            double height = label.FontSize * 1.2;
            double dx = 0;
            double dy = 0;
            if (left < margin)
                dx = margin - left;
            else if (left + width > maxX)
                dx = maxX - (left + width);
            if (top < margin)
                dy = margin - top;
            else if (top + height > maxY)
                dy = maxY - (top + height);
            if (Math.Abs(dx) > 1e-6 || Math.Abs(dy) > 1e-6)
            {
                label.X += dx;
                label.Y += dy;
            }
        }
    }

    public static double ComputeOutwardClearance(
        Point point,
        Vector outward,
        double canvasWidth,
        double canvasHeight,
        double margin)
    {
        double minDistance = double.PositiveInfinity;

        if (outward.X < -1e-9)
            minDistance = Math.Min(minDistance, (point.X - margin) / -outward.X);
        if (outward.X > 1e-9)
            minDistance = Math.Min(minDistance, (canvasWidth - margin - point.X) / outward.X);
        if (outward.Y < -1e-9)
            minDistance = Math.Min(minDistance, (point.Y - margin) / -outward.Y);
        if (outward.Y > 1e-9)
            minDistance = Math.Min(minDistance, (canvasHeight - margin - point.Y) / outward.Y);

        return double.IsPositiveInfinity(minDistance) ? double.PositiveInfinity : Math.Max(0, minDistance);
    }

    public static bool TryGetMeshCentroidCanvas(
        MeshViewModel? mesh,
        GeometryDefinitionViewModel geometry,
        out Point centroid)
    {
        centroid = default;
        if (mesh is not { IsDiscretized: true } || mesh.DiscretizationNodes.Count == 0)
            return false;

        double sumX = 0;
        double sumY = 0;
        foreach (var node in mesh.DiscretizationNodes)
        {
            var canvas = geometry.ConvertAxisToCanvas(new Point(node.Location.X, node.Location.Y));
            sumX += canvas.X;
            sumY += canvas.Y;
        }

        int count = mesh.DiscretizationNodes.Count;
        centroid = new Point(sumX / count, sumY / count);
        return true;
    }

    public static Vector EnsureTowardSoil(
        Vector canvasDir,
        Point fromPoint,
        MeshViewModel? mesh,
        GeometryDefinitionViewModel geometry)
    {
        if (canvasDir.Length < 1e-6)
            return canvasDir;
        if (!TryGetMeshCentroidCanvas(mesh, geometry, out var centroid))
            return canvasDir;

        var toInterior = new Vector(centroid.X - fromPoint.X, centroid.Y - fromPoint.Y);
        if (toInterior.Length < 1e-6)
            return canvasDir;

        canvasDir.Normalize();
        if (Vector.Multiply(canvasDir, toInterior) < 0)
            canvasDir = new Vector(-canvasDir.X, -canvasDir.Y);
        return canvasDir;
    }
}
