using System.Windows.Media;

namespace GeotechSlopeApp.Models;

/// <summary>Canvas-space polyline for a committed geological feature (weak plane / bedding / tension crack).</summary>
public sealed class GeologicalFeatureOverlayItem
{
    public PointCollection Points { get; init; } = new();
    public Brush Stroke { get; init; } = Brushes.DarkOrange;
    public string Name { get; init; } = "";
    public double LabelLeft { get; init; }
    public double LabelTop { get; init; }
    public bool ShowLabel => !string.IsNullOrWhiteSpace(Name) && Points.Count > 0;
}

/// <summary>Vertex marker while a geological polyline is being drawn.</summary>
public sealed class GeologicalDraftMarker
{
    public double X { get; init; }
    public double Y { get; init; }
}
