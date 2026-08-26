using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace GeotechSlopeApp.Models;

/// <summary>RS2D-style line load graphics for one <see cref="LoadModel"/> row.</summary>
public sealed class LineLoadOverlayDisplay
{
    public PointCollection EdgePoints { get; init; } = new();
    public Point BracketStart { get; init; }
    public Point BracketEnd { get; init; }
    public IReadOnlyList<LineLoadArrowDisplay> Arrows { get; init; } = new List<LineLoadArrowDisplay>();
    public IReadOnlyList<LineLoadLabelDisplay> Labels { get; init; } = new List<LineLoadLabelDisplay>();

    /// <summary>Optional stroke for water-head overlays; line loads ignore this and use XAML defaults.</summary>
    public Brush? Stroke { get; init; }

    /// <summary>Optional thickness for water-head overlays.</summary>
    public double StrokeThickness { get; init; } = 3.0;
}

/// <summary>Magnitude label for a distributed line load (start/end or single uniform value).</summary>
public sealed class LineLoadLabelDisplay
{
    public double X { get; set; }
    public double Y { get; set; }
    public double OffsetX { get; init; }
    public double OffsetY { get; init; }
    public string Text { get; init; } = "";
    public double FontSize { get; init; } = 10;
}

/// <summary>One arrow along a distributed line load.</summary>
public sealed class LineLoadArrowDisplay
{
    public double TailX { get; init; }
    public double TailY { get; init; }
    public double HeadX { get; init; }
    public double HeadY { get; init; }
    public double HeadLeftX { get; init; }
    public double HeadLeftY { get; init; }
    public double HeadRightX { get; init; }
    public double HeadRightY { get; init; }
    public PointCollection ArrowHeadPoints { get; init; } = new();
}
