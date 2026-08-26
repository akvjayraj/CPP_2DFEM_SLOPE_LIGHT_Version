using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.ViewModels.Workspace;

public enum GeometryBottomTableKind
{
    Points,
    Lines,
    Surfaces,
    Regions,
    Blocks
}

public sealed class RegionExplorerPayload
{
    public RegionExplorerPayload(GeometrySurface surface) =>
        Surface = surface ?? throw new ArgumentNullException(nameof(surface));

    public GeometrySurface Surface { get; }
}

public sealed class GeometryRegionTableRow
{
    public required GeometrySurface Surface { get; init; }
    public string RegionDisplay => $"R{Surface.SurfaceNumber}";
    public string Name => string.IsNullOrWhiteSpace(Surface.SurfaceName)
        ? $"Region {Surface.SurfaceNumber}"
        : Surface.SurfaceName;
    public string Tag => Surface.Tag ?? "";
    public string Material { get; init; } = "—";
    public string Area { get; init; } = "—";
    public string Color { get; init; } = "—";
}

public sealed class GeometryBlockTableRow
{
    public string Name { get; init; } = "Soil Block";
    public int Vertices { get; init; }
    public string Area { get; init; } = "—";
    public string Material { get; init; } = "—";
}

/// <summary>
/// Geometry-ribbon chrome: explorer tree, details totals, selected-entity properties.
/// Hosts existing GeometryDefinition collections; does not duplicate the canvas.
/// </summary>
public sealed class GeometryWorkspaceViewModel : INotifyPropertyChanged
{
    public const string GlyphPoint = "\uE707";
    public const string GlyphLine = "\uE8A1";
    public const string GlyphSurface = "\uE81F";
    public const string GlyphNewSurface = "\uE710";
    public const string GlyphRegion = "\uE8F1";
    public const string GlyphBlock = "\uE80A";
    public const string GlyphBoolean = "\uE8A6";
    public const string GlyphConstruction = "\uE713";
    public const string GlyphFolder = "\uE8B7";
    public const string GlyphVisible = "#FFFFFFFF";
    public const string GlyphChevronExpanded = "\uE70D";
    public const string GlyphChevronCollapsed = "\uE70E";

    public const string TablePoints = "table-points";
    public const string TableLines = "table-lines";
    public const string TableSurfaces = "table-surfaces";
    public const string TableRegions = "table-regions";
    public const string TableBlocks = "table-blocks";

    private readonly MainWindowViewModel _host;
    private ProjectExplorerNode? _selectedNode;
    private object? _selectedEntity;
    private GeometryRegionTableRow? _selectedRegionRow;
    private GeometryBlockTableRow? _selectedBlockRow;
    private GeometryBottomTableKind _bottomTableKind = GeometryBottomTableKind.Points;
    private bool _isExplorerExpanded = true;
    private bool _isDetailsExpanded = true;
    private bool _isPropertiesExpanded = true;
    private bool _isPointsTableExpanded = true;
    private bool _isViewOptionsExpanded = true;
    private bool _showPoints = true;
    private bool _showLines = true;
    private bool _showSurfaces = true;
    private bool _showRegions = true;
    private string _filterText = "";
    private bool _showLabels = true;
    private bool _showPointLabel = true;
    private double _labelSize = 10;
    private string _pointColor = "Blue";
    private string _pointSymbol = "Circle";
    private double _symbolSize = 8;

    public GeometryWorkspaceViewModel(MainWindowViewModel host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        Roots = new ObservableCollection<ProjectExplorerNode>();
        SelectNodeCommand = new RelayCommand<ProjectExplorerNode>(OnSelect);
        DeleteSelectedCommand = new RelayCommand(DeleteSelected);
        ToggleExplorerCommand = new RelayCommand(() => IsExplorerExpanded = !IsExplorerExpanded);
        ToggleDetailsCommand = new RelayCommand(() => IsDetailsExpanded = !IsDetailsExpanded);
        TogglePropertiesCommand = new RelayCommand(() => IsPropertiesExpanded = !IsPropertiesExpanded);
        TogglePointsTableCommand = new RelayCommand(() => IsPointsTableExpanded = !IsPointsTableExpanded);
        ToggleViewOptionsCommand = new RelayCommand(() => IsViewOptionsExpanded = !IsViewOptionsExpanded);
        Hook(_host.GeometryDefinitionVM.GeometryVertexRows);
        Hook(_host.GeometryDefinitionVM.GeometryLineSegments);
        Hook(_host.GeometryDefinitionVM.GeometrySurfaces);
        Hook(_host.GeometryDefinitionVM.GeologicalFeatures);
        _host.GeometryDefinitionVM.PropertyChanged += OnGeometryPropertyChanged;
        _host.ProjectSettingsVM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ProjectSettingsViewModel.Idealization)
                or nameof(ProjectSettingsViewModel.SrfAxisymmetric))
                OnPropertyChanged(nameof(StatusDimension));
        };
        Refresh();
    }

    public ObservableCollection<ProjectExplorerNode> Roots { get; }
    public ICommand SelectNodeCommand { get; }
    public ICommand DeleteSelectedCommand { get; }
    public ICommand ToggleExplorerCommand { get; }
    public ICommand ToggleDetailsCommand { get; }
    public ICommand TogglePropertiesCommand { get; }
    public ICommand TogglePointsTableCommand { get; }
    public ICommand ToggleViewOptionsCommand { get; }

    public bool IsExplorerExpanded
    {
        get => _isExplorerExpanded;
        set
        {
            if (_isExplorerExpanded == value) return;
            _isExplorerExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExplorerChevronGlyph));
            _host.NotifyWorkspaceChromeChanged();
        }
    }

    public bool IsDetailsExpanded
    {
        get => _isDetailsExpanded;
        set
        {
            if (_isDetailsExpanded == value) return;
            _isDetailsExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DetailsChevronGlyph));
        }
    }

    public bool IsPropertiesExpanded
    {
        get => _isPropertiesExpanded;
        set
        {
            if (_isPropertiesExpanded == value) return;
            _isPropertiesExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PropertiesChevronGlyph));
            _host.NotifyWorkspaceChromeChanged();
        }
    }

    public bool IsPointsTableExpanded
    {
        get => _isPointsTableExpanded;
        set
        {
            if (_isPointsTableExpanded == value) return;
            _isPointsTableExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PointsTableChevronGlyph));
            _host.NotifyWorkspaceChromeChanged();
        }
    }

    public bool IsViewOptionsExpanded
    {
        get => _isViewOptionsExpanded;
        set
        {
            if (_isViewOptionsExpanded == value) return;
            _isViewOptionsExpanded = value;
            OnPropertyChanged();
        }
    }

    public string ExplorerChevronGlyph => IsExplorerExpanded ? GlyphChevronExpanded : GlyphChevronCollapsed;
    public string DetailsChevronGlyph => IsDetailsExpanded ? GlyphChevronExpanded : GlyphChevronCollapsed;
    public string PropertiesChevronGlyph => IsPropertiesExpanded ? GlyphChevronExpanded : GlyphChevronCollapsed;
    public string PointsTableChevronGlyph => IsPointsTableExpanded ? GlyphChevronExpanded : GlyphChevronCollapsed;

    public GeometryBottomTableKind BottomTableKind
    {
        get => _bottomTableKind;
        private set
        {
            if (_bottomTableKind == value) return;
            _bottomTableKind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TableTitle));
            OnPropertyChanged(nameof(IsPointsTable));
            OnPropertyChanged(nameof(IsLinesTable));
            OnPropertyChanged(nameof(IsSurfacesTable));
            OnPropertyChanged(nameof(IsRegionsTable));
            OnPropertyChanged(nameof(IsBlocksTable));
        }
    }

    public string TableTitle => BottomTableKind switch
    {
        GeometryBottomTableKind.Lines => "Lines",
        GeometryBottomTableKind.Surfaces => "Surfaces",
        GeometryBottomTableKind.Regions => "Regions",
        GeometryBottomTableKind.Blocks => "Blocks",
        _ => "Points Table"
    };

    public bool IsPointsTable => BottomTableKind == GeometryBottomTableKind.Points;
    public bool IsLinesTable => BottomTableKind == GeometryBottomTableKind.Lines;
    public bool IsSurfacesTable => BottomTableKind == GeometryBottomTableKind.Surfaces;
    public bool IsRegionsTable => BottomTableKind == GeometryBottomTableKind.Regions;
    public bool IsBlocksTable => BottomTableKind == GeometryBottomTableKind.Blocks;

    public ObservableCollection<GeometryRegionTableRow> RegionRows { get; } = new();
    public ObservableCollection<GeometryBlockTableRow> BlockRows { get; } = new();

    public GeometryRegionTableRow? SelectedRegionRow
    {
        get => _selectedRegionRow;
        set
        {
            if (ReferenceEquals(_selectedRegionRow, value)) return;
            _selectedRegionRow = value;
            OnPropertyChanged();
            if (value == null) return;
            SelectedEntity = value.Surface;
            Geometry.HighlightExplorerSelection(value.Surface);
        }
    }

    public GeometryBlockTableRow? SelectedBlockRow
    {
        get => _selectedBlockRow;
        set
        {
            if (ReferenceEquals(_selectedBlockRow, value)) return;
            _selectedBlockRow = value;
            OnPropertyChanged();
            if (value == null) return;
            SelectedEntity = null;
            Geometry.HighlightExplorerSelection(GeometryDefinitionViewModel.SoilBlockExplorerPayload);
        }
    }

    public GeometryDefinitionViewModel Geometry => _host.GeometryDefinitionVM;

    public string FilterText
    {
        get => _filterText;
        set
        {
            var next = value ?? "";
            if (_filterText == next) return;
            _filterText = next;
            OnPropertyChanged();
            Refresh();
        }
    }

    public int PointCount => Geometry.GeometryVertexRows.Count;
    public int LineCount => Geometry.GeometryLineSegments.Count;
    public int SurfaceCount => Geometry.GeometrySurfaces.Count;
    public int RegionCount => Geometry.GeometrySurfaces.Count;
    public int BlockCount => Geometry.HasClosedSoilPolygon ? 1 : 0;
    public int BooleanCount => Geometry.GeometrySurfaces.Count(s =>
        !string.IsNullOrWhiteSpace(s.Tag) &&
        s.Tag.Contains("boolean", StringComparison.OrdinalIgnoreCase));
    public int ConstructionCount => Geometry.GeologicalFeatures.Count;

    public ProjectExplorerNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (ReferenceEquals(_selectedNode, value)) return;
            _selectedNode = value;
            OnPropertyChanged();
            if (value != null)
                OnSelect(value);
        }
    }

    public object? SelectedEntity
    {
        get => _selectedEntity;
        set
        {
            if (ReferenceEquals(_selectedEntity, value)) return;
            _selectedEntity = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedPoint));
            OnPropertyChanged(nameof(SelectedPointId));
            OnPropertyChanged(nameof(SelectedPointX));
            OnPropertyChanged(nameof(SelectedPointY));
            OnPropertyChanged(nameof(SelectedPointZ));
            OnPropertyChanged(nameof(SelectedPointDescription));
            OnPropertyChanged(nameof(SelectedPointType));
        }
    }

    public bool HasSelectedPoint => SelectedEntity != null && TryReadPoint(SelectedEntity, out _, out _, out _, out _);

    public string SelectedPointId => TryReadPoint(SelectedEntity, out var id, out _, out _, out _) ? id : "—";
    public string SelectedPointX => TryReadPoint(SelectedEntity, out _, out var x, out _, out _) ? x.ToString("0.000") : "—";
    public string SelectedPointY => TryReadPoint(SelectedEntity, out _, out _, out var y, out _) ? y.ToString("0.000") : "—";
    public string SelectedPointZ => "0.000";
    public string SelectedPointDescription => TryReadPoint(SelectedEntity, out _, out _, out _, out var label) ? label : "";
    public string SelectedPointType => "Control Point";

    public bool ShowPoints { get => _showPoints; set { if (_showPoints == value) return; _showPoints = value; OnPropertyChanged(); SyncCanvas(); } }
    public bool ShowLines { get => _showLines; set { if (_showLines == value) return; _showLines = value; OnPropertyChanged(); SyncCanvas(); } }
    public bool ShowSurfaces { get => _showSurfaces; set { if (_showSurfaces == value) return; _showSurfaces = value; OnPropertyChanged(); SyncCanvas(); } }
    public bool ShowRegions { get => _showRegions; set { if (_showRegions == value) return; _showRegions = value; OnPropertyChanged(); SyncCanvas(); } }
    public bool ShowLabels { get => _showLabels; set { if (_showLabels == value) return; _showLabels = value; OnPropertyChanged(); SyncCanvas(); } }
    public bool ShowGrid
    {
        get => Geometry.ShowGrid;
        set
        {
            if (Geometry.ShowGrid == value) return;
            Geometry.ShowGrid = value;
            OnPropertyChanged();
        }
    }

    public bool ShowAxes
    {
        get => Geometry.ShowAxes;
        set
        {
            if (Geometry.ShowAxes == value) return;
            Geometry.ShowAxes = value;
            OnPropertyChanged();
        }
    }
    public bool ShowSnap
    {
        get => Geometry.SnapSettings.SnapEnabled;
        set
        {
            if (Geometry.SnapSettings.SnapEnabled == value) return;
            Geometry.SnapSettings.SnapEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool ShowPointLabel { get => _showPointLabel; set { if (_showPointLabel == value) return; _showPointLabel = value; OnPropertyChanged(); } }
    public double LabelSize { get => _labelSize; set { if (Math.Abs(_labelSize - value) < 1e-9) return; _labelSize = value; OnPropertyChanged(); } }
    public string PointColor { get => _pointColor; set { if (_pointColor == value) return; _pointColor = value ?? "Blue"; OnPropertyChanged(); } }
    public string PointSymbol { get => _pointSymbol; set { if (_pointSymbol == value) return; _pointSymbol = value ?? "Circle"; OnPropertyChanged(); } }
    public double SymbolSize { get => _symbolSize; set { if (Math.Abs(_symbolSize - value) < 1e-9) return; _symbolSize = value; OnPropertyChanged(); } }

    public string CoordinateSystem => "Global";
    public string UnitsLabel =>
        $"{_host.ProjectSettingsVM.LengthUnitLabel}, {_host.ProjectSettingsVM.UnitWeightUnitLabel}, {_host.ProjectSettingsVM.StressUnitLabel}";
    public string AngleLabel => "Degrees";

    public string StatusReady => "Ready";
    public string StatusUnits => _host.ProjectSettingsVM.Project.Units ?? ProjectUnitSystem.Si;
    public string StatusAnalysis =>
        _host.ProjectSettingsVM.ResolvedAnalysisKind == AnalysisKind.Srf ? "SRF" : "Nonlinear";
    public string StatusDimension =>
        _host.ProjectSettingsVM.IsAxisymmetricIdealization ? "2D Axisymmetric" : "2D Plane Strain";
    public string StatusLastSaved
    {
        get
        {
            var path = _host.CurrentXfemProjectPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return "Last Saved: —";
            var t = File.GetLastWriteTime(path);
            return t.Date == DateTime.Today
                ? $"Last Saved: Today, {t:hh:mm tt}"
                : $"Last Saved: {t:yyyy-MM-dd hh:mm tt}";
        }
    }

    public string StatusCursor
    {
        get
        {
            var raw = Geometry.GeometryCanvasCoordinates;
            return string.IsNullOrWhiteSpace(raw) ? "X: —  Y: —" : raw;
        }
    }

    public void Refresh()
    {
        Roots.Clear();
        var root = new ProjectExplorerNode
        {
            Kind = ExplorerNodeKind.Geometry,
            Title = "Project Geometry",
            Glyph = GlyphFolder,
            TabIndex = 1,
            IsExpanded = true
        };

        var points = Folder("Points", GlyphPoint, PointCount, expanded: true, payload: TablePoints);
        foreach (var vertex in Geometry.GeometryVertexRows)
        {
            var title = PointTitle(vertex);
            if (!MatchesFilter(title)) continue;
            points.Children.Add(new ProjectExplorerNode
            {
                Kind = ExplorerNodeKind.FeatureGroup,
                Title = title,
                Glyph = GlyphPoint,
                TabIndex = 1,
                Payload = vertex
            });
        }
        root.Children.Add(points);

        var lines = Folder("Lines", GlyphLine, LineCount, payload: TableLines);
        foreach (var line in Geometry.GeometryLineSegments)
        {
            if (!MatchesFilter(line.LineNumberLabel) && !MatchesFilter(line.ConnectedPoints)) continue;
            lines.Children.Add(new ProjectExplorerNode
            {
                Kind = ExplorerNodeKind.FeatureGroup,
                Title = line.LineNumberLabel,
                Glyph = GlyphLine,
                TabIndex = 1,
                Payload = line
            });
        }
        root.Children.Add(lines);

        var surfaces = Folder("Surfaces", GlyphSurface, SurfaceCount, expanded: true, payload: TableSurfaces);
        if (MatchesFilter("New Surface"))
        {
            surfaces.Children.Add(new ProjectExplorerNode
            {
                Kind = ExplorerNodeKind.FeatureGroup,
                Title = "New Surface",
                Glyph = GlyphNewSurface,
                TabIndex = 1,
                Payload = GeometryDefinitionViewModel.NewSurfaceExplorerPayload
            });
        }
        foreach (var surface in Geometry.GeometrySurfaces)
        {
            var title = string.IsNullOrWhiteSpace(surface.SurfaceName)
                ? $"Surface {surface.SurfaceNumber}"
                : surface.SurfaceName;
            if (!MatchesFilter(title)) continue;
            surfaces.Children.Add(new ProjectExplorerNode
            {
                Kind = ExplorerNodeKind.FeatureGroup,
                Title = title,
                Glyph = GlyphSurface,
                TabIndex = 1,
                Payload = surface
            });
        }
        root.Children.Add(surfaces);

        var regions = Folder("Regions", GlyphRegion, RegionCount, expanded: true, payload: TableRegions);
        foreach (var surface in Geometry.GeometrySurfaces)
        {
            var title = string.IsNullOrWhiteSpace(surface.SurfaceName)
                ? $"Region {surface.SurfaceNumber}"
                : surface.SurfaceName;
            if (!MatchesFilter(title)) continue;
            regions.Children.Add(new ProjectExplorerNode
            {
                Kind = ExplorerNodeKind.FeatureGroup,
                Title = title,
                Glyph = GlyphRegion,
                TabIndex = 1,
                Payload = new RegionExplorerPayload(surface)
            });
        }
        root.Children.Add(regions);

        var blocks = Folder("Blocks", GlyphBlock, BlockCount, payload: TableBlocks);
        if (Geometry.HasClosedSoilPolygon && MatchesFilter("Soil Block"))
        {
            blocks.Children.Add(new ProjectExplorerNode
            {
                Kind = ExplorerNodeKind.FeatureGroup,
                Title = "Soil Block",
                Glyph = GlyphBlock,
                TabIndex = 1,
                Payload = GeometryDefinitionViewModel.SoilBlockExplorerPayload
            });
        }
        root.Children.Add(blocks);
        root.Children.Add(Folder("Boolean", GlyphBoolean, BooleanCount));
        var construction = Folder("Construction Geometry", GlyphConstruction, ConstructionCount);
        foreach (var feature in Geometry.GeologicalFeatures)
        {
            var title = string.IsNullOrWhiteSpace(feature.Name) ? feature.Kind.ToString() : feature.Name;
            if (!MatchesFilter(title)) continue;
            construction.Children.Add(new ProjectExplorerNode
            {
                Kind = ExplorerNodeKind.GeologicalFeature,
                Title = title,
                Glyph = GlyphConstruction,
                TabIndex = 1,
                Payload = feature
            });
        }
        root.Children.Add(construction);

        Roots.Add(root);
        RefreshTableRows();
        NotifyCounts();
    }

    private void OnSelect(ProjectExplorerNode? node)
    {
        if (node == null) return;
        _host.SelectedTabIndex = 1;
        BottomTableKind = ResolveTableKind(node);
        if (BottomTableKind != GeometryBottomTableKind.Regions)
            SelectedRegionRow = null;
        if (BottomTableKind != GeometryBottomTableKind.Blocks)
            SelectedBlockRow = null;

        if (node.Payload is string token &&
            string.Equals(token, GeometryDefinitionViewModel.NewSurfaceExplorerPayload, StringComparison.Ordinal) &&
            string.Equals(node.Title, "New Surface", StringComparison.Ordinal))
        {
            Geometry.BeginExplorerSurfaceDefinition();
            SelectedEntity = Geometry.SelectedGeometrySurface;
            if (Geometry.SelectedGeometrySurface != null)
                Geometry.HighlightExplorerSelection(Geometry.SelectedGeometrySurface);
            return;
        }

        if (node.Payload is RegionExplorerPayload region)
        {
            SelectedRegionRow = RegionRows.FirstOrDefault(r => ReferenceEquals(r.Surface, region.Surface));
            if (SelectedRegionRow == null)
            {
                SelectedEntity = region.Surface;
                Geometry.HighlightExplorerSelection(region.Surface);
            }
            return;
        }

        if (node.Payload is string soilBlock &&
            string.Equals(soilBlock, GeometryDefinitionViewModel.SoilBlockExplorerPayload, StringComparison.Ordinal))
        {
            SelectedBlockRow = BlockRows.FirstOrDefault();
            if (SelectedBlockRow == null)
                Geometry.HighlightExplorerSelection(soilBlock);
            return;
        }

        if (node.Payload != null && node.Payload is not string)
        {
            SelectedEntity = node.Payload;
            Geometry.HighlightExplorerSelection(node.Payload);
        }
        else
        {
            SelectedEntity = null;
        }
    }

    private static GeometryBottomTableKind ResolveTableKind(ProjectExplorerNode node)
    {
        if (node.Payload is GeometryLineSegment
            || node.Payload is string lines && lines == TableLines
            || string.Equals(node.Title, "Lines", StringComparison.Ordinal))
            return GeometryBottomTableKind.Lines;

        if (node.Payload is RegionExplorerPayload
            || node.Payload is string regions && regions == TableRegions
            || string.Equals(node.Title, "Regions", StringComparison.Ordinal))
            return GeometryBottomTableKind.Regions;

        if (node.Payload is string blocks &&
            (blocks == TableBlocks
             || string.Equals(blocks, GeometryDefinitionViewModel.SoilBlockExplorerPayload, StringComparison.Ordinal))
            || string.Equals(node.Title, "Blocks", StringComparison.Ordinal)
            || string.Equals(node.Title, "Soil Block", StringComparison.Ordinal))
            return GeometryBottomTableKind.Blocks;

        if (node.Payload is GeometrySurface
            || node.Payload is string surfaces && surfaces is TableSurfaces
                or GeometryDefinitionViewModel.NewSurfaceExplorerPayload
            || string.Equals(node.Title, "Surfaces", StringComparison.Ordinal)
            || string.Equals(node.Title, "New Surface", StringComparison.Ordinal))
            return GeometryBottomTableKind.Surfaces;

        return GeometryBottomTableKind.Points;
    }

    /// <summary>
    /// Deletes the current point, line, or surface the same way as the Actions Delete
    /// buttons on Geometry Definition Details.
    /// </summary>
    public bool TryDeleteSelection()
    {
        var payload = ResolveDeletePayload();
        if (payload != null)
        {
            Geometry.DeleteExplorerPayload(payload);
            ClearExplorerSelection();
            return true;
        }

        if (Geometry.CanDeleteSelectedSurface)
        {
            Geometry.DeleteSelectedSurfaceCommand.Execute(null);
            return true;
        }

        if (Geometry.CanDeleteLineSegment)
        {
            Geometry.DeleteLineSegmentCommand.Execute(null);
            return true;
        }

        var highlighted = Geometry.GeometryVertexRows.Where(v => v.IsHighlighted).ToList();
        if (highlighted.Count != 1)
            return false;

        Geometry.RemoveGeometryVertexCommand.Execute(highlighted[0]);
        return true;
    }

    private void DeleteSelected() => TryDeleteSelection();

    private object? ResolveDeletePayload()
    {
        var entity = UnwrapPayload(SelectedEntity);
        if (IsDeletablePayload(entity))
            return UnwrapPayload(entity);
        var nodePayload = UnwrapPayload(SelectedNode?.Payload);
        return IsDeletablePayload(nodePayload) ? nodePayload : null;
    }

    private static bool IsDeletablePayload(object? payload) =>
        payload is GeometryDefinitionViewModel.GeometryVertexDisplay
            or GeometryLineSegment
            or GeometrySurface
            or RegionExplorerPayload;

    private static object? UnwrapPayload(object? payload) =>
        payload is RegionExplorerPayload region ? region.Surface : payload;

    private void ClearExplorerSelection()
    {
        _selectedNode = null;
        _selectedEntity = null;
        OnPropertyChanged(nameof(SelectedNode));
        OnPropertyChanged(nameof(SelectedEntity));
        OnPropertyChanged(nameof(HasSelectedPoint));
        OnPropertyChanged(nameof(SelectedPointId));
        OnPropertyChanged(nameof(SelectedPointX));
        OnPropertyChanged(nameof(SelectedPointY));
        OnPropertyChanged(nameof(SelectedPointZ));
        OnPropertyChanged(nameof(SelectedPointDescription));
        OnPropertyChanged(nameof(SelectedPointType));
        Geometry.HighlightExplorerSelection(null);
    }

    private void Hook(INotifyCollectionChanged collection) =>
        collection.CollectionChanged += (_, _) => Refresh();

    private void SyncCanvas() =>
        _host.ApplyGeometryCanvasDecorationsForTab(_host.SelectedTabIndex);

    private void OnGeometryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GeometryDefinitionViewModel.GeometryCanvasCoordinates))
            OnPropertyChanged(nameof(StatusCursor));
        if (e.PropertyName == nameof(GeometryDefinitionViewModel.ShowGrid))
            OnPropertyChanged(nameof(ShowGrid));
        if (e.PropertyName == nameof(GeometryDefinitionViewModel.ShowAxes))
            OnPropertyChanged(nameof(ShowAxes));
    }

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(PointCount));
        OnPropertyChanged(nameof(LineCount));
        OnPropertyChanged(nameof(SurfaceCount));
        OnPropertyChanged(nameof(RegionCount));
        OnPropertyChanged(nameof(BlockCount));
        OnPropertyChanged(nameof(BooleanCount));
        OnPropertyChanged(nameof(ConstructionCount));
        OnPropertyChanged(nameof(StatusLastSaved));
        OnPropertyChanged(nameof(StatusCursor));
        OnPropertyChanged(nameof(StatusDimension));
        OnPropertyChanged(nameof(UnitsLabel));
    }

    private void RefreshTableRows()
    {
        var selectedSurface = _selectedRegionRow?.Surface;
        RegionRows.Clear();
        foreach (var surface in Geometry.GeometrySurfaces)
        {
            RegionRows.Add(new GeometryRegionTableRow
            {
                Surface = surface,
                Material = ResolveMaterialName(surface),
                Area = FormatArea(Geometry.GetSurfaceAxisArea(surface)),
                Color = Geometry.SurfaceColorOptions
                    .FirstOrDefault(o => o.Index == surface.ColorIndex)?.DisplayName ?? "Auto (by S#)"
            });
        }
        _selectedRegionRow = selectedSurface == null
            ? null
            : RegionRows.FirstOrDefault(r => ReferenceEquals(r.Surface, selectedSurface));
        OnPropertyChanged(nameof(SelectedRegionRow));

        var keepBlock = _selectedBlockRow != null && Geometry.HasClosedSoilPolygon;
        BlockRows.Clear();
        if (Geometry.HasClosedSoilPolygon)
        {
            BlockRows.Add(new GeometryBlockTableRow
            {
                Name = "Soil Block",
                Vertices = Geometry.GeometryVertexRows.Count,
                Area = FormatArea(Geometry.GetClosedSoilBlockArea()),
                Material = "—"
            });
        }
        _selectedBlockRow = keepBlock ? BlockRows.FirstOrDefault() : null;
        OnPropertyChanged(nameof(SelectedBlockRow));
    }

    private string ResolveMaterialName(GeometrySurface surface)
    {
        int? id = surface.AssignedMaterialId;
        foreach (var entry in Geometry.SurfaceMaterialFillsSnapshot)
        {
            if (entry.SurfaceNumber != surface.SurfaceNumber || entry.MaterialId is not int fillId)
                continue;
            id = fillId;
            break;
        }

        if (id is int matId)
        {
            var mat = _host.MaterialPropertiesVM.Materials.FirstOrDefault(m => m.Id == matId);
            if (mat != null && !string.IsNullOrWhiteSpace(mat.Name))
                return mat.Name;
        }

        return "—";
    }

    private static string FormatArea(double area) => area > 0 ? area.ToString("0.###") : "—";

    private bool MatchesFilter(string title) =>
        string.IsNullOrWhiteSpace(_filterText)
        || title.Contains(_filterText, StringComparison.OrdinalIgnoreCase);

    private static ProjectExplorerNode Folder(string title, string glyph, int count, bool expanded = false, object? payload = null) =>
        new()
        {
            Kind = ExplorerNodeKind.Folder,
            Title = title,
            Glyph = glyph,
            GlyphColor = GlyphVisible,
            Count = count,
            TabIndex = 1,
            IsExpanded = expanded,
            Payload = payload
        };

    private static string PointTitle(object vertex)
    {
        TryReadPoint(vertex, out var id, out _, out _, out var label);
        return string.IsNullOrWhiteSpace(label) ? $"Point {id}" : label;
    }

    private static bool TryReadPoint(object? entity, out string id, out double x, out double y, out string label)
    {
        id = "";
        x = 0;
        y = 0;
        label = "";
        if (entity == null) return false;
        var type = entity.GetType();
        var idProp = type.GetProperty("StablePointId");
        var xProp = type.GetProperty("AxisX");
        var yProp = type.GetProperty("AxisY");
        if (idProp == null || xProp == null || yProp == null)
            return false;
        id = Convert.ToString(idProp.GetValue(entity)) ?? "";
        x = Convert.ToDouble(xProp.GetValue(entity));
        y = Convert.ToDouble(yProp.GetValue(entity));
        label = Convert.ToString(type.GetProperty("PointLabel")?.GetValue(entity)) ?? $"Point {id}";
        return true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
