using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GeotechSlopeApp.Models.FieldStations;

/// <summary>One soil unit interval in a field-station log (elevations, not depths).</summary>
public sealed class FieldUnitInterval : INotifyPropertyChanged
{
    private double _topY;
    private double _bottomY;
    private string _unitCode = "";
    private string _note = "";

    public double TopY { get => _topY; set => Set(ref _topY, value); }
    public double BottomY { get => _bottomY; set => Set(ref _bottomY, value); }
    public string UnitCode { get => _unitCode; set => Set(ref _unitCode, value ?? ""); }
    public string Note { get => _note; set => Set(ref _note, value ?? ""); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
