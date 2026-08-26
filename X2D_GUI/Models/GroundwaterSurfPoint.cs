using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GeotechSlopeApp.Models;

/// <summary>One point on the phreatic surface polyline (axis coordinates, Y-up).</summary>
public class GroundwaterSurfPoint : INotifyPropertyChanged
{
    private int _index;
    private double _axisX;
    private double _axisY;

    public GroundwaterSurfPoint(int index, double axisX, double axisY)
    {
        _index = index;
        _axisX = axisX;
        _axisY = axisY;
    }

    public int Index
    {
        get => _index;
        set { _index = value; OnPropertyChanged(); }
    }

    public double AxisX
    {
        get => _axisX;
        set { _axisX = value; OnPropertyChanged(); OnPropertyChanged(nameof(Elevation)); }
    }

    public double AxisY
    {
        get => _axisY;
        set { _axisY = value; OnPropertyChanged(); OnPropertyChanged(nameof(Elevation)); }
    }

    public double Elevation => AxisY;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
