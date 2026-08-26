using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GeotechSlopeApp.Models;

/// <summary>Named Static water-pressure grid (points + type).</summary>
public class WaterPressureGrid : INotifyPropertyChanged
{
    private string _name;
    private WaterPressureGridType _gridType;

    public WaterPressureGrid(string name, WaterPressureGridType gridType)
    {
        _name = name;
        _gridType = gridType;
    }

    public string Name
    {
        get => _name;
        set
        {
            var next = value ?? "";
            if (_name == next) return;
            _name = next;
            OnPropertyChanged();
        }
    }

    public WaterPressureGridType GridType
    {
        get => _gridType;
        set
        {
            if (_gridType == value) return;
            _gridType = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ValueColumnHeader));
            OnPropertyChanged(nameof(GridTypeDisplayName));
        }
    }

    public string GridTypeDisplayName => GridType.GetDisplayName();

    public string ValueColumnHeader => GridType.GetValueColumnHeader();

    public ObservableCollection<WaterPressureGridPoint> Points { get; } = new();

    public WaterPressureGrid Clone()
    {
        var copy = new WaterPressureGrid(Name, GridType);
        for (var i = 0; i < Points.Count; i++)
        {
            var p = Points[i];
            copy.Points.Add(new WaterPressureGridPoint(p.Index, p.X, p.Y, p.Value));
        }
        return copy;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Serializable snapshot of a named water-pressure grid for .xfem / BuildModel.</summary>
public class WaterPressureGridData
{
    public string Name { get; set; } = "Default Grid";
    public WaterPressureGridType GridType { get; set; }
    public List<(double X, double Y, double Value)> Points { get; set; } = new();
}
