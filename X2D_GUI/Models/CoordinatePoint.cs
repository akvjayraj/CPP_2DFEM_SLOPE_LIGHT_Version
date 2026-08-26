using CommunityToolkit.Mvvm.ComponentModel;

namespace GeotechSlopeApp.Models;

public partial class CoordinatePoint : ObservableObject
{
    [ObservableProperty]
    private double x;

    [ObservableProperty]
    private double y;

    [ObservableProperty]
    private bool isHighlighted;
}
