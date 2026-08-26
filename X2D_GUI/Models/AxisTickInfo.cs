using CommunityToolkit.Mvvm.ComponentModel;

namespace GeotechSlopeApp.Models;

public partial class AxisTickInfo : ObservableObject
{
    [ObservableProperty]
    private double x1;

    [ObservableProperty]
    private double y1;

    [ObservableProperty]
    private double x2;

    [ObservableProperty]
    private double y2;

    [ObservableProperty]
    private double labelX;

    [ObservableProperty]
    private double labelY;

    [ObservableProperty]
    private string label = string.Empty;
}
