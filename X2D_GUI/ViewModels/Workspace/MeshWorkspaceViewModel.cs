using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.ViewModels.Workspace;

/// <summary>
/// MESH-ribbon chrome: explorer, statistics, generator selection.
/// Hosts existing MeshViewModel collections; does not invent mesh sets.
/// </summary>
public sealed class MeshWorkspaceViewModel : INotifyPropertyChanged
{
    public const string GlyphMesh = "\uE80A";
    public const string GlyphFolder = "\uE8B7";
    public const string GlyphElement = "\uE8A5";
    public const string GlyphDiag = "\uE9D5";
    public const string GlyphChevronExpanded = "\uE70D";
    public const string GlyphChevronCollapsed = "\uE70E";

    public const string PayloadGlobal = "global";
    public const string PayloadDiagnostics = "diagnostics";

    private readonly MainWindowViewModel _host;
    private ProjectExplorerNode? _selectedNode;
    private string _filterText = "";
    private bool _isExplorerExpanded = true;
    private bool _isStatsExpanded = true;
    private bool _isTableExpanded = true;
    private bool _isControlsExpanded = true;
    private bool _isViewOptionsExpanded = true;

    public MeshWorkspaceViewModel(MainWindowViewModel host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        Roots = new ObservableCollection<ProjectExplorerNode>();
        SelectNodeCommand = new RelayCommand<ProjectExplorerNode>(OnSelect);
        ToggleExplorerCommand = new RelayCommand(() => IsExplorerExpanded = !IsExplorerExpanded);
        ToggleStatsCommand = new RelayCommand(() => IsStatsExpanded = !IsStatsExpanded);
        ToggleTableCommand = new RelayCommand(() => IsTableExpanded = !IsTableExpanded);
        ToggleControlsCommand = new RelayCommand(() => IsControlsExpanded = !IsControlsExpanded);
        ToggleViewOptionsCommand = new RelayCommand(() => IsViewOptionsExpanded = !IsViewOptionsExpanded);
        SelectUnstructuredCommand = new RelayCommand(() =>
            Mesh.MeshPopupSelectedTabIndex = MeshViewModel.MeshPopupRs2dTabIndex);
        SelectStructuredCommand = new RelayCommand(() =>
            Mesh.MeshPopupSelectedTabIndex = MeshViewModel.MeshPopupUserDefinedTabIndex);
        Select1DCommand = new RelayCommand(() =>
            Mesh.MeshPopupSelectedTabIndex = MeshViewModel.MeshPopupOneDTabIndex);
        FocusStatisticsCommand = new RelayCommand(() => IsStatsExpanded = true);
        GenerateCommand = new RelayCommand(OnGenerate);
        DiscretizeActiveCommand = new RelayCommand(OnDiscretize);
        ClearCommand = new RelayCommand(OnClear);

        Hook(Mesh.MeshInformationRows);
        Hook(Mesh.DiscretizationNodes);
        Mesh.PropertyChanged += OnMeshPropertyChanged;
        _host.ProjectSettingsVM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ProjectSettingsViewModel.Idealization)
                or nameof(ProjectSettingsViewModel.SrfAxisymmetric))
                OnPropertyChanged(nameof(StatusDimension));
        };
        Refresh();
    }

    public MeshViewModel Mesh => _host.MeshVM;
    public ObservableCollection<ProjectExplorerNode> Roots { get; }
    public ICommand SelectNodeCommand { get; }
    public ICommand ToggleExplorerCommand { get; }
    public ICommand ToggleStatsCommand { get; }
    public ICommand ToggleTableCommand { get; }
    public ICommand ToggleControlsCommand { get; }
    public ICommand ToggleViewOptionsCommand { get; }
    public ICommand SelectUnstructuredCommand { get; }
    public ICommand SelectStructuredCommand { get; }
    public ICommand Select1DCommand { get; }
    public ICommand FocusStatisticsCommand { get; }
    public ICommand GenerateCommand { get; }
    public ICommand DiscretizeActiveCommand { get; }
    public ICommand ClearCommand { get; }

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

    public bool IsStatsExpanded
    {
        get => _isStatsExpanded;
        set
        {
            if (_isStatsExpanded == value) return;
            _isStatsExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatsChevronGlyph));
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

    public bool IsControlsExpanded
    {
        get => _isControlsExpanded;
        set
        {
            if (_isControlsExpanded == value) return;
            _isControlsExpanded = value;
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
    public string StatsChevronGlyph => IsStatsExpanded ? GlyphChevronExpanded : GlyphChevronCollapsed;
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

    public bool IsUnstructuredGenerator =>
        Mesh.MeshPopupSelectedTabIndex == MeshViewModel.MeshPopupRs2dTabIndex;
    public bool IsStructuredGenerator =>
        Mesh.MeshPopupSelectedTabIndex == MeshViewModel.MeshPopupUserDefinedTabIndex;
    public bool Is1DGenerator =>
        Mesh.MeshPopupSelectedTabIndex == MeshViewModel.MeshPopupOneDTabIndex;

    public string GeneratorLabel =>
        Mesh.Is1DProblem ? "1D"
        : IsUnstructuredGenerator ? "Unstructured"
        : "Structured";

    public string ElementFamilyLabel
    {
        get
        {
            if (Mesh.Is1DProblem)
                return "1D";
            if (IsUnstructuredGenerator)
                return string.IsNullOrWhiteSpace(Mesh.Rs2dElementType) ? "—" : Mesh.Rs2dElementType;
            return string.IsNullOrWhiteSpace(Mesh.UserDefinedMeshVM.ElementType)
                ? "—"
                : Mesh.UserDefinedMeshVM.ElementType;
        }
    }

    public int ElementCount => Mesh.MeshInformationRows.Count;
    public int NodeCount
    {
        get
        {
            if (Mesh.DiscretizationNodes.Count > 0)
                return Mesh.DiscretizationNodes.Count;
            if (Mesh.SurfaceMeshNodeCount > 0)
                return Mesh.SurfaceMeshNodeCount;
            var ids = new HashSet<int>();
            foreach (var row in Mesh.MeshInformationRows)
            {
                AddNode(ids, row.Node1);
                AddNode(ids, row.Node2);
                AddNode(ids, row.Node3);
                AddNode(ids, row.Node4);
                AddNode(ids, row.Node5);
                AddNode(ids, row.Node6);
                AddNode(ids, row.Node7);
                AddNode(ids, row.Node8);
            }
            return ids.Count;
        }
    }

    public int ActiveElementCount =>
        Mesh.MeshInformationRows.Count(r =>
            string.Equals(r.Status, "Active", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(r.Status));
    public int InactiveElementCount => ElementCount - ActiveElementCount;
    public bool HasMesh => ElementCount > 0;
    public string ElementTypesLabel
    {
        get
        {
            var kinds = Mesh.MeshInformationRows
                .Select(ResolveKind)
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return kinds.Count == 0 ? "—" : string.Join(" / ", kinds);
        }
    }

    public string QualityGrade => Mesh.MeshQualityGrade;
    public string QualityGradeColor => Mesh.MeshQualityGradeColor;
    public string QualityMinAngleText => FormatQuality(Mesh.MeshQualityMinAngle);
    public string QualityAspectText => FormatQuality(Mesh.MeshQualityMaxAspectRatio);
    public string QualitySkewText => FormatQuality(Mesh.MeshQualityMaxSkew);
    public string QualityJacobianText => FormatQuality(Mesh.MeshQualityMinJacobian);

    public string StatusReady
    {
        get
        {
            var status = Mesh.SurfaceMeshStatus;
            return string.IsNullOrWhiteSpace(status) ? "Ready" : status;
        }
    }
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
        Roots.Clear();
        var root = new ProjectExplorerNode
        {
            Kind = ExplorerNodeKind.Mesh,
            Title = "Mesh",
            Glyph = GlyphFolder,
            TabIndex = 3,
            IsExpanded = true
        };

        if (MatchesFilter("Global Mesh"))
        {
            root.Children.Add(new ProjectExplorerNode
            {
                Kind = ExplorerNodeKind.Mesh,
                Title = $"Global Mesh ({GeneratorLabel})",
                Glyph = GlyphMesh,
                TabIndex = 3,
                Payload = PayloadGlobal
            });
        }

        var byEtype = Mesh.MeshInformationRows
            .GroupBy(r => r.Etype)
            .OrderBy(g => g.Key)
            .ToList();
        var etypeFolder = Folder("Elements by Etype", GlyphElement, byEtype.Count, expanded: true);
        foreach (var group in byEtype)
        {
            var title = $"Etype {group.Key}";
            if (!MatchesFilter(title)) continue;
            etypeFolder.Children.Add(new ProjectExplorerNode
            {
                Kind = ExplorerNodeKind.Mesh,
                Title = title,
                Glyph = GlyphElement,
                TabIndex = 3,
                Count = group.Count(),
                Payload = $"etype:{group.Key}"
            });
        }
        root.Children.Add(etypeFolder);

        var byKind = Mesh.MeshInformationRows
            .GroupBy(ResolveKind)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var kindFolder = Folder("Elements by Kind", GlyphElement, byKind.Count);
        foreach (var group in byKind)
        {
            var title = string.IsNullOrWhiteSpace(group.Key) ? "Unknown" : group.Key;
            if (!MatchesFilter(title)) continue;
            kindFolder.Children.Add(new ProjectExplorerNode
            {
                Kind = ExplorerNodeKind.Mesh,
                Title = title,
                Glyph = GlyphElement,
                TabIndex = 3,
                Count = group.Count(),
                Payload = $"kind:{title}"
            });
        }
        root.Children.Add(kindFolder);

        var inactive = Mesh.MeshInformationRows
            .Where(r => !string.IsNullOrWhiteSpace(r.Status)
                        && !string.Equals(r.Status, "Active", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var diag = Folder("Diagnostics", GlyphDiag, inactive.Count, payload: PayloadDiagnostics);
        foreach (var row in inactive.Take(80))
        {
            var title = $"Element {row.ElementId} ({row.Status})";
            if (!MatchesFilter(title)) continue;
            diag.Children.Add(new ProjectExplorerNode
            {
                Kind = ExplorerNodeKind.Mesh,
                Title = title,
                Glyph = GlyphDiag,
                TabIndex = 3,
                Payload = row
            });
        }
        root.Children.Add(diag);

        Roots.Add(root);
        NotifyStats();
    }

    private void OnSelect(ProjectExplorerNode? node)
    {
        if (node == null) return;
        _host.SelectedTabIndex = 3;
        if (node.Payload is MeshInformationRow row)
            Mesh.SelectedMeshInformationRow = row;
    }

    private void OnGenerate()
    {
        if (Is1DGenerator || Mesh.Is1DProblem)
        {
            Mesh.Generate1DMeshCommand.Execute(null);
            return;
        }

        if (IsStructuredGenerator)
        {
            Mesh.UserDefinedMeshVM.GenerateHybridMeshCommand.Execute(null);
            // Hybrid needs a locked surface; if it still produced nothing, use the
            // unstructured generator (same as Discretize) so Generate is never a no-op.
            if (Mesh.MeshInformationRows.Count == 0)
                Mesh.Rs2dMeshCommand.Execute(null);
        }
        else
        {
            // Always invoke ExecuteRs2dMesh (auto-discretizes when needed). Calling through
            // CanExecute would leave the green Generate button looking clickable but inert.
            Mesh.Rs2dMeshCommand.Execute(null);
        }

        if (Mesh.MeshInformationRows.Count > 0)
        {
            Mesh.ShowMeshElements = true;
            IsTableExpanded = true;
        }
        OnPropertyChanged(nameof(StatusReady));
        NotifyStats();
    }

    private void OnDiscretize()
    {
        if (IsStructuredGenerator)
            Mesh.UserDefinedMeshVM.DiscretizeCommand.Execute(null);
        else
            Mesh.Rs2dDiscretizeCommand.Execute(null);
    }

    private void OnClear()
    {
        Mesh.ClearMeshCommand.Execute(null);
        NotifyStats();
        OnPropertyChanged(nameof(StatusReady));
    }

    private void OnMeshPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MeshViewModel.MeshPopupSelectedTabIndex)
            or nameof(MeshViewModel.Rs2dElementType)
            or nameof(MeshViewModel.Is1DProblem))
        {
            OnPropertyChanged(nameof(IsUnstructuredGenerator));
            OnPropertyChanged(nameof(IsStructuredGenerator));
            OnPropertyChanged(nameof(Is1DGenerator));
            OnPropertyChanged(nameof(GeneratorLabel));
            OnPropertyChanged(nameof(ElementFamilyLabel));
            Refresh();
            return;
        }

        if (e.PropertyName is nameof(MeshViewModel.MeshQualityGrade)
            or nameof(MeshViewModel.MeshQualityGradeColor)
            or nameof(MeshViewModel.MeshQualityMinAngle)
            or nameof(MeshViewModel.MeshQualityMaxAspectRatio)
            or nameof(MeshViewModel.MeshQualityMaxSkew)
            or nameof(MeshViewModel.MeshQualityMinJacobian)
            or nameof(MeshViewModel.SurfaceMeshNodeCount)
            or nameof(MeshViewModel.SurfaceMeshElementCount)
            or nameof(MeshViewModel.SurfaceMeshStatus))
        {
            NotifyStats();
            OnPropertyChanged(nameof(StatusReady));
        }
    }

    private void Hook(INotifyCollectionChanged collection)
    {
        collection.CollectionChanged += (_, _) => Refresh();
    }

    private void NotifyStats()
    {
        OnPropertyChanged(nameof(ElementCount));
        OnPropertyChanged(nameof(NodeCount));
        OnPropertyChanged(nameof(ActiveElementCount));
        OnPropertyChanged(nameof(InactiveElementCount));
        OnPropertyChanged(nameof(HasMesh));
        OnPropertyChanged(nameof(ElementTypesLabel));
        OnPropertyChanged(nameof(ElementFamilyLabel));
        OnPropertyChanged(nameof(QualityGrade));
        OnPropertyChanged(nameof(QualityGradeColor));
        OnPropertyChanged(nameof(QualityMinAngleText));
        OnPropertyChanged(nameof(QualityAspectText));
        OnPropertyChanged(nameof(QualitySkewText));
        OnPropertyChanged(nameof(QualityJacobianText));
    }

    private ProjectExplorerNode Folder(string title, string glyph, int count, bool expanded = false, object? payload = null)
    {
        return new ProjectExplorerNode
        {
            Kind = ExplorerNodeKind.Folder,
            Title = title,
            Glyph = glyph,
            TabIndex = 3,
            Count = count,
            IsExpanded = expanded,
            Payload = payload
        };
    }

    private bool MatchesFilter(string title) =>
        string.IsNullOrWhiteSpace(_filterText)
        || title.Contains(_filterText, StringComparison.OrdinalIgnoreCase);

    private static string ResolveKind(MeshInformationRow row)
    {
        if (!string.IsNullOrWhiteSpace(row.Kind))
            return row.Kind;
        if (row.IsQ8) return "Q8";
        if (row.IsQ4) return "Q4";
        if (row.IsT6) return "T6";
        if (row.IsT3) return "T3";
        return "Unknown";
    }

    private static void AddNode(HashSet<int> ids, int id)
    {
        if (id > 0)
            ids.Add(id);
    }

    private string FormatQuality(double value) =>
        !HasMesh || value <= 0 ? "—" : value.ToString("0.###");

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
