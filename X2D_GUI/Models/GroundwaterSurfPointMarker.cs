using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GeotechSlopeApp.Models;

/// <summary>Canvas marker for one phreatic-surface point (DataGrid row).</summary>
public class GroundwaterSurfPointMarker : INotifyPropertyChanged
{
    private int _index;
    private double _x;
    private double _y;
    private bool _isHighlighted;

    public GroundwaterSurfPointMarker(int index, double x, double y)
    {
        _index = index;
        _x = x;
        _y = y;
    }

    public int Index
    {
        get => _index;
        set { _index = value; OnPropertyChanged(); }
    }

    public double X
    {
        get => _x;
        set { _x = value; OnPropertyChanged(); }
    }

    public double Y
    {
        get => _y;
        set { _y = value; OnPropertyChanged(); }
    }

    public bool IsHighlighted
    {
        get => _isHighlighted;
        set { _isHighlighted = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
