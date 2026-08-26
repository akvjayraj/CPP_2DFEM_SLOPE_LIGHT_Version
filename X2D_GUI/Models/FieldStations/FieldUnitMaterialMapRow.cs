using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace GeotechSlopeApp.Models.FieldStations;

/// <summary>Maps a log unit code onto a project material library id (null = unmapped).</summary>
public sealed class FieldUnitMaterialMapRow : INotifyPropertyChanged
{
    private string _unitCode = "";
    private int? _materialId;
    private Color _colour;

    public string UnitCode { get => _unitCode; set => Set(ref _unitCode, value ?? ""); }
    public int? MaterialId
    {
        get => _materialId;
        set => Set(ref _materialId, value is 0 ? null : value);
    }

    public Color Colour
    {
        get => _colour;
        set => Set(ref _colour, value);
    }

    public SolidColorBrush ColourBrush => new(Colour);

    public bool IsMapped => MaterialId is > 0;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name == nameof(MaterialId))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMapped)));
        if (name == nameof(Colour))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ColourBrush)));
    }
}
