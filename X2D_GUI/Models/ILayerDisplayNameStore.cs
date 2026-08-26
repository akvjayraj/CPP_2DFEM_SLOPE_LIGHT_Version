namespace GeotechSlopeApp.Models;

/// <summary>Per layering row display names (keyed by <see cref="LayerCoordinateSummary.DisplayLayerNumber"/>).</summary>
public interface ILayerDisplayNameStore
{
    string GetLayerDisplayName(int displayLayerNumber, string? segmentTagFallback);
    void SetLayerDisplayName(int displayLayerNumber, string value);
}
