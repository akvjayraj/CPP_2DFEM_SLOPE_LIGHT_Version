using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GeotechSlopeApp.Models;

public class TunnelExcavationCircleRow : INotifyPropertyChanged
{
    private bool _isHighlighted;
    private int? _stage;

    public int CircleNumber { get; set; }
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double Radius { get; set; }
    public string UnitLabel { get; set; } = string.Empty;
    public TunnelExcavationCircle? Circle { get; set; }

    public int? Stage
    {
        get => _stage ?? Circle?.StageActivation;
        set
        {
            if (_stage != value)
            {
                _stage = value;
                if (Circle != null)
                    Circle.StageActivation = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsHighlighted
    {
        get => _isHighlighted;
        set { if (_isHighlighted != value) { _isHighlighted = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
