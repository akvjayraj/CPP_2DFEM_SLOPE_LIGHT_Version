using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GeotechSlopeApp.Models;

public partial class GeometrySurface : ObservableObject
{
    public GeometrySurface(int surfaceNumber, string? surfaceName = null, IEnumerable<int>? connectedLineNumbers = null)
    {
        SurfaceNumber = surfaceNumber;
        SurfaceName = surfaceName ?? string.Empty;
        ConnectedLineNumbers = connectedLineNumbers != null ? new List<int>(connectedLineNumbers) : new List<int>();
        ConnectedLineNumbersText = ConnectedLineNumbers.Count > 0 ? string.Join(", ", ConnectedLineNumbers) : string.Empty;
        SurfaceDisplay = !string.IsNullOrEmpty(SurfaceName) ? $"Surface {surfaceNumber}: {SurfaceName}" : $"Surface {surfaceNumber}";
        PickedLineNumbers.CollectionChanged += OnPickedLineNumbersChanged;
    }

    private void OnPickedLineNumbersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(LineLabelsDisplay));
        OnPropertyChanged(nameof(PickedLineNumbers));
    }

    public int SurfaceNumber { get; }
    public string SurfaceNumberDisplay => $"S{SurfaceNumber}";
    public string SurfaceName { get; }
    public List<int> ConnectedLineNumbers { get; }
    public string ConnectedLineNumbersText { get; }
    public string SurfaceDisplay { get; }

    /// <summary>Line numbers picked for this surface's boundary (canvas-click or manual). Authoritative while unlocked; cleared on Add (lock).</summary>
    public ObservableCollection<int> PickedLineNumbers { get; } = new();

    /// <summary>Comma-separated "L#" display for the Line Segments grid column.</summary>
    public string LineLabelsDisplay
    {
        get
        {
            if (PickedLineNumbers.Count > 0)
                return string.Join(", ", PickedLineNumbers.Select(n => $"L{n}"));
            if (IsPolygonLocked && ConnectedLineNumbers.Count > 0)
                return string.Join(", ", ConnectedLineNumbers.Select(n => $"L{n}"));
            return "";
        }
    }

    [ObservableProperty]
    private string? _tag;

    /// <summary>-1 = automatic color from surface index (S1→slot 0, …); otherwise 0..paletteLength-1.</summary>
    [ObservableProperty]
    private int _colorIndex = -1;

    /// <summary>Id of the MaterialModel assigned to this surface region; null = unassigned.</summary>
    [ObservableProperty]
    private int? _assignedMaterialId;

    /// <summary>After <c>Add</c> commits the polygon, vertex order is fixed (tracks points by label; overlay follows current coordinates).</summary>
    [ObservableProperty]
    private bool _isPolygonLocked;

    /// <summary>Ordered vertex labels for an unlocked surface built by canvas clicks (before Add locks).</summary>
    public ObservableCollection<string> DraftPolygonVertexLabels { get; } = new();

    /// <summary>Committed closed polygon: ordered unique corner labels (no duplicate closing vertex).</summary>
    public ObservableCollection<string> LockedPolygonVertexLabels { get; } = new();

    partial void OnIsPolygonLockedChanged(bool value) => OnPropertyChanged(nameof(LineLabelsDisplay));
}
