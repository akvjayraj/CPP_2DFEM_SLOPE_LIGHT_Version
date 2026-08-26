using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.ViewModels.Workspace;

/// <summary>
/// BOUNDARY-ribbon chrome: explorer, summary, properties, table.
/// Hosts existing BoundaryConditionsViewModel collections; does not invent solver BC types.
/// </summary>
public sealed class BoundaryWorkspaceViewModel : INotifyPropertyChanged
{
    public const string GlyphFolder = "\uE8B7";
    public const string GlyphBc = "\uE890";
    public const string GlyphChevronExpanded = "\uE70D";
    public const string GlyphChevronCollapsed = "\uE70E";

    public const string TypeFixed = "Fixed";
    public const string TypeRollerX = "Roller X";
    public const string TypeRollerY = "Roller Y";
    public const string TypePrescribedDisplacement = "Prescribed Displacement";
    public const string TypePorePressure = "Pore Pressure";
    public const string TypeSeepageFace = "Zero Pressure (P=0)";
    public const string OuterBoundariesFolderTitle = "Outer Boundaries";

    private readonly MainWindowViewModel _host;
    private ProjectExplorerNode? _selectedNode;
    private string _filterText = "";
    private bool _isExplorerExpanded = true;
    private bool _isSummaryExpanded = true;
    private bool _isTableExpanded = true;
    private bool _isPropertiesExpanded = true;
    private bool _isViewOptionsExpanded = true;
    private bool _refreshing;

    public BoundaryWorkspaceViewModel(MainWindowViewModel host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        Roots = new ObservableCollection<ProjectExplorerNode>();
        SelectNodeCommand = new RelayCommand<ProjectExplorerNode>(OnSelect);
        ToggleExplorerCommand = new RelayCommand(() => IsExplorerExpanded = !IsExplorerExpanded);
        ToggleSummaryCommand = new RelayCommand(() => IsSummaryExpanded = !IsSummaryExpanded);
        ToggleTableCommand = new RelayCommand(() => IsTableExpanded = !IsTableExpanded);
        TogglePropertiesCommand = new RelayCommand(() => IsPropertiesExpanded = !IsPropertiesExpanded);
        ToggleViewOptionsCommand = new RelayCommand(() => IsViewOptionsExpanded = !IsViewOptionsExpanded);
        ClosePropertiesCommand = new RelayCommand(() => IsPropertiesExpanded = false);
        ImportBoundariesCommand = new RelayCommand(OnImportBoundaries);
        StartDisplacementCommand = new RelayCommand(() => StartApplyType(TypePrescribedDisplacement));
        StartRollerCommand = new RelayCommand(() => StartApplyType(ResolveRollerType()));
        StartPinnedCommand = new RelayCommand(() => StartApplyType(TypeFixed));
        StartFixedCommand = new RelayCommand(() => StartApplyType(TypeFixed));
        StartPorePressureCommand = new RelayCommand(() => StartApplyType(TypePorePressure));
        StartSeepageFaceCommand = new RelayCommand(() => StartApplyType(TypeSeepageFace));
        EditSelectedCommand = new RelayCommand(OnEditSelected);
        DeleteSelectedCommand = new RelayCommand(
            () => Boundary.DeleteBoundaryConditionCommand.Execute(null),
            () => HasSelectedRow);
        ToggleMarkersCommand = new RelayCommand(() => Boundary.ShowMarkersCommand.Execute(null));
        ShowBcManagerCommand = new RelayCommand(OnShowBcManager);
        ApplyCommand = new RelayCommand(
            () => Boundary.ApplyBCToSelectedBoundaryCommand.Execute(null),
            () => Boundary.HasAvailableBcTypes
                  && (Boundary.SelectedOuterBoundary != null || !string.IsNullOrWhiteSpace(Boundary.NodesText)));
        Boundary.RefreshAvailableBcTypes();

        Boundary.BoundaryConditions.CollectionChanged += OnRowsChanged;
        Boundary.AvailableOuterBoundaries.CollectionChanged += (_, _) => Refresh();
        Boundary.BcMarkerLegendItems.CollectionChanged += (_, _) => NotifySummary();
        Boundary.PropertyChanged += OnBoundaryPropertyChanged;
        foreach (var row in Boundary.BoundaryConditions)
            row.PropertyChanged += OnRowPropertyChanged;
        _host.ProjectSettingsVM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ProjectSettingsViewModel.Idealization)
                or nameof(ProjectSettingsViewModel.SrfAxisymmetric))
                OnPropertyChanged(nameof(StatusDimension));
        };
        Refresh();
    }

    public BoundaryConditionsViewModel Boundary => _host.BoundaryConditionsVM;
    public ObservableCollection<ProjectExplorerNode> Roots { get; }
    public ObservableCollection<BcMarkerLegendItem> LegendItems => Boundary.BcMarkerLegendItems;

    public ICommand SelectNodeCommand { get; }
    public ICommand ToggleExplorerCommand { get; }
    public ICommand ToggleSummaryCommand { get; }
    public ICommand ToggleTableCommand { get; }
    public ICommand TogglePropertiesCommand { get; }
    public ICommand ToggleViewOptionsCommand { get; }
    public ICommand ClosePropertiesCommand { get; }
    public ICommand ImportBoundariesCommand { get; }
    public ICommand StartDisplacementCommand { get; }
    public ICommand StartRollerCommand { get; }
    public ICommand StartPinnedCommand { get; }
    public ICommand StartFixedCommand { get; }
    public ICommand StartPorePressureCommand { get; }
    public ICommand StartSeepageFaceCommand { get; }
    public ICommand EditSelectedCommand { get; }
    public ICommand DeleteSelectedCommand { get; }
    public ICommand ToggleMarkersCommand { get; }
    public ICommand ShowBcManagerCommand { get; }
    public ICommand ApplyCommand { get; }

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

    public BoundaryConditionRow? SelectedRow => Boundary.SelectedBoundaryCondition as BoundaryConditionRow;
    public bool HasSelectedRow => SelectedRow != null;

    public string? SelectedType
    {
        get => SelectedRow?.BCType ?? Boundary.SelectedBCTypeForBoundary;
        set => ApplyTypeDefaults(value);
    }

    public string SelectedLabel
    {
        get => SelectedRow?.BoundaryName
               ?? Boundary.SelectedOuterBoundary?.BoundaryTag
               ?? "";
        set
        {
            if (SelectedRow != null)
                SelectedRow.BoundaryName = value ?? "";
            OnPropertyChanged();
        }
    }

    public string? SelectedDofDisplay
    {
        get
        {
            var type = CurrentBcType;
            if (BcSymbolCatalog.ShowsRestraintFlags(type))
                return BcSymbolCatalog.FormatDofDisplay(type, Boundary.PendingDegreeOfFreedom);
            var dof = SelectedRow?.DegreeOfFreedom ?? Boundary.PendingDegreeOfFreedom;
            return dof == 1 ? "1 (Y)" : "0 (X)";
        }
        set
        {
            int dof = value != null && value.StartsWith("1", StringComparison.Ordinal) ? 1 : 0;
            Boundary.PendingDegreeOfFreedom = dof;
            if (SelectedRow != null)
                SelectedRow.DegreeOfFreedom = dof;
            OnPropertyChanged();
            NotifyEditor();
        }
    }

    public string UxValue => BcSymbolCatalog.DefaultUx(CurrentBcType);

    public string UyValue => BcSymbolCatalog.DefaultUy(CurrentBcType);

    public bool ShowRestraintFields => BcSymbolCatalog.ShowsRestraintFlags(CurrentBcType);
    public bool ShowDofField => BcSymbolCatalog.ShowsDof(CurrentBcType);
    public bool ShowValueField => BcSymbolCatalog.ShowsValue(CurrentBcType);
    public bool ValueIsReadOnly => BcSymbolCatalog.ValueIsReadOnly(CurrentBcType);
    public string ValueFieldLabel => BcSymbolCatalog.ValueFieldLabel(CurrentBcType);

    public bool ShowOnModel
    {
        get => MarkersVisible;
        set => MarkersVisible = value;
    }

    public bool MarkersVisible
    {
        get => Boundary.MarkersVisible;
        set
        {
            if (Boundary.MarkersVisible == value) return;
            Boundary.MarkersVisible = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowOnModel));
        }
    }

    public bool ShowStructuralMarkers
    {
        get => Boundary.ShowStructuralMarkers;
        set
        {
            if (Boundary.ShowStructuralMarkers == value) return;
            Boundary.ShowStructuralMarkers = value;
            OnPropertyChanged();
        }
    }

    public bool ShowSeepageMarkers
    {
        get => Boundary.ShowSeepageMarkers;
        set
        {
            if (Boundary.ShowSeepageMarkers == value) return;
            Boundary.ShowSeepageMarkers = value;
            OnPropertyChanged();
        }
    }

    public int TotalBcCount => Boundary.BoundaryConditions.Count;
    public int StructuralCount =>
        Boundary.BoundaryConditions.Count(r =>
            string.Equals(r.AnalysisCategory, BoundaryConditionAnalysisCategories.Structural, StringComparison.Ordinal));
    public int SeepageCount =>
        Boundary.BoundaryConditions.Count(r =>
            string.Equals(r.AnalysisCategory, BoundaryConditionAnalysisCategories.Seepage, StringComparison.Ordinal));

    public string StatusReady =>
        string.IsNullOrWhiteSpace(Boundary.StatusMessage) ? "Ready" : Boundary.StatusMessage;
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

    public void StartApplyType(string type)
    {
        ApplyTypeDefaults(type);
        IsPropertiesExpanded = true;
    }

    public void ApplyTypeDefaults(string? type)
    {
        Boundary.SelectedBCTypeForBoundary = type;
        if (SelectedRow != null)
            SelectedRow.BCType = type ?? "";
        Boundary.BcValueForBoundary = BcSymbolCatalog.DefaultValue(type);
        if (SelectedRow != null)
            SelectedRow.Value = Boundary.BcValueForBoundary;
        var implied = BcSymbolCatalog.ImpliedDegreeOfFreedom(type);
        if (implied.HasValue)
            Boundary.PendingDegreeOfFreedom = implied.Value;
        else if (SelectedRow != null)
            Boundary.PendingDegreeOfFreedom = SelectedRow.DegreeOfFreedom;
        NotifyEditor();
    }

    public string ResolveRollerType()
    {
        var selected = Boundary.SelectedOuterBoundary;
        if (selected == null)
            return TypeRollerX;
        var segment = _host.GeometryDefinitionVM.GeometryLineSegments
            .FirstOrDefault(s => s.LineNumber == selected.LineNumber);
        if (segment == null)
            return TypeRollerX;
        var from = _host.GeometryDefinitionVM.GeometryVertexRows
            .FirstOrDefault(v => string.Equals(v.PointLabel, segment.FromPoint, StringComparison.OrdinalIgnoreCase));
        var to = _host.GeometryDefinitionVM.GeometryVertexRows
            .FirstOrDefault(v => string.Equals(v.PointLabel, segment.ToPoint, StringComparison.OrdinalIgnoreCase));
        if (from == null || to == null)
            return TypeRollerX;
        var dx = Math.Abs(to.AxisX - from.AxisX);
        var dy = Math.Abs(to.AxisY - from.AxisY);
        return dy > dx ? TypeRollerY : TypeRollerX;
    }

    private void RefreshCore()
    {
        Roots.Clear();
        Roots.Add(OuterBoundariesFolder());

        var root = new ProjectExplorerNode
        {
            Kind = ExplorerNodeKind.BoundaryCondition,
            Title = "Boundary Conditions",
            Glyph = GlyphFolder,
            TabIndex = 5,
            Count = TotalBcCount,
            IsExpanded = true
        };

        root.Children.Add(CategoryFolder(
            "Structural",
            BoundaryConditionAnalysisCategories.Structural));
        root.Children.Add(CategoryFolder(
            "Seepage",
            BoundaryConditionAnalysisCategories.Seepage));

        Roots.Add(root);
        NotifySummary();
        NotifyEditor();
    }

    private ProjectExplorerNode OuterBoundariesFolder()
    {
        var folder = new ProjectExplorerNode
        {
            Kind = ExplorerNodeKind.Folder,
            Title = OuterBoundariesFolderTitle,
            Glyph = GlyphFolder,
            TabIndex = 5,
            Count = Boundary.AvailableOuterBoundaries.Count,
            IsExpanded = true
        };

        foreach (var info in Boundary.AvailableOuterBoundaries)
        {
            var title = string.IsNullOrWhiteSpace(info.BoundaryTag)
                ? $"L{info.LineNumber}"
                : info.BoundaryTag;
            if (!MatchesFilter(title))
                continue;
            folder.Children.Add(new ProjectExplorerNode
            {
                Kind = ExplorerNodeKind.BoundaryCondition,
                Title = title,
                Glyph = GlyphBc,
                TabIndex = 5,
                Count = info.NodeCount,
                Payload = info
            });
        }

        return folder;
    }

    private ProjectExplorerNode CategoryFolder(string title, string category)
    {
        var rows = Boundary.BoundaryConditions
            .Where(r => string.Equals(r.AnalysisCategory, category, StringComparison.Ordinal))
            .ToList();
        var folder = new ProjectExplorerNode
        {
            Kind = ExplorerNodeKind.Folder,
            Title = title,
            Glyph = GlyphFolder,
            TabIndex = 5,
            Count = rows.Count,
            IsExpanded = true,
            Payload = category
        };

        foreach (var group in rows.GroupBy(r => r.BCType).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var typeFolder = new ProjectExplorerNode
            {
                Kind = ExplorerNodeKind.BoundaryCondition,
                Title = group.Key,
                Glyph = GlyphBc,
                TabIndex = 5,
                Count = group.Count(),
                IsExpanded = true,
                Payload = group.Key
            };
            foreach (var row in group)
            {
                var name = FormatRowTitle(row);
                if (!MatchesFilter(name) && !MatchesFilter(row.BCType))
                    continue;
                typeFolder.Children.Add(new ProjectExplorerNode
                {
                    Kind = ExplorerNodeKind.BoundaryCondition,
                    Title = name,
                    Glyph = GlyphBc,
                    TabIndex = 5,
                    Payload = row
                });
            }
            if (typeFolder.Children.Count > 0 || string.IsNullOrWhiteSpace(_filterText))
                folder.Children.Add(typeFolder);
        }

        return folder;
    }

    private static string FormatRowTitle(BoundaryConditionRow row)
    {
        var label = string.IsNullOrWhiteSpace(row.BoundaryName) ? "Manual" : row.BoundaryName;
        return row.NodeId > 0 ? $"N{row.NodeId} · {label}" : label;
    }

    private void OnSelect(ProjectExplorerNode? node)
    {
        if (node == null) return;
        _host.SelectedTabIndex = 5;
        if (node.Payload is OuterBoundaryInfo outer)
        {
            Boundary.SelectedOuterBoundary = outer;
            IsPropertiesExpanded = true;
            NotifyEditor();
            return;
        }

        if (node.Payload is BoundaryConditionRow row)
        {
            Boundary.SelectedBoundaryCondition = row;
            Boundary.SelectedBCTypeForBoundary = row.BCType;
            Boundary.BcValueForBoundary = row.Value;
            NotifyEditor();
            return;
        }

        if (node.Payload is string typeOrCategory)
        {
            if (BoundaryConditionAnalysisCategories.IsStructuralBcType(typeOrCategory)
                || BoundaryConditionAnalysisCategories.IsSeepageBcType(typeOrCategory))
            {
                StartApplyType(typeOrCategory);
                var match = Boundary.BoundaryConditions.FirstOrDefault(r =>
                    string.Equals(r.BCType, typeOrCategory, StringComparison.Ordinal));
                if (match != null)
                    Boundary.SelectedBoundaryCondition = match;
            }
        }
    }

    private void OnImportBoundaries()
    {
        if (string.Equals(
                Boundary.SelectedImportMode,
                BoundaryConditionsViewModel.ImportModeBoundary,
                StringComparison.Ordinal))
        {
            Boundary.ImportOuterBoundariesCommand.Execute(null);
        }
        else
        {
            Boundary.SelectedImportMode = BoundaryConditionsViewModel.ImportModeBoundary;
        }

        IsPropertiesExpanded = true;
    }

    private void OnEditSelected()
    {
        IsPropertiesExpanded = true;
        if (Boundary.SelectedBoundaryCondition == null)
            Boundary.SelectedBoundaryCondition = Boundary.BoundaryConditions.FirstOrDefault();
        NotifyEditor();
    }

    private void OnShowBcManager()
    {
        IsExplorerExpanded = true;
        IsSummaryExpanded = true;
        IsTableExpanded = true;
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is BoundaryConditionRow removed)
                    removed.PropertyChanged -= OnRowPropertyChanged;
            }
        }

        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is BoundaryConditionRow added)
                    added.PropertyChanged += OnRowPropertyChanged;
            }
        }

        Refresh();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BoundaryConditionRow.BCType)
            or nameof(BoundaryConditionRow.BoundaryName)
            or nameof(BoundaryConditionRow.NodeId)
            or nameof(BoundaryConditionRow.AnalysisCategory))
            Refresh();
        if (ReferenceEquals(sender, SelectedRow))
            NotifyEditor();
    }

    private void OnBoundaryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BoundaryConditionsViewModel.SelectedBoundaryCondition))
        {
            OnPropertyChanged(nameof(SelectedRow));
            OnPropertyChanged(nameof(HasSelectedRow));
            NotifyEditor();
        }
        if (e.PropertyName is nameof(BoundaryConditionsViewModel.MarkersVisible)
            or nameof(BoundaryConditionsViewModel.ShowMarkersButtonLabel))
        {
            OnPropertyChanged(nameof(MarkersVisible));
            OnPropertyChanged(nameof(ShowOnModel));
        }
        if (e.PropertyName == nameof(BoundaryConditionsViewModel.ShowStructuralMarkers))
            OnPropertyChanged(nameof(ShowStructuralMarkers));
        if (e.PropertyName == nameof(BoundaryConditionsViewModel.ShowSeepageMarkers))
            OnPropertyChanged(nameof(ShowSeepageMarkers));
        if (e.PropertyName is nameof(BoundaryConditionsViewModel.StatusMessage))
            OnPropertyChanged(nameof(StatusReady));
        if (e.PropertyName is nameof(BoundaryConditionsViewModel.SelectedImportMode))
            Refresh();
        if (e.PropertyName is nameof(BoundaryConditionsViewModel.SelectedBCTypeForBoundary)
            or nameof(BoundaryConditionsViewModel.BcValueForBoundary)
            or nameof(BoundaryConditionsViewModel.ConnectedNodesDisplayText)
            or nameof(BoundaryConditionsViewModel.NodesText)
            or nameof(BoundaryConditionsViewModel.SelectedOuterBoundary))
            NotifyEditor();
    }

    private void NotifySummary()
    {
        OnPropertyChanged(nameof(TotalBcCount));
        OnPropertyChanged(nameof(StructuralCount));
        OnPropertyChanged(nameof(SeepageCount));
        OnPropertyChanged(nameof(LegendItems));
    }

    private void NotifyEditor()
    {
        OnPropertyChanged(nameof(SelectedRow));
        OnPropertyChanged(nameof(HasSelectedRow));
        OnPropertyChanged(nameof(SelectedType));
        OnPropertyChanged(nameof(SelectedLabel));
        OnPropertyChanged(nameof(SelectedDofDisplay));
        OnPropertyChanged(nameof(UxValue));
        OnPropertyChanged(nameof(UyValue));
        OnPropertyChanged(nameof(ShowRestraintFields));
        OnPropertyChanged(nameof(ShowDofField));
        OnPropertyChanged(nameof(ShowValueField));
        OnPropertyChanged(nameof(ValueIsReadOnly));
        OnPropertyChanged(nameof(ValueFieldLabel));
        OnPropertyChanged(nameof(ShowOnModel));
    }

    private string? CurrentBcType =>
        Boundary.SelectedBCTypeForBoundary ?? SelectedRow?.BCType;

    private bool MatchesFilter(string title) =>
        string.IsNullOrWhiteSpace(_filterText)
        || title.Contains(_filterText, StringComparison.OrdinalIgnoreCase);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
