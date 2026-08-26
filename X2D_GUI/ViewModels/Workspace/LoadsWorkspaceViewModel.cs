using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.ViewModels.Workspace;

/// <summary>
/// LOADS-ribbon chrome: explorer, summary, properties, table, inspector.
/// Hosts existing LoadConditionsViewModel collections; does not invent load types.
/// </summary>
public sealed class LoadsWorkspaceViewModel : INotifyPropertyChanged
{
    public const string GlyphFolder = "\uE8B7";
    public const string GlyphNodal = "\uE8FD";
    public const string GlyphLine = "\uE8B3";
    public const string GlyphEdge = "\uE7C9";
    public const string GlyphField = "\uE80A";
    public const string GlyphSeismic = "\uE895";
    public const string GlyphChevronExpanded = "\uE70D";
    public const string GlyphChevronCollapsed = "\uE70E";

    public const string PayloadFieldStress = "field-stress";
    public const string PayloadSeismic = "seismic";

    private readonly MainWindowViewModel _host;
    private ProjectExplorerNode? _selectedNode;
    private string _filterText = "";
    private bool _isExplorerExpanded = true;
    private bool _isSummaryExpanded = true;
    private bool _isTableExpanded = true;
    private bool _isPropertiesExpanded = true;
    private bool _isInspectorExpanded = true;
    private bool _isViewOptionsExpanded = true;
    private bool _showPointLoads = true;
    private bool _showLineLoads = true;
    private string _selectedSection = "load";
    private bool _refreshing;
    private bool _notifyingInspector;

    public LoadsWorkspaceViewModel(MainWindowViewModel host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        Roots = new ObservableCollection<ProjectExplorerNode>();
        SelectNodeCommand = new RelayCommand<ProjectExplorerNode>(OnSelect);
        ToggleExplorerCommand = new RelayCommand(() => IsExplorerExpanded = !IsExplorerExpanded);
        ToggleSummaryCommand = new RelayCommand(() => IsSummaryExpanded = !IsSummaryExpanded);
        ToggleTableCommand = new RelayCommand(() => IsTableExpanded = !IsTableExpanded);
        TogglePropertiesCommand = new RelayCommand(() => IsPropertiesExpanded = !IsPropertiesExpanded);
        ToggleInspectorCommand = new RelayCommand(() => IsInspectorExpanded = !IsInspectorExpanded);
        ToggleViewOptionsCommand = new RelayCommand(() => IsViewOptionsExpanded = !IsViewOptionsExpanded);
        AddNodalLoadCommand = new RelayCommand(() => Loads.AddLoadOfType(LoadType.Point));
        AddLineLoadCommand = new RelayCommand(() => Loads.AddLoadOfType(LoadType.Line));
        DeleteSelectedCommand = new RelayCommand(OnDelete, () => HasSelectedLoad);
        ActivateCommand = new RelayCommand(() => SetEnabled(true), () => HasSelectedLoad);
        DeactivateCommand = new RelayCommand(() => SetEnabled(false), () => HasSelectedLoad);
        FocusFieldStressCommand = new RelayCommand(() => SelectSection(PayloadFieldStress));
        FocusSeismicCommand = new RelayCommand(() =>
        {
            Loads.IsSeismicExpanderExpanded = true;
            SelectSection(PayloadSeismic);
        });

        Loads.Loads.CollectionChanged += OnLoadsChanged;
        Loads.PropertyChanged += OnLoadsPropertyChanged;
        foreach (var load in Loads.Loads)
            load.PropertyChanged += OnLoadModelPropertyChanged;
        _host.ProjectSettingsVM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ProjectSettingsViewModel.Idealization)
                or nameof(ProjectSettingsViewModel.SrfAxisymmetric))
                OnPropertyChanged(nameof(StatusDimension));
        };
        Refresh();
    }

    public LoadConditionsViewModel Loads => _host.LoadConditionsVM;
    public ObservableCollection<ProjectExplorerNode> Roots { get; }
    public ICommand SelectNodeCommand { get; }
    public ICommand ToggleExplorerCommand { get; }
    public ICommand ToggleSummaryCommand { get; }
    public ICommand ToggleTableCommand { get; }
    public ICommand TogglePropertiesCommand { get; }
    public ICommand ToggleInspectorCommand { get; }
    public ICommand ToggleViewOptionsCommand { get; }
    public ICommand AddNodalLoadCommand { get; }
    public ICommand AddLineLoadCommand { get; }
    public ICommand DeleteSelectedCommand { get; }
    public ICommand ActivateCommand { get; }
    public ICommand DeactivateCommand { get; }
    public ICommand FocusFieldStressCommand { get; }
    public ICommand FocusSeismicCommand { get; }

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

    public bool IsSummaryExpanded
    {
        get => _isSummaryExpanded;
        set
        {
            if (_isSummaryExpanded == value) return;
            _isSummaryExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SummaryChevronGlyph));
        }
    }

    public bool IsTableExpanded
    {
        get => _isTableExpanded;
        set
        {
            if (_isTableExpanded == value) return;
            _isTableExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TableChevronGlyph));
            _host.NotifyWorkspaceChromeChanged();
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
            _host.NotifyWorkspaceChromeChanged();
        }
    }

    public bool IsInspectorExpanded
    {
        get => _isInspectorExpanded;
        set
        {
            if (_isInspectorExpanded == value) return;
            _isInspectorExpanded = value;
            OnPropertyChanged();
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

    public bool ShowPointLoads
    {
        get => _showPointLoads;
        set
        {
            if (_showPointLoads == value) return;
            _showPointLoads = value;
            OnPropertyChanged();
            _host.ApplyGeometryCanvasDecorationsForTab(_host.SelectedTabIndex);
        }
    }

    public bool ShowLineLoads
    {
        get => _showLineLoads;
        set
        {
            if (_showLineLoads == value) return;
            _showLineLoads = value;
            OnPropertyChanged();
            _host.ApplyGeometryCanvasDecorationsForTab(_host.SelectedTabIndex);
        }
    }

    public string ExplorerChevronGlyph => IsExplorerExpanded ? GlyphChevronExpanded : GlyphChevronCollapsed;
    public string SummaryChevronGlyph => IsSummaryExpanded ? GlyphChevronExpanded : GlyphChevronCollapsed;
    public string TableChevronGlyph => IsTableExpanded ? GlyphChevronExpanded : GlyphChevronCollapsed;

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

    public string SelectedSection
    {
        get => _selectedSection;
        private set
        {
            if (_selectedSection == value) return;
            _selectedSection = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLoadSection));
            OnPropertyChanged(nameof(IsFieldStressSection));
            OnPropertyChanged(nameof(IsSeismicSection));
        }
    }

    public bool IsLoadSection => SelectedSection == "load";
    public bool IsFieldStressSection => SelectedSection == PayloadFieldStress;
    public bool IsSeismicSection => SelectedSection == PayloadSeismic;

    public LoadModel? SelectedLoad => Loads.SelectedLoad;
    public bool HasSelectedLoad => SelectedLoad != null;

    public IReadOnlyList<string> TypeDisplayOptions { get; } =
        new[] { "Point Load", "Line Load", "Surface Load" };
    public IReadOnlyList<string> StatusOptions { get; } =
        new[] { "Active", "Inactive" };
    public IReadOnlyList<string> LoadGroupOptions { get; } =
        new[] { "LG-01 (Permanent)", "LG-02 (Variable)", "Ungrouped" };
    public IReadOnlyList<string> CoordinateSystemOptions { get; } =
        new[] { "Global", "Local" };
    public IReadOnlyList<string> DistributionOptions { get; } =
        new[] { "Uniform", "Linear", "Triangular" };
    public IReadOnlyList<string> DirectionOptions { get; } =
        new[] { "Vertical", "Horizontal", "Normal to Surface", "Angle from Horizontal", "Angle to Boundary" };
    public IReadOnlyList<string> ProjectionOptions { get; } =
        new[] { "Normal to Surface", "Vertical", "Horizontal" };
    public IReadOnlyList<string> ApplyToOptions { get; } =
        new[] { "Selected Surfaces (1)", "Selected Edges (1)", "Selected Nodes (1)", "All Surfaces" };

    public IReadOnlyList<LoadStageChoice> StageFromChoices => BuildStageChoices(includeAllSubsequent: false);
    public IReadOnlyList<LoadStageChoice> StageToChoices => BuildStageChoices(includeAllSubsequent: true);

    public int TotalLoadCount => Loads.Loads.Count;
    public int NodalCount => Loads.Loads.Count(l => l.Type == LoadType.Point);
    public int LineCount => Loads.Loads.Count(l => l.Type == LoadType.Line);
    public int TriangularCount => Loads.Loads.Count(l => l.Type == LoadType.Triangular);
    public int EnabledCount => Loads.Loads.Count(l => l.IsEnabled);

    public string InspectorName
    {
        get
        {
            if (SelectedLoad == null)
                return "—";
            var name = string.IsNullOrWhiteSpace(SelectedLoad.Name)
                ? FormatType(SelectedLoad.Type)
                : SelectedLoad.Name;
            return string.IsNullOrWhiteSpace(SelectedLoad.IdCode)
                ? name
                : $"{name} ({SelectedLoad.IdCode})";
        }
    }
    public string InspectorId =>
        string.IsNullOrWhiteSpace(SelectedLoad?.IdCode) ? "—" : SelectedLoad!.IdCode;
    public string InspectorType => SelectedLoad?.DisplayType ?? FormatType(SelectedLoad?.Type);
    public string InspectorGroup => SelectedLoad?.LoadGroup ?? "—";
    public string InspectorDirection => SelectedLoad == null ? "—" : SelectedLoad.DisplayDirection;
    public string InspectorValue =>
        SelectedLoad == null ? "—" : $"{SelectedLoad.Magnitude:0.00} {SelectedLoad.MagnitudeUnit}";
    public string InspectorDistribution => SelectedLoad?.DisplayDistribution ?? "—";
    public string InspectorLoadGroup => InspectorGroup;
    public string InspectorStatus =>
        SelectedLoad == null ? "—" : SelectedLoad.IsEnabled ? "Active" : "Inactive";
    public string InspectorStatusColor =>
        SelectedLoad is { IsEnabled: true } ? "#FF34D399" : "#FFF87171";
    public string InspectorSurfaces => SelectedLoad?.Type == LoadType.Triangular ? "1" : "—";
    public string InspectorArea => "—";
    public bool InspectorShowsLength => SelectedLoad is { IsDistributedLineLoad: true };
    public string InspectorLength
    {
        get
        {
            if (SelectedLoad is not { IsDistributedLineLoad: true } load)
                return "—";
            var len = AxisLength(load);
            return len <= 0 ? "—" : $"{len:0.00} m";
        }
    }
    public string InspectorPreviewCaption =>
        SelectedLoad == null ? "—" : DirectionLabel(SelectedLoad);
    public double InspectorPreviewAngle
    {
        get
        {
            if (SelectedLoad == null)
                return 0;
            try
            {
                var (ux, uy) = ResolveUnit(SelectedLoad);
                return -Math.Atan2(ux, -uy) * 180.0 / Math.PI;
            }
            catch
            {
                return 0;
            }
        }
    }
    public string InspectorStageFrom =>
        SelectedLoad?.ApplyFromStage is { } n ? $"Stage {n}" : "All stages";
    public string InspectorStageTo =>
        SelectedLoad?.ApplyToStage is { } n ? $"Stage {n}" : "All Subsequent";
    public string InspectorResultantVertical => SafeResultant().fy;
    public string InspectorResultantHorizontalX => SafeResultant().fx;
    public string InspectorResultantHorizontalY => "0.00";
    public string InspectorMagnitude => InspectorValue;
    public string InspectorLocation =>
        SelectedLoad == null
            ? "—"
            : SelectedLoad.IsPointLoad
                ? $"({SelectedLoad.StartX:0.##}, {SelectedLoad.StartY:0.##})"
                : $"({SelectedLoad.StartX:0.##}, {SelectedLoad.StartY:0.##}) → ({SelectedLoad.EndX:0.##}, {SelectedLoad.EndY:0.##})";
    public string InspectorOrientation => InspectorDirection;
    public string InspectorStage => InspectorStageFrom;

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
            var raw = _host.GeometryDefinitionVM.GeometryCanvasCoordinates;
            return string.IsNullOrWhiteSpace(raw) ? "X: —  Y: —" : raw;
        }
    }

    public void Refresh()
    {
        if (_refreshing)
            return;
        _refreshing = true;
        try
        {
            RefreshCore();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RefreshCore()
    {
        Roots.Clear();
        var root = new ProjectExplorerNode
        {
            Kind = ExplorerNodeKind.LoadGroup,
            Title = "All Loads",
            Glyph = GlyphFolder,
            TabIndex = 4,
            Count = TotalLoadCount,
            IsExpanded = true
        };

        root.Children.Add(Group("Nodal Loads", GlyphNodal, LoadType.Point));
        root.Children.Add(Group("Line Loads", GlyphLine, LoadType.Line));
        root.Children.Add(Group("Triangular Edge", GlyphEdge, LoadType.Triangular));

        if (MatchesFilter("Field Stress"))
        {
            root.Children.Add(new ProjectExplorerNode
            {
                Kind = ExplorerNodeKind.Folder,
                Title = "Field Stress",
                Glyph = GlyphField,
                TabIndex = 4,
                Payload = PayloadFieldStress
            });
        }

        if (MatchesFilter("Seismic"))
        {
            root.Children.Add(new ProjectExplorerNode
            {
                Kind = ExplorerNodeKind.Folder,
                Title = "Seismic",
                Glyph = GlyphSeismic,
                TabIndex = 4,
                Payload = PayloadSeismic
            });
        }

        Roots.Add(root);
        NotifyStats();
        EnsureSelectedLoad();
    }

    public void EnsureSelectedLoad()
    {
        if (SelectedSection is PayloadFieldStress or PayloadSeismic)
            return;
        if (Loads.SelectedLoad != null && Loads.Loads.Contains(Loads.SelectedLoad))
            return;
        var first = Loads.Loads.FirstOrDefault();
        if (first == null)
            return;
        Loads.SelectedLoad = first;
        SelectedSection = "load";
    }

    private ProjectExplorerNode Group(string title, string glyph, LoadType type)
    {
        var rows = Loads.Loads.Where(l => l.Type == type).ToList();
        var folder = new ProjectExplorerNode
        {
            Kind = ExplorerNodeKind.LoadGroup,
            Title = title,
            Glyph = glyph,
            TabIndex = 4,
            Count = rows.Count,
            IsExpanded = true,
            Payload = type
        };
        foreach (var load in rows)
        {
            var name = string.IsNullOrWhiteSpace(load.Name) ? FormatType(type) : load.Name;
            if (!MatchesFilter(name)) continue;
            folder.Children.Add(new ProjectExplorerNode
            {
                Kind = ExplorerNodeKind.Load,
                Title = name,
                Glyph = glyph,
                TabIndex = 4,
                Payload = load
            });
        }
        return folder;
    }

    private void OnSelect(ProjectExplorerNode? node)
    {
        if (node == null) return;
        _host.SelectedTabIndex = 4;
        if (node.Payload is LoadModel load)
        {
            Loads.SelectedLoad = load;
            SelectedSection = "load";
            NotifyInspector();
            return;
        }

        if (node.Payload is LoadType type)
        {
            SelectedSection = "load";
            var match = Loads.Loads.FirstOrDefault(l => l.Type == type);
            if (match != null)
                Loads.SelectedLoad = match;
            NotifyInspector();
            return;
        }

        if (node.Payload is string section)
        {
            SelectSection(section);
            return;
        }

        EnsureSelectedLoad();
    }

    private void SelectSection(string section)
    {
        _host.SelectedTabIndex = 4;
        SelectedSection = section;
        if (section == PayloadSeismic)
            Loads.IsSeismicExpanderExpanded = true;
    }

    private void OnDelete()
    {
        if (SelectedLoad != null)
            Loads.RemoveLoadCommand.Execute(SelectedLoad);
    }

    private void SetEnabled(bool enabled)
    {
        if (SelectedLoad == null) return;
        SelectedLoad.IsEnabled = enabled;
        NotifyInspector();
        NotifyStats();
    }

    private void OnLoadsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is LoadModel removed)
                    removed.PropertyChanged -= OnLoadModelPropertyChanged;
            }
        }

        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is LoadModel added)
                    added.PropertyChanged += OnLoadModelPropertyChanged;
            }
        }

        Refresh();
    }

    private void OnLoadModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LoadModel.Name) or nameof(LoadModel.IdCode) or nameof(LoadModel.Type))
            Refresh();
        if (ReferenceEquals(sender, SelectedLoad))
            NotifyInspector();
    }

    private void OnLoadsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LoadConditionsViewModel.SelectedLoad))
        {
            OnPropertyChanged(nameof(SelectedLoad));
            OnPropertyChanged(nameof(HasSelectedLoad));
            NotifyInspector();
        }
    }

    private void NotifyStats()
    {
        OnPropertyChanged(nameof(TotalLoadCount));
        OnPropertyChanged(nameof(NodalCount));
        OnPropertyChanged(nameof(LineCount));
        OnPropertyChanged(nameof(TriangularCount));
        OnPropertyChanged(nameof(EnabledCount));
    }

    private void NotifyInspector()
    {
        if (_notifyingInspector)
            return;
        _notifyingInspector = true;
        try
        {
            OnPropertyChanged(nameof(SelectedLoad));
            OnPropertyChanged(nameof(HasSelectedLoad));
            OnPropertyChanged(nameof(InspectorName));
            OnPropertyChanged(nameof(InspectorId));
            OnPropertyChanged(nameof(InspectorType));
            OnPropertyChanged(nameof(InspectorGroup));
            OnPropertyChanged(nameof(InspectorMagnitude));
            OnPropertyChanged(nameof(InspectorLocation));
            OnPropertyChanged(nameof(InspectorOrientation));
            OnPropertyChanged(nameof(InspectorDirection));
            OnPropertyChanged(nameof(InspectorValue));
            OnPropertyChanged(nameof(InspectorDistribution));
            OnPropertyChanged(nameof(InspectorLoadGroup));
            OnPropertyChanged(nameof(InspectorStatusColor));
            OnPropertyChanged(nameof(InspectorSurfaces));
            OnPropertyChanged(nameof(InspectorArea));
            OnPropertyChanged(nameof(InspectorLength));
            OnPropertyChanged(nameof(InspectorShowsLength));
            OnPropertyChanged(nameof(InspectorPreviewCaption));
            OnPropertyChanged(nameof(InspectorPreviewAngle));
            OnPropertyChanged(nameof(InspectorStageFrom));
            OnPropertyChanged(nameof(InspectorStageTo));
            OnPropertyChanged(nameof(InspectorResultantVertical));
            OnPropertyChanged(nameof(InspectorResultantHorizontalX));
            OnPropertyChanged(nameof(InspectorResultantHorizontalY));
            OnPropertyChanged(nameof(InspectorStatus));
            OnPropertyChanged(nameof(InspectorStage));
        }
        finally
        {
            _notifyingInspector = false;
        }
    }

    private bool MatchesFilter(string title) =>
        string.IsNullOrWhiteSpace(_filterText)
        || title.Contains(_filterText, StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<LoadStageChoice> BuildStageChoices(bool includeAllSubsequent)
    {
        var list = new List<LoadStageChoice>();
        if (includeAllSubsequent)
            list.Add(new LoadStageChoice { Number = 0, Name = "All Subsequent" });

        var tabs = _host.ProjectSettingsVM.StageFooterTabs;
        if (tabs.Count > 0)
        {
            foreach (var tab in tabs)
            {
                var label = string.IsNullOrWhiteSpace(tab.StageName)
                    ? $"Stage {tab.StageNumber}"
                    : tab.StageName.StartsWith("Stage", StringComparison.OrdinalIgnoreCase)
                        ? tab.StageName
                        : $"Stage {tab.StageNumber}";
                list.Add(new LoadStageChoice { Number = tab.StageNumber, Name = label });
            }
        }
        else
        {
            list.Add(new LoadStageChoice { Number = 1, Name = "Stage 1" });
        }

        return list;
    }

    private static string FormatType(LoadType? type) => type switch
    {
        LoadType.Point => "Point Load",
        LoadType.Line => "Line Load",
        LoadType.Triangular => "Surface Load",
        _ => "—"
    };

    private static string DirectionLabel(LoadModel load)
    {
        var axis = load.Orientation switch
        {
            LoadOrientation.VerticalDown => "Global -Y",
            LoadOrientation.HorizontalRight => "Global +X",
            LoadOrientation.AngleFromHorizontal => $"Global {load.AngleDeg:0}°",
            LoadOrientation.AngleToBoundary => $"Boundary {load.AngleDeg:0}°",
            _ => load.CoordinateSystem
        };
        return $"{load.DisplayDirection} ({axis})";
    }

    private (double ux, double uy) ResolveUnit(LoadModel load)
    {
        var geometry = _host.GeometryDefinitionVM;
        var mesh = _host.MeshVM;
        if (load.IsPointLoad)
        {
            var nodeId = LoadExportDirectionResolver.ResolvePointNodeId(load, mesh, geometry);
            var axis = LoadExportDirectionResolver.ResolveNodeAxisPoint(mesh, geometry, nodeId, load);
            return LoadExportDirectionResolver.TryResolvePointAxisDirection(load, geometry, mesh, axis, out var ux, out var uy)
                ? (ux, uy)
                : (0, -1);
        }

        var (_, dx, dy) = LoadExportDirectionResolver.ResolveEdgeGroupDirection(load, geometry, mesh);
        var length = Math.Sqrt(dx * dx + dy * dy);
        return length > 1e-12 ? (dx / length, dy / length) : (0, -1);
    }

    private double AxisLength(LoadModel load)
    {
        var geometry = _host.GeometryDefinitionVM;
        var start = geometry.ConvertCanvasToAxis(new Point(load.StartX, load.StartY));
        var end = geometry.ConvertCanvasToAxis(new Point(load.EndX, load.EndY));
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private (string fx, string fy) SafeResultant()
    {
        try
        {
            var (fx, fy) = ResolveResultant();
            return (FormatForce(fx), FormatForce(fy));
        }
        catch
        {
            return ("—", "—");
        }
    }

    private (double? fx, double? fy) ResolveResultant()
    {
        if (SelectedLoad == null)
            return (null, null);

        var load = SelectedLoad;
        if (load.IsTriangularLoad)
            return (null, null);

        var geometry = _host.GeometryDefinitionVM;
        var mesh = _host.MeshVM;
        if (load.IsPointLoad)
        {
            var nodeId = LoadExportDirectionResolver.ResolvePointNodeId(load, mesh, geometry);
            var force = LoadExportDirectionResolver.ResolvePointForceComponents(load, geometry, mesh, nodeId);
            return (force.fx, force.fy);
        }

        var length = AxisLength(load);
        if (length <= 1e-12)
            return (0, 0);

        var (ux, uy) = ResolveUnit(load);
        var forceMag = load.Magnitude * length;
        return (forceMag * ux, forceMag * uy);
    }

    private static string FormatForce(double? value) =>
        value is { } n ? n.ToString("N2") : "—";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class LoadStageChoice
{
    public int Number { get; init; }
    public string Name { get; init; } = "";
}
