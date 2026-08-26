using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GeotechSlopeApp.Models;

/// <summary>One point on a Static water-pressure grid (axis X/Y plus scalar value).</summary>
public class WaterPressureGridPoint : INotifyPropertyChanged
{
    private int _index;
    private double _x;
    private double _y;
    private double _value;

    public WaterPressureGridPoint(int index, double x = 0, double y = 0, double value = 0)
    {
        _index = index;
        _x = x;
        _y = y;
        _value = value;
    }

    public int Index
    {
        get => _index;
        set { if (_index == value) return; _index = value; OnPropertyChanged(); }
    }

    public double X
    {
        get => _x;
        set { if (Math.Abs(_x - value) < 1e-15) return; _x = value; OnPropertyChanged(); }
    }

    public double Y
    {
        get => _y;
        set { if (Math.Abs(_y - value) < 1e-15) return; _y = value; OnPropertyChanged(); }
    }

    public double Value
    {
        get => _value;
        set { if (Math.Abs(_value - value) < 1e-15) return; _value = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
