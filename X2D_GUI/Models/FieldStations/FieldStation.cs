using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GeotechSlopeApp.Models.FieldStations;

/// <summary>Evidence pin on the 2D section: model X plus a stacked unit log.</summary>
public sealed class FieldStation : INotifyPropertyChanged
{
    private string _id = "";
    private double _x;
    private double _collarY;
    private string _label = "";

    public string Id { get => _id; set => Set(ref _id, value ?? ""); }
    public double X { get => _x; set => Set(ref _x, value); }
    public double CollarY { get => _collarY; set => Set(ref _collarY, value); }
    public string Label { get => _label; set => Set(ref _label, value ?? ""); }

    public ObservableCollection<FieldUnitInterval> Intervals { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
