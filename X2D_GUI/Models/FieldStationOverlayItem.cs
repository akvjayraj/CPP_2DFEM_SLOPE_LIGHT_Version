using System.Collections.ObjectModel;
using System.Windows.Media;

namespace GeotechSlopeApp.Models;

/// <summary>Canvas pin, stem, and unit-stack column for a field station borehole.</summary>
public sealed class FieldStationOverlayItem
{
    public string StationId { get; init; } = "";
    public string Label { get; init; } = "";
    public double PinLeft { get; init; }
    public double PinTop { get; init; }
    public double PinSize { get; init; } = 10;
    public bool IsSelected { get; init; }
    public Brush PinFill { get; init; } = Brushes.SteelBlue;
    public double StemX1 { get; init; }
    public double StemY1 { get; init; }
    public double StemX2 { get; init; }
    public double StemY2 { get; init; }
    public bool ShowStem { get; init; }
    public ObservableCollection<FieldStationColumnBandOverlayItem> Bands { get; init; } = new();
}

/// <summary>One unit rectangle in a borehole column strip.</summary>
public sealed class FieldStationColumnBandOverlayItem
{
    public double Left { get; init; }
    public double Top { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public Brush Fill { get; init; } = Brushes.LightGray;
    public string UnitCode { get; init; } = "";
    public double StrokeThickness { get; init; } = 0.6;
    public bool ShowUnitLabel { get; init; }
}
