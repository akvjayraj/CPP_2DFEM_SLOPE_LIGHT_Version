using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.ViewModels.Workspace;

/// <summary>
/// MATERIALS-ribbon chrome: explorer, summary, and selected-material inspector.
/// Hosts existing MaterialProperties collections; does not duplicate CRUD.
/// </summary>
public sealed class MaterialsWorkspaceViewModel : INotifyPropertyChanged
{
    public const string ToolLayering = "tool-layering";
    public const string ToolProject = "tool-project";
    public const string ToolAssignment = "tool-assignment";
    public const string ToolAllMaterials = "tool-all-materials";

    public const string GroupSoil = "group-soil";
    public const string GroupRock = "group-rock";
    public const string GroupStructural = "group-structural";
    public const string GroupInterface = "group-interface";

    public const string GlyphFolder = "\uE8B7";
    public const string GlyphMaterials = "\uE790";
    public const string GlyphTools = "\uE90F";
    public const string GlyphChevronExpanded = "\uE70D";
    public const string GlyphChevronCollapsed = "\uE70E";

    public const string ColorSoil = ProjectExplorerViewModel.ColorSoil;
    public const string ColorRock = ProjectExplorerViewModel.ColorRock;
    public const string ColorStructural = ProjectExplorerViewModel.ColorStructural;
    public const string ColorInterface = "#FF34D399";

    private readonly MainWindowViewModel _host;
    private ProjectExplorerNode? _selectedNode;
    private string _filterText = "";
    private bool _showUnassignedOnly;
    private string? _selectedMaterialGroup;
    private bool _isExplorerExpanded = true;
    private bool _isTableExpanded = true;
    private bool _isInspectorExpanded = true;
    private bool _isViewOptionsExpanded = true;
    private bool _showLayerLabels = true;
    private bool _showInternalAndLayerCutLines = true;

    public MaterialsWorkspaceViewModel(MainWindowViewModel host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        Roots = new ObservableCollection<ProjectExplorerNode>();
        SelectNodeCommand = new RelayCommand<ProjectExplorerNode>(OnSelect);
        ShowMaterialGroupsCommand = new RelayCommand(ShowMaterialGroups);
        ShowUnassignedCommand = new RelayCommand(ShowUnassigned);
        AssignMaterialsCommand = new RelayCommand(() =>
        {
            Materials.SelectedMaterialSubtabIndex = MaterialPropertiesViewModel.LayerMaterialAssignmentSubtabIndex;
            _host.SelectedTabIndex = 2;
            _host.AssignMaterialToRegionCommand.Execute(null);
        });
        RefreshCommand = new RelayCommand(Refresh);
        ValidateMaterialsCommand = new RelayCommand(() =>
            _host.ModelHealthVM.RefreshCommand.Execute(null));
        ToggleExplorerCommand = new RelayCommand(() => IsExplorerExpanded = !IsExplorerExpanded);
        ToggleTableCommand = new RelayCommand(() => IsTableExpanded = !IsTableExpanded);
        ToggleInspectorCommand = new RelayCommand(() => IsInspectorExpanded = !IsInspectorExpanded);
        ToggleViewOptionsCommand = new RelayCommand(() => IsViewOptionsExpanded = !IsViewOptionsExpanded);

        HookMaterials(_host.MaterialPropertiesVM.Materials);
        Hook(_host.MaterialPropertiesVM.LayerAssignments);
        _host.MaterialPropertiesVM.PropertyChanged += OnMaterialsPropertyChanged;
        _host.GeometryDefinitionVM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GeometryDefinitionViewModel.GeometryCanvasCoordinates))
                OnPropertyChanged(nameof(StatusCursor));
        };
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
    public ICommand ShowMaterialGroupsCommand { get; }
    public ICommand ShowUnassignedCommand { get; }
    public ICommand AssignMaterialsCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ValidateMaterialsCommand { get; }
    public ICommand ToggleExplorerCommand { get; }
    public ICommand ToggleTableCommand { get; }
    public ICommand ToggleInspectorCommand { get; }
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

    public bool IsTableExpanded
    {
        get => _isTableExpanded;
        set
        {
            if (_isTableExpanded == value) return;
            _isTableExpanded = value;
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

    public bool ShowLayerLabels
    {
        get => _showLayerLabels;
        set
        {
            if (_showLayerLabels == value) return;
            _showLayerLabels = value;
            OnPropertyChanged();
            _host.ApplyGeometryCanvasDecorationsForTab(_host.SelectedTabIndex);
        }
    }

    public bool ShowInternalAndLayerCutLines
    {
        get => _showInternalAndLayerCutLines;
        set
        {
            if (_showInternalAndLayerCutLines == value) return;
            _showInternalAndLayerCutLines = value;
            OnPropertyChanged();
            _host.ApplyGeometryCanvasDecorationsForTab(_host.SelectedTabIndex);
        }
    }

    public string ExplorerChevronGlyph => IsExplorerExpanded ? GlyphChevronExpanded : GlyphChevronCollapsed;

    public MaterialPropertiesViewModel Materials => _host.MaterialPropertiesVM;
    public MaterialModel? SelectedMaterial => Materials.SelectedMaterialModel;

    public string? SelectedMaterialGroup
    {
        get => _selectedMaterialGroup;
        private set
        {
            if (_selectedMaterialGroup == value) return;
            _selectedMaterialGroup = value;
            OnPropertyChanged();
            Materials.NewMaterialFailureCriterion = CriterionForGroup(value);
        }
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            var next = value ?? "";
            if (_filterText == next) return;
            _filterText = next;
            OnPropertyChanged();
            RefreshPreservingSelection();
        }
    }

    public bool ShowUnassignedOnly
    {
        get => _showUnassignedOnly;
        set
        {
            if (_showUnassignedOnly == value) return;
            _showUnassignedOnly = value;
            OnPropertyChanged();
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

    public int TotalCount => Materials.Materials.Count;
    public int SoilCount => Materials.Materials.Count(m =>
        ProjectExplorerViewModel.ClassifyMaterial(m.FailureCriterion) == "Soil");
    public int RockCount => Materials.Materials.Count(m =>
        ProjectExplorerViewModel.ClassifyMaterial(m.FailureCriterion) == "Rock");
    public int StructuralCount => Materials.Materials.Count(m =>
        ProjectExplorerViewModel.ClassifyMaterial(m.FailureCriterion) == "Structural");
    public int InterfaceCount => 0;
    public int UnassignedCount => Materials.LayerAssignments.Count(r => r.AssignedMaterial == null);

    public string SelectedName => SelectedMaterial?.Name ?? "—";
    public string SelectedId => SelectedMaterial == null ? "—" : SelectedMaterial.Id.ToString();
    public string SelectedGroup => SelectedMaterial == null
        ? "—"
        : ProjectExplorerViewModel.ClassifyMaterial(SelectedMaterial.FailureCriterion);
    public string SelectedCriterion => SelectedMaterial?.FailureCriterion.ToString() ?? "—";
    public string SelectedType => SelectedMaterial?.MaterialType.ToString() ?? "—";
    public string SelectedUnitWeight => SelectedMaterial == null ? "—" : SelectedMaterial.UnitWeight.ToString("0.###");
    public string SelectedYoungsModulus => SelectedMaterial == null ? "—" : SelectedMaterial.YoungsModulus.ToString("0.###");
    public string SelectedPoissonsRatio => SelectedMaterial == null ? "—" : SelectedMaterial.PoissonsRatio.ToString("0.###");
    public string SelectedCohesion => SelectedMaterial == null ? "—" : SelectedMaterial.Cohesion.ToString("0.###");
    public string SelectedFrictionAngle => SelectedMaterial == null ? "—" : SelectedMaterial.FrictionAngle.ToString("0.###");

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
        Roots.Clear();
        var tools = Folder("Tools", GlyphTools, count: 3, expanded: true);
        tools.Children.Add(Leaf("Layering controls", GlyphTools, ToolLayering));
        tools.Children.Add(Leaf("Project materials", GlyphMaterials, ToolProject));
        tools.Children.Add(Leaf("Layer material assignment", GlyphFolder, ToolAssignment));
        Roots.Add(tools);

        var all = Folder("All Materials", GlyphMaterials, TotalCount, expanded: true, payload: ToolAllMaterials);
        var soil = Folder("Soil", GlyphMaterials, SoilCount, expanded: true, color: ColorSoil, payload: GroupSoil);
        var rock = Folder("Rock", GlyphMaterials, RockCount, color: ColorRock, payload: GroupRock);
        var structural = Folder("Structural", GlyphMaterials, StructuralCount, color: ColorStructural, payload: GroupStructural);
        var iface = Folder("Interface", GlyphMaterials, InterfaceCount, color: ColorInterface, payload: GroupInterface);

        foreach (var mat in Materials.Materials)
        {
            var title = string.IsNullOrWhiteSpace(mat.Name) ? $"Material {mat.Id}" : mat.Name;
            if (!MatchesFilter(title)) continue;
            var groupName = ProjectExplorerViewModel.ClassifyMaterial(mat.FailureCriterion);
            var group = groupName switch
            {
                "Soil" => soil,
                "Rock" => rock,
                _ => structural
            };
            group.Children.Add(new ProjectExplorerNode
            {
                Kind = ExplorerNodeKind.Material,
                Title = title,
                Glyph = GlyphMaterials,
                GlyphColor = group.GlyphColor,
                TabIndex = 2,
                Payload = mat
            });
        }

        soil.Count = soil.Children.Count;
        rock.Count = rock.Children.Count;
        structural.Count = structural.Children.Count;
        all.Children.Add(soil);
        all.Children.Add(rock);
        all.Children.Add(structural);
        all.Children.Add(iface);
        Roots.Add(all);
        NotifyCounts();
    }

    private void ShowMaterialGroups()
    {
        ShowUnassignedOnly = false;
        Materials.SelectedMaterialSubtabIndex = MaterialPropertiesViewModel.ProjectMaterialsSubtabIndex;
        _host.SelectedTabIndex = 2;
        if (Roots.FirstOrDefault(r => r.Title == "All Materials") is { } all)
        {
            all.IsExpanded = true;
            SelectedNode = all;
        }
    }

    private void ShowUnassigned()
    {
        ShowUnassignedOnly = true;
        Materials.SelectedMaterialSubtabIndex = MaterialPropertiesViewModel.LayerMaterialAssignmentSubtabIndex;
        _host.SelectedTabIndex = 2;
    }

    private void OnSelect(ProjectExplorerNode? node)
    {
        if (node == null) return;
        _host.SelectedTabIndex = 2;
        switch (node.Payload)
        {
            case string token when token == ToolLayering:
                Materials.ShowProjectMaterialsToolbar = true;
                Materials.DeleteTargetsAllMaterials = false;
                Materials.SelectedMaterialSubtabIndex = MaterialPropertiesViewModel.LayeringControlsSubtabIndex;
                break;
            case string token when token == ToolProject:
                SelectedMaterialGroup = null;
                Materials.ShowProjectMaterialsToolbar = true;
                Materials.DeleteTargetsAllMaterials = true;
                Materials.SelectedMaterialModel = null;
                Materials.SelectedMaterialSubtabIndex = MaterialPropertiesViewModel.ProjectMaterialsSubtabIndex;
                break;
            case string token when token == ToolAllMaterials:
                SelectedMaterialGroup = null;
                Materials.ShowProjectMaterialsToolbar = false;
                Materials.DeleteTargetsAllMaterials = true;
                Materials.SelectedMaterialModel = null;
                Materials.SelectedMaterialSubtabIndex = MaterialPropertiesViewModel.ProjectMaterialsSubtabIndex;
                break;
            case string token when token == GroupSoil:
                SelectMaterialGroup("Soil");
                break;
            case string token when token == GroupRock:
                SelectMaterialGroup("Rock");
                break;
            case string token when token == GroupStructural:
                SelectMaterialGroup("Structural");
                break;
            case string token when token == GroupInterface:
                SelectMaterialGroup("Interface");
                break;
            case string token when token == ToolAssignment:
                Materials.ShowProjectMaterialsToolbar = true;
                Materials.DeleteTargetsAllMaterials = false;
                Materials.SelectedMaterialSubtabIndex = MaterialPropertiesViewModel.LayerMaterialAssignmentSubtabIndex;
                break;
            case MaterialModel material:
                SelectedMaterialGroup = ProjectExplorerViewModel.ClassifyMaterial(material.FailureCriterion);
                Materials.ShowProjectMaterialsToolbar = false;
                Materials.DeleteTargetsAllMaterials = false;
                Materials.SelectedMaterialModel = material;
                Materials.SelectedMaterialSubtabIndex = MaterialPropertiesViewModel.ProjectMaterialsSubtabIndex;
                NotifySelected();
                break;
        }
    }

    private void SelectMaterialGroup(string group)
    {
        SelectedMaterialGroup = group;
        Materials.ShowProjectMaterialsToolbar = false;
        Materials.DeleteTargetsAllMaterials = false;
        Materials.SelectedMaterialModel = null;
        Materials.SelectedMaterialSubtabIndex = MaterialPropertiesViewModel.ProjectMaterialsSubtabIndex;
    }

    public static FailureCriterion CriterionForGroup(string? group) => group switch
    {
        "Soil" => FailureCriterion.MohrCoulomb,
        "Rock" => FailureCriterion.HoekBrown,
        _ => FailureCriterion.Elastic
    };

    private void Hook(INotifyCollectionChanged collection) =>
        collection.CollectionChanged += (_, _) => RefreshPreservingSelection();

    private void HookMaterials(ObservableCollection<MaterialModel> materials)
    {
        foreach (var material in materials)
            material.PropertyChanged += OnMaterialModelPropertyChanged;
        materials.CollectionChanged += OnMaterialsCollectionChanged;
    }

    private void OnMaterialsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is MaterialModel removed)
                    removed.PropertyChanged -= OnMaterialModelPropertyChanged;
            }
        }

        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is MaterialModel added)
                    added.PropertyChanged += OnMaterialModelPropertyChanged;
            }
        }

        RefreshPreservingSelection();
    }

    private void OnMaterialModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MaterialModel.Name) or nameof(MaterialModel.FailureCriterion))
            RefreshPreservingSelection();
    }

    public void RefreshPreservingSelection()
    {
        var materialId = SelectedNode?.Payload is MaterialModel selected
            ? selected.Id
            : Materials.SelectedMaterialModel?.Id;
        var token = SelectedNode?.Payload as string;
        Refresh();
        if (materialId is int id && FindMaterialNode(id) is { } leaf)
            SelectedNode = leaf;
        else if (token != null && FindNodeByPayload(token) is { } folder)
            SelectedNode = folder;
    }

    private ProjectExplorerNode? FindMaterialNode(int id)
    {
        var all = Roots.FirstOrDefault(r => r.Title == "All Materials");
        if (all == null) return null;
        foreach (var group in all.Children)
        {
            foreach (var child in group.Children)
            {
                if (child.Payload is MaterialModel material && material.Id == id)
                    return child;
            }
        }

        return null;
    }

    private ProjectExplorerNode? FindNodeByPayload(string token)
    {
        foreach (var root in Roots)
        {
            if (root.Payload is string rootToken && rootToken == token)
                return root;
            foreach (var child in root.Children)
            {
                if (child.Payload is string childToken && childToken == token)
                    return child;
            }
        }

        return null;
    }

    private void OnMaterialsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MaterialPropertiesViewModel.SelectedMaterialModel))
            return;
        NotifySelected();
        if (Materials.SelectedMaterialModel is { } mat && FindMaterialNode(mat.Id) is { } leaf
            && !ReferenceEquals(SelectedNode, leaf))
            SelectedNode = leaf;
    }

    private void NotifySelected()
    {
        OnPropertyChanged(nameof(SelectedMaterial));
        OnPropertyChanged(nameof(SelectedName));
        OnPropertyChanged(nameof(SelectedId));
        OnPropertyChanged(nameof(SelectedGroup));
        OnPropertyChanged(nameof(SelectedCriterion));
        OnPropertyChanged(nameof(SelectedType));
        OnPropertyChanged(nameof(SelectedUnitWeight));
        OnPropertyChanged(nameof(SelectedYoungsModulus));
        OnPropertyChanged(nameof(SelectedPoissonsRatio));
        OnPropertyChanged(nameof(SelectedCohesion));
        OnPropertyChanged(nameof(SelectedFrictionAngle));
    }

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(SoilCount));
        OnPropertyChanged(nameof(RockCount));
        OnPropertyChanged(nameof(StructuralCount));
        OnPropertyChanged(nameof(InterfaceCount));
        OnPropertyChanged(nameof(UnassignedCount));
        OnPropertyChanged(nameof(StatusLastSaved));
        OnPropertyChanged(nameof(StatusCursor));
        OnPropertyChanged(nameof(StatusDimension));
        NotifySelected();
    }

    private bool MatchesFilter(string title) =>
        string.IsNullOrWhiteSpace(_filterText)
        || title.Contains(_filterText, StringComparison.OrdinalIgnoreCase);

    private static ProjectExplorerNode Folder(
        string title, string glyph, int count, bool expanded = false, string color = "", object? payload = null) =>
        new()
        {
            Kind = ExplorerNodeKind.Folder,
            Title = title,
            Glyph = glyph,
            GlyphColor = color,
            Count = count,
            TabIndex = 2,
            IsExpanded = expanded,
            Payload = payload
        };

    private static ProjectExplorerNode Leaf(string title, string glyph, string payload) =>
        new()
        {
            Kind = ExplorerNodeKind.Folder,
            Title = title,
            Glyph = glyph,
            TabIndex = 2,
            Payload = payload
        };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
