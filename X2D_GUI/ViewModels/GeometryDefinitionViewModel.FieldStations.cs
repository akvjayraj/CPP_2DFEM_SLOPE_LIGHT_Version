using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Models.FieldStations;
using GeotechSlopeApp.Services.FieldStations;

namespace GeotechSlopeApp.ViewModels;

public partial class GeometryDefinitionViewModel
{
    private FieldStationSet? _fieldStationOverlaySource;
    private string? _selectedFieldStationId;
    private bool _showFieldStationOverlays;
    private bool _showBoreholesOnGeometryCanvas;
    private bool _showBoreholesOnMaterialCanvas;
    private IReadOnlyList<MaterialModel>? _fieldStationMaterials;
    private double _fieldStationYOffset;

    public ObservableCollection<FieldStationOverlayItem> FieldStationOverlays { get; } = new();

    /// <summary>Toolbar toggle: borehole columns on the Geometry Definition canvas only.</summary>
    public bool ShowBoreholesOnGeometryCanvas
    {
        get => _showBoreholesOnGeometryCanvas;
        set
        {
            if (_showBoreholesOnGeometryCanvas == value) return;
            _showBoreholesOnGeometryCanvas = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Toolbar toggle: borehole columns on the Material Properties canvas only.</summary>
    public bool ShowBoreholesOnMaterialCanvas
    {
        get => _showBoreholesOnMaterialCanvas;
        set
        {
            if (_showBoreholesOnMaterialCanvas == value) return;
            _showBoreholesOnMaterialCanvas = value;
            OnPropertyChanged();
        }
    }

    public void SetFieldStationOverlaySource(
        FieldStationSet? set,
        string? selectedStationId,
        bool visible,
        IReadOnlyList<MaterialModel>? materials,
        double yOffset = 0)
    {
        _fieldStationOverlaySource = set;
        _selectedFieldStationId = selectedStationId;
        _showFieldStationOverlays = visible || (set?.Stations.Count ?? 0) > 0;
        _fieldStationMaterials = materials;
        _fieldStationYOffset = yOffset;
        RefreshFieldStationOverlays();
    }

    public void RefreshFieldStationOverlays()
    {
        FieldStationOverlays.Clear();
        if (!_showFieldStationOverlays
            || _fieldStationOverlaySource == null
            || _fieldStationOverlaySource.Stations.Count == 0)
        {
            OnPropertyChanged(nameof(FieldStationOverlays));
            return;
        }

        foreach (var station in _fieldStationOverlaySource.Stations)
        {
            var collarY = station.CollarY;
            if (collarY == 0 && station.Intervals.Count > 0)
                collarY = station.Intervals.Max(i => i.TopY);
            collarY = FieldStationSoilBlock.ToModelY(collarY, _fieldStationYOffset);

            var pin = ConvertAxisToCanvas(new Point(station.X, collarY));
            var selected = string.Equals(station.Id, _selectedFieldStationId, StringComparison.OrdinalIgnoreCase);
            var pinSize = selected ? 12.0 : 10.0;
            var stripWidth = selected ? 16.0 : 10.0;
            var deepestY = station.Intervals.Count == 0
                ? collarY
                : FieldStationSoilBlock.ToModelY(station.Intervals.Min(i => i.BottomY), _fieldStationYOffset);
            var toe = ConvertAxisToCanvas(new Point(station.X, deepestY));
            var item = new FieldStationOverlayItem
            {
                StationId = station.Id,
                Label = string.IsNullOrWhiteSpace(station.Label) ? station.Id : station.Label,
                PinLeft = pin.X - pinSize / 2,
                PinTop = pin.Y - pinSize / 2,
                PinSize = pinSize,
                IsSelected = selected,
                PinFill = selected
                    ? CreateFrozenBrush(0x1D, 0x4E, 0xD8)
                    : CreateFrozenBrush(0x0F, 0x76, 0x6E),
                StemX1 = pin.X,
                StemY1 = pin.Y,
                StemX2 = toe.X,
                StemY2 = toe.Y,
                ShowStem = station.Intervals.Count > 0
            };

            foreach (var interval in station.Intervals)
            {
                var top = ConvertAxisToCanvas(new Point(station.X, FieldStationSoilBlock.ToModelY(interval.TopY, _fieldStationYOffset)));
                var bottom = ConvertAxisToCanvas(new Point(station.X, FieldStationSoilBlock.ToModelY(interval.BottomY, _fieldStationYOffset)));
                var y0 = Math.Min(top.Y, bottom.Y);
                var height = Math.Abs(bottom.Y - top.Y);
                if (height < 1) height = 1;
                item.Bands.Add(new FieldStationColumnBandOverlayItem
                {
                    Left = pin.X - stripWidth / 2,
                    Top = y0,
                    Width = stripWidth,
                    Height = height,
                    Fill = ResolveUnitFill(interval.UnitCode),
                    UnitCode = interval.UnitCode,
                    StrokeThickness = selected ? 1.2 : 0.6,
                    ShowUnitLabel = selected && height >= 12
                });
            }

            FieldStationOverlays.Add(item);
        }

        OnPropertyChanged(nameof(FieldStationOverlays));
    }

    private Brush ResolveUnitFill(string unitCode)
    {
        var row = _fieldStationOverlaySource?.RowForUnit(unitCode);
        if (row != null && !FieldStationUnitColours.IsUnset(row.Colour))
        {
            var c = row.Colour;
            var brush = new SolidColorBrush(Color.FromArgb(180, c.R, c.G, c.B));
            brush.Freeze();
            return brush;
        }

        return CreateFrozenBrush(0x94, 0xA3, 0xB8);
    }

    public bool HasClosedSoilPolygon =>
        GeometryVertexRows.Count >= 3 && !IsOpenPolygonBoundaryMode;

    /// <summary>
    /// Replaces the outer vertex ring with a closed rectangle. Clears horizontal layer cuts.
    /// Non-horizontal polylines are kept unless <paramref name="clearNonHorizontalCuts"/> is true.
    /// </summary>
    public void CreateClosedSoilBlock(
        double minX,
        double minY,
        double maxX,
        double maxY,
        bool clearNonHorizontalCuts = false,
        bool recordUndo = true)
    {
        if (maxX < minX)
            (minX, maxX) = (maxX, minX);
        if (maxY < minY)
            (minY, maxY) = (maxY, minY);
        if (maxX - minX < 1e-9 || maxY - minY < 1e-9)
            return;

        if (recordUndo)
            RecordUndo("Create soil block from field stations");

        IsOpenPolygonBoundaryMode = false;
        GeometryVertexRows.Clear();
        GeometryLineSegments.Clear();
        GeometryInternalEdges.Clear();
        GeometryInternalCircles.Clear();
        ExcavationOperations.Clear();
        _selectedExcavationOperation = null;
        OnPropertyChanged(nameof(SelectedExcavationOperation));
        ClearSurfaceBooleanCanvasResult();
        _hasAddedElevationLayer = false;
        _materialLayerCutElevations.Clear();
        if (clearNonHorizontalCuts)
            ClearNonHorizontalLayerCuts();
        _layerMaterialAssignmentByDisplayNumber.Clear();
        _nextStablePointId = 1;

        var corners = new[]
        {
            (minX, minY),
            (maxX, minY),
            (maxX, maxY),
            (minX, maxY)
        };
        foreach (var (x, y) in corners)
        {
            var display = new GeometryVertexDisplay
            {
                Index = GeometryVertexRows.Count,
                AxisX = x,
                AxisY = y
            };
            display.StablePointId = AllocateStablePointId();
            display.Owner = this;
            GeometryVertexRows.Add(display);
        }

        RebuildGeometryAfterProjectLoad();
        LockImportedBoundaryAsSurfaceS1();
        FitAxisBoundsToVertexExtents();
        _projectSettings?.PullAxisFromGeometry();
        RefreshFieldStationOverlays();
    }

    /// <summary>
    /// Single undo point: replace horizontal layer cuts and bottom→top material assignments from Field Stations.
    /// </summary>
    public void CommitFieldStationHorizontalCuts(
        IReadOnlyList<double> cutYs,
        IReadOnlyList<(int DisplayLayer, int MaterialId)> assignments,
        string undoDescription)
    {
        RecordUndo(string.IsNullOrWhiteSpace(undoDescription)
            ? "Commit field stations to layer cuts"
            : undoDescription);
        ApplyInferredMaterialLayerCuts(cutYs);
        ClearLayerMaterialAssignments();
        foreach (var (displayLayer, materialId) in assignments)
            SetLayerMaterialAssignmentMaterialId(displayLayer, materialId);
        OnPropertyChanged(nameof(MaterialLayerCutElevations));
    }

    public IReadOnlyList<Point> CollectBoreholeContactSnapPoints() =>
        _fieldStationOverlaySource == null
            ? Array.Empty<Point>()
            : FieldStationSoilBlock.CollectContactSnapPoints(_fieldStationOverlaySource.Stations, _fieldStationYOffset);
}
