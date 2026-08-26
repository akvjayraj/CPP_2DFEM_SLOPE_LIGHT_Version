namespace GeotechSlopeApp.Models;

/// <summary>ComboBox entry for picking a surface (S1, S2, …) in boolean region dialogs.</summary>
public sealed class SurfaceRegionPickItem
{
    public string RegionName { get; init; } = "";
    public GeometrySurface Surface { get; init; } = null!;
}
