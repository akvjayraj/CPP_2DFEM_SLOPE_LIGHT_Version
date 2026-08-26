using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace GeotechSlopeApp.Models;

/// <summary>
/// Summary for a layer, populated from horizontal strips (Material Properties) or from boundary segments (legacy row).
/// <see cref="LayerNumber"/> is the representative geometry line segment for highlight and material sync.
/// </summary>
public class LayerCoordinateSummary : INotifyPropertyChanged
{
    private readonly GeometryLineSegment? _segment;
    private readonly string? _fromPoint;
    private readonly string? _toPoint;
    private readonly string[]? _cornerNames;
    private readonly ILayerDisplayNameStore? _layerNames;
    private readonly ILayerDisplayColorStore? _layerColors;
    private string _layerName = "";

    /// <summary>Legacy: one row per boundary segment (P1–P2 only; remaining cells empty).</summary>
    public LayerCoordinateSummary(int layerNumber, string layerHeader, string fromPoint, string toPoint, GeometryLineSegment? segment = null, ILayerDisplayNameStore? layerNames = null, ILayerDisplayColorStore? layerColors = null)
    {
        DisplayLayerNumber = layerNumber;
        LayerNumber = layerNumber;
        _segment = segment;
        _fromPoint = fromPoint;
        _toPoint = toPoint;
        _cornerNames = null;
        _layerNames = layerNames;
        _layerColors = layerColors ?? layerNames as ILayerDisplayColorStore;
        _layerName = ResolveInitialLayerName(layerNumber, layerHeader, segment);
        if (_layerNames == null)
            AttachSegmentTagSync();
    }

    /// <summary>Horizontal material layer: corners P1..P8; <paramref name="representativeLineNumber"/> ties to a boundary segment for material/highlight.</summary>
    public LayerCoordinateSummary(int displayLayerNumber, int representativeLineNumber, string[] cornerNamesUpTo8, GeometryLineSegment? segment, ILayerDisplayNameStore? layerNames, ILayerDisplayColorStore? layerColors = null)
    {
        DisplayLayerNumber = displayLayerNumber;
        LayerNumber = representativeLineNumber;
        _segment = segment;
        _cornerNames = new string[8];
        for (var i = 0; i < 8; i++)
            _cornerNames[i] = i < cornerNamesUpTo8.Length ? (cornerNamesUpTo8[i] ?? "") : "";
        _layerNames = layerNames;
        _layerColors = layerColors ?? layerNames as ILayerDisplayColorStore;
        _layerName = ResolveInitialLayerName(displayLayerNumber, null, segment);
        if (_layerNames == null)
            AttachSegmentTagSync();
    }

    private string ResolveInitialLayerName(int displayLayerNumber, string? layerHeader, GeometryLineSegment? segment)
    {
        if (_layerNames != null)
            return _layerNames.GetLayerDisplayName(displayLayerNumber, segment?.Tag ?? layerHeader);
        if (!string.IsNullOrWhiteSpace(segment?.Tag))
            return segment!.Tag!.Trim();
        if (!string.IsNullOrWhiteSpace(layerHeader))
            return layerHeader.Trim();
        return $"Layer {displayLayerNumber}";
    }

    private void AttachSegmentTagSync()
    {
        if (_segment != null && string.IsNullOrWhiteSpace(_segment.Tag))
            _segment.Tag = _layerName;
        if (_segment == null) return;
        _segment.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(GeometryLineSegment.Tag)) return;
            var tag = _segment.Tag?.Trim() ?? "";
            var next = string.IsNullOrEmpty(tag) ? $"Layer {DisplayLayerNumber}" : tag;
            if (_layerName == next) return;
            _layerName = next;
            OnPropertyChanged(nameof(LayerName));
            OnPropertyChanged(nameof(LayerHeader));
            OnPropertyChanged(nameof(LayerPickerDisplay));
        };
    }

    /// <summary>1-based index from bottom of model (Layer 1, 2, …) for horizontal strips; line number for legacy segment rows.</summary>
    public int DisplayLayerNumber { get; }

    /// <summary>Representative <see cref="GeometryLineSegment.LineNumber"/> for canvas highlight and material properties.</summary>
    public int LayerNumber { get; }

    /// <summary>
    /// User-editable name per grid row. When <see cref="ILayerDisplayNameStore"/> is provided, names are stored independently
    /// (multiple rows may share one representative segment without overwriting each other).
    /// </summary>
    public string LayerName
    {
        get => _layerNames != null
            ? _layerNames.GetLayerDisplayName(DisplayLayerNumber, _segment?.Tag)
            : _layerName;
        set
        {
            var v = value?.Trim() ?? "";
            if (string.IsNullOrEmpty(v))
                v = $"Layer {DisplayLayerNumber}";
            if (_layerNames != null)
            {
                if (string.Equals(_layerNames.GetLayerDisplayName(DisplayLayerNumber, _segment?.Tag), v, StringComparison.Ordinal))
                    return;
                _layerNames.SetLayerDisplayName(DisplayLayerNumber, v);
                _layerName = v;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LayerHeader));
                OnPropertyChanged(nameof(LayerPickerDisplay));
                return;
            }

            if (_layerName == v) return;
            _layerName = v;
            if (_segment != null)
                _segment.Tag = v;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LayerHeader));
            OnPropertyChanged(nameof(LayerPickerDisplay));
        }
    }

    /// <summary>Assign Material dialog and legacy bindings.</summary>
    public string LayerPickerDisplay => $"Layer {DisplayLayerNumber} — {LayerName}";

    /// <summary>Same as <see cref="LayerName"/> (kept for existing XAML).</summary>
    public string LayerHeader
    {
        get => LayerName;
        set => LayerName = value;
    }

    /// <summary>Layer tint from <see cref="ILayerDisplayColorStore"/> (palette + per-row overrides).</summary>
    public Color LayerColor
    {
        get => _layerColors?.GetLayerDisplayColor(DisplayLayerNumber) ?? Colors.SlateGray;
        set
        {
            if (_layerColors == null) return;
            var cur = _layerColors.GetLayerDisplayColor(DisplayLayerNumber);
            if (cur == value) return;
            _layerColors.SetLayerDisplayColor(DisplayLayerNumber, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(LayerColorBrush));
        }
    }

    public Brush LayerColorBrush => new SolidColorBrush(LayerColor);

    /// <summary>Called when <see cref="ILayerDisplayColorStore"/> updates this row's tint externally.</summary>
    internal void NotifyLayerColorChangedFromStore()
    {
        OnPropertyChanged(nameof(LayerColor));
        OnPropertyChanged(nameof(LayerColorBrush));
    }

    public string FromPoint => _cornerNames != null ? Corner(0) : (_fromPoint ?? "");
    public string ToPoint => _cornerNames != null ? Corner(1) : (_toPoint ?? "");

    public string Point1Name => Corner(0);
    public string Point2Name => Corner(1);
    public string Point3Name => Corner(2);
    public string Point4Name => Corner(3);
    public string Point5Name => Corner(4);
    public string Point6Name => Corner(5);
    public string Point7Name => Corner(6);
    public string Point8Name => Corner(7);

    private string Corner(int i)
    {
        if (_cornerNames != null)
            return _cornerNames[i] ?? "";
        if (i == 0) return _fromPoint ?? "";
        if (i == 1) return _toPoint ?? "";
        return "";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
