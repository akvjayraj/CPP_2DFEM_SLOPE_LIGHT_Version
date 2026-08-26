using System.Windows;
using System.Windows.Media;

namespace GeotechSlopeApp.Models;

/// <summary>
/// One user-defined closed surface region for canvas overlay (named polygon + fill), similar to region tools in FE preprocessors.
/// </summary>
public sealed class SurfaceRegionOverlayItem
{
    public PointCollection PolygonPoints { get; init; } = new();
    /// <summary>When set (e.g. surface boolean A−B), canvas draws this geometry instead of <see cref="PolygonPoints"/>.</summary>
    public System.Windows.Media.Geometry? FillGeometry { get; init; }
    public bool HasFillGeometry => FillGeometry != null;
    public Brush Fill { get; init; } = Brushes.Transparent;
    /// <summary>Tag if set; otherwise e.g. S1.</summary>
    public string DisplayName { get; init; } = "";
    public double LabelLeft { get; init; }
    public double LabelTop { get; init; }
    public bool ShowLabel { get; init; }
    public bool IsSelected { get; init; }
}
