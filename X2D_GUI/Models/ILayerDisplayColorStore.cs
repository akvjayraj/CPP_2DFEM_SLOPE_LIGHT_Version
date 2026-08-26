using System.Windows.Media;

namespace GeotechSlopeApp.Models;

/// <summary>Per-layer tint for Layering Controls and canvas strip highlight.</summary>
public interface ILayerDisplayColorStore
{
    Color GetLayerDisplayColor(int displayLayerNumber);
    void SetLayerDisplayColor(int displayLayerNumber, Color value);
}
