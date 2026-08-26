using CommunityToolkit.Mvvm.ComponentModel;

namespace GeotechSlopeApp.Models;

/// <summary>Checkbox row for multi-surface union in the boolean / Excavate dialog.</summary>
public partial class SurfaceRegionCheckItem : ObservableObject
{
    public SurfaceRegionPickItem Pick { get; }

    public string DisplayName => Pick.RegionName;

    public GeometrySurface Surface => Pick.Surface;

    public SurfaceRegionCheckItem(SurfaceRegionPickItem pick, bool isSelected = false)
    {
        Pick = pick;
        _isSelected = isSelected;
    }

    [ObservableProperty]
    private bool _isSelected;
}
