using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Builds a simple line + label overlay for Groundwater Water Head segments
/// (reuses <see cref="LineLoadOverlayDisplay"/> for SlopeCanvasView binding).
/// </summary>
public static class WaterHeadOverlayBuilder
{
    private static readonly Brush SelectedStroke = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
    private static readonly Brush NormalStroke = new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8));
    private static readonly Brush DimStroke = new SolidColorBrush(Color.FromArgb(0x99, 0x1D, 0x4E, 0xD8));

    static WaterHeadOverlayBuilder()
    {
        SelectedStroke.Freeze();
        NormalStroke.Freeze();
        DimStroke.Freeze();
    }

    public static LineLoadOverlayDisplay? Build(
        WaterHeadModel head,
        GeometryDefinitionViewModel geometry,
        bool awaitingEnd,
        bool isSelected = false)
    {
        if (head is not { IsEnabled: true })
            return null;
        if (!head.HasPlacement && !awaitingEnd)
            return null;

        var startCanvas = geometry.ConvertAxisToCanvas(new Point(head.StartX, head.StartY));
        Point endCanvas;
        if (head.PlacementType == WaterHeadPlacementType.Point)
        {
            endCanvas = new Point(startCanvas.X + 8, startCanvas.Y);
        }
        else if (awaitingEnd && !head.HasPlacement)
        {
            endCanvas = new Point(startCanvas.X + 12, startCanvas.Y);
        }
        else
        {
            endCanvas = geometry.ConvertAxisToCanvas(new Point(head.EndX, head.EndY));
            if (Math.Abs(endCanvas.X - startCanvas.X) < 0.5
                && Math.Abs(endCanvas.Y - startCanvas.Y) < 0.5)
                endCanvas = new Point(startCanvas.X + 12, startCanvas.Y);
        }

        var edge = Densify(startCanvas, endCanvas, 8);
        double midX = (startCanvas.X + endCanvas.X) * 0.5;
        double midY = (startCanvas.Y + endCanvas.Y) * 0.5;
        string label = string.Create(CultureInfo.InvariantCulture, $"H={head.Head:G5} ({head.RoleLabel})");

        return new LineLoadOverlayDisplay
        {
            EdgePoints = edge,
            BracketStart = startCanvas,
            BracketEnd = endCanvas,
            Stroke = isSelected ? SelectedStroke : NormalStroke,
            StrokeThickness = isSelected ? 4.5 : 3.0,
            Arrows = Array.Empty<LineLoadArrowDisplay>(),
            Labels = new List<LineLoadLabelDisplay>
            {
                new()
                {
                    X = midX,
                    Y = midY,
                    OffsetX = 6,
                    OffsetY = -10,
                    Text = label,
                    FontSize = isSelected ? 11 : 10
                }
            }
        };
    }

    /// <summary>Dim overlay for non-selected placed heads when another row is selected.</summary>
    public static LineLoadOverlayDisplay? BuildDimmed(
        WaterHeadModel head,
        GeometryDefinitionViewModel geometry)
    {
        var overlay = Build(head, geometry, awaitingEnd: false, isSelected: false);
        if (overlay == null) return null;
        return new LineLoadOverlayDisplay
        {
            EdgePoints = overlay.EdgePoints,
            BracketStart = overlay.BracketStart,
            BracketEnd = overlay.BracketEnd,
            Stroke = DimStroke,
            StrokeThickness = 2.0,
            Arrows = overlay.Arrows,
            Labels = overlay.Labels
        };
    }

    private static PointCollection Densify(Point a, Point b, int segments)
    {
        segments = Math.Max(1, segments);
        var pc = new PointCollection(segments + 1);
        for (int i = 0; i <= segments; i++)
        {
            double t = (double)i / segments;
            pc.Add(new Point(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y)));
        }
        return pc;
    }
}
