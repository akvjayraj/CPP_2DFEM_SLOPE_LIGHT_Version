using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Builds canvas annotations for transient consolidation initial pressure and
/// prescribed-pressure boundaries. The displayed faces use the same bounding-box
/// extrema convention as p84/p88 deck generation.
/// </summary>
public static class TransientPressureOverlayBuilder
{
    private static readonly Brush FixedPressureStroke =
        new SolidColorBrush(Color.FromRgb(0xBE, 0x12, 0x3C));

    static TransientPressureOverlayBuilder() => FixedPressureStroke.Freeze();

    public static IReadOnlyList<LineLoadOverlayDisplay> BuildFixedBoundaries(
        TransientAnalysisSettings settings,
        GeometryDefinitionViewModel geometry,
        string pressureUnit,
        IEnumerable<TransientFixedPressureNode>? explicitRows = null)
    {
        if (geometry.GeometryVertexRows.Count == 0)
            return Array.Empty<LineLoadOverlayDisplay>();

        var (minX, maxX, minY, maxY) = geometry.GetBoundingBoxBounds();
        var result = new List<LineLoadOverlayDisplay>(8);

        if (settings.FixTopFace)
            AddBoundary(result, geometry, new Point(minX, maxY), new Point(maxX, maxY),
                "Top", settings.FixedPressure, pressureUnit, 6, -18);
        if (settings.FixBottomFace)
            AddBoundary(result, geometry, new Point(minX, minY), new Point(maxX, minY),
                "Bottom", settings.FixedPressure, pressureUnit, 6, 5);
        if (settings.FixMinXFace)
            AddBoundary(result, geometry, new Point(minX, minY), new Point(minX, maxY),
                "Min-x", settings.FixedPressure, pressureUnit, 6, -10);
        if (settings.FixMaxXFace)
            AddBoundary(result, geometry, new Point(maxX, minY), new Point(maxX, maxY),
                "Max-x", settings.FixedPressure, pressureUnit, -112, -10);

        if (explicitRows != null)
        {
            foreach (var row in explicitRows.Where(r => r.IsEnabled))
            {
                if (row.IsUserDefined && !row.HasPlacement)
                {
                    var (a, b, label, ox, oy) = row.Face switch
                    {
                        TransientDrainFace.Bottom => (new Point(minX, minY), new Point(maxX, minY), "Bottom", 6.0, 5.0),
                        TransientDrainFace.MinX => (new Point(minX, minY), new Point(minX, maxY), "Min-x", 6.0, -10.0),
                        TransientDrainFace.MaxX => (new Point(maxX, minY), new Point(maxX, maxY), "Max-x", -112.0, -10.0),
                        _ => (new Point(minX, maxY), new Point(maxX, maxY), "Top", 6.0, -18.0)
                    };
                    AddBoundary(result, geometry, a, b, label, row.Pressure, pressureUnit, ox, oy);
                }
                else if (row.HasPlacement && row.IsLinePlacement)
                {
                    AddBoundary(result, geometry,
                        new Point(row.StartX, row.StartY),
                        new Point(row.EndX, row.EndY),
                        "Line", row.Pressure, pressureUnit, 6, -14);
                }
            }
        }

        return result;
    }

    public static PondedWaterCallout? BuildInitialPressureCallout(
        TransientAnalysisSettings settings,
        GeometryDefinitionViewModel geometry,
        string pressureUnit)
    {
        if (geometry.GeometryVertexRows.Count == 0)
            return null;

        var x = geometry.GeometryVertexRows.Average(v => v.AxisX);
        var y = geometry.GeometryVertexRows.Average(v => v.AxisY);
        var canvas = geometry.ConvertAxisToCanvas(new Point(x, y));
        var value = settings.InitialPressure.ToString("G5", CultureInfo.InvariantCulture);
        return new PondedWaterCallout
        {
            X = canvas.X - 48,
            Y = canvas.Y - 12,
            Text = $"Initial p\u2080 = {value} {pressureUnit}".TrimEnd()
        };
    }

    private static void AddBoundary(
        ICollection<LineLoadOverlayDisplay> result,
        GeometryDefinitionViewModel geometry,
        Point axisStart,
        Point axisEnd,
        string face,
        double fixedPressure,
        string pressureUnit,
        double offsetX,
        double offsetY)
    {
        var start = geometry.ConvertAxisToCanvas(axisStart);
        var end = geometry.ConvertAxisToCanvas(axisEnd);
        var points = new PointCollection { start, end };
        var value = fixedPressure.ToString("G5", CultureInfo.InvariantCulture);

        result.Add(new LineLoadOverlayDisplay
        {
            EdgePoints = points,
            BracketStart = start,
            BracketEnd = end,
            Stroke = FixedPressureStroke,
            StrokeThickness = 4,
            Arrows = Array.Empty<LineLoadArrowDisplay>(),
            Labels =
            [
                new LineLoadLabelDisplay
                {
                    X = 0.5 * (start.X + end.X),
                    Y = 0.5 * (start.Y + end.Y),
                    OffsetX = offsetX,
                    OffsetY = offsetY,
                    Text = $"Fixed p = {value} {pressureUnit} ({face})".TrimEnd(),
                    FontSize = 10
                }
            ]
        });
    }
}
