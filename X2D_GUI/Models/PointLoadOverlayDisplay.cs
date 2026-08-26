namespace GeotechSlopeApp.Models;

/// <summary>RS2D-style point load graphics for one <see cref="LoadModel"/> row.</summary>
public sealed class PointLoadOverlayDisplay
{
    public LineLoadArrowDisplay Arrow { get; init; } = new();
    public LineLoadLabelDisplay Label { get; init; } = new();
}
