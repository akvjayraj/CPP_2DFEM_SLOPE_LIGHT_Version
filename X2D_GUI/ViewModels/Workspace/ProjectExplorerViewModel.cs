using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Models.Geometry;

namespace GeotechSlopeApp.ViewModels.Workspace;

public enum ExplorerNodeKind
{
    Project,
    Folder,
    Geometry,
    MaterialGroup,
    Material,
    FeatureGroup,
    GeologicalFeature,
    Mesh,
    Refinement,
    BoundaryCondition,
    GroundwaterItem,
    LoadGroup,
    Load,
    Stage,
    AnalysisStage,
    ResultsGroup,
    LastRun
}

public sealed class ProjectExplorerNode : INotifyPropertyChanged
{
    private bool _isExpanded;

    public ExplorerNodeKind Kind { get; init; }
    public string Title { get; init; } = "";
    public int? Count { get; set; }
    public string Glyph { get; init; } = "\uE8F1";
    public string GlyphColor { get; init; } = "#FFFFFFFF";
    public int TabIndex { get; init; }
    public object? Payload { get; init; }
    public ObservableCollection<ProjectExplorerNode> Children { get; } = new();

    public string CountLabel => Count is { } n ? $" ({n})" : "";

    public bool ShowAssignButton => Kind == ExplorerNodeKind.Material;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Screenshot-style explorer: grouped adapters over existing view-models. Counts are honest.
/// </summary>
public sealed class ProjectExplorerViewModel : INotifyPropertyChanged
{
    public const string GlyphProject = "\uE8F1";
    public const string GlyphFolder = "\uE8B7";
    public const string GlyphMaterials = "\uE790";
    public const string GlyphLine = "\uED63";
    public const string GlyphSurface = "\uE7C9";
    public const string GlyphMesh = "\uE80A";
    public const string GlyphRefine = "\uF0E2";
    public const string GlyphBc = "\uE890";
    public const string GlyphNodalLoad = "\uE8FD";
    public const string GlyphLineLoad = "\uE8B3";
    public const string GlyphSurfaceLoad = "\uE8B3";
    public const string GlyphStages = "\uE8EC";
    public const string GlyphAnalysis = "\uE9D9";
    public const string GlyphAnalysisStep = "\uE8EF";
    public const string GlyphResults = "\uE9F9";
    public const string GlyphChart = "\uE9D2";
    public const string GlyphTable = "\uE8A1";

    public const string ColorSoil = "#FFF59E0B";
    public const string ColorRock = "#FFA855F7";
    public const string ColorStructural = "#FF60A5FA";

    private readonly MainWindowViewModel _host;
    private AnalysisWorkspaceViewModel? _workspace;
    private ProjectExplorerNode? _selectedNode;
    private string _searchText = "";
    private ProjectExplorerNode? _fullRoot;

    public ProjectExplorerViewModel(MainWindowViewModel host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        Roots = new ObservableCollection<ProjectExplorerNode>();
        SelectNodeCommand = new RelayCommand<ProjectExplorerNode>(OnSelect);
        Hook(_host.GeometryDefinitionVM.GeometryVertexRows);
        Hook(_host.GeometryDefinitionVM.GeometryLineSegments);
        Hook(_host.GeometryDefinitionVM.GeometrySurfaces);
        Hook(_host.GeometryDefinitionVM.GeologicalFeatures);
        Hook(_host.MaterialPropertiesVM.Materials);
        Hook(_host.MeshVM.MeshInformationRows);
        Hook(_host.LoadConditionsVM.Loads);
        Hook(_host.BoundaryConditionsVM.BoundaryConditions);
        Hook(_host.StagedConstructionVM.Stages);
        Hook(_host.FEAnalysisVM.StageDefinitions);
        _host.MeshVM.PropertyChanged += OnMeshPropertyChanged;
        _host.ResultsVM.PropertyChanged += OnResultsPropertyChanged;
        _host.ProjectSettingsVM.Project.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProjectSettingsModel.ProjectName))
                Refresh();
        };
        _host.MaterialPropertiesVM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MaterialPropertiesViewModel.IsSrfAnalysisMode)
                || e.PropertyName == nameof(MaterialPropertiesViewModel.SelectedAnalysisCategory))
                Refresh();
        };
        Refresh();
    }

    public void BindWorkspace(AnalysisWorkspaceViewModel workspace)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        Refresh();
    }

    /// <summary>ANALYSIS ribbon shows only the Analysis folder under the project root.</summary>
    public bool IsAnalysisOnlyExplorer =>
        _host.UseAnalysisWorkspace && _workspace is { IsAnalysisRibbon: true };

    public ObservableCollection<ProjectExplorerNode> Roots { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            var next = value ?? "";
            if (_searchText == next) return;
            _searchText = next;
            OnPropertyChanged();
            ApplySearch();
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

    public ICommand SelectNodeCommand { get; }

    public static string ClassifyMaterial(FailureCriterion criterion) => criterion switch
    {
        FailureCriterion.MohrCoulomb or FailureCriterion.DruckerPrager or FailureCriterion.ElastoPlastic
            => "Soil",
        FailureCriterion.HoekBrown or FailureCriterion.GeneralizedHoekBrown
            => "Rock",
        _ => "Structural"
    };

    public void Refresh()
    {
        var expanded = CaptureExpanded(_fullRoot);
        _fullRoot = BuildTree();
        RestoreExpanded(_fullRoot, expanded);
        ApplySearch();
    }

    private ProjectExplorerNode BuildTree()
    {
        var root = Node(ExplorerNodeKind.Project, ResolveProjectTitle(), GlyphProject, 0, expanded: true);
        var analysisOnly = IsAnalysisOnlyExplorer;

        if (!analysisOnly)
        {
        var geometry = Node(ExplorerNodeKind.Geometry, "Geometry", GlyphFolder, 1, expanded: false);
        root.Children.Add(geometry);

        var materials = Node(ExplorerNodeKind.Folder, "Materials", GlyphMaterials, 2, expanded: true);
        var soil = Node(ExplorerNodeKind.MaterialGroup, "Soil", GlyphMaterials, 2, ColorSoil, expanded: true);
        var rock = Node(ExplorerNodeKind.MaterialGroup, "Rock", GlyphMaterials, 2, ColorRock, expanded: true);
        var structural = Node(ExplorerNodeKind.MaterialGroup, "Structural", GlyphMaterials, 2, ColorStructural, expanded: true);
        foreach (var mat in _host.MaterialPropertiesVM.Materials)
        {
            var group = ClassifyMaterial(mat.FailureCriterion) switch
            {
                "Soil" => soil,
                "Rock" => rock,
                _ => structural
            };
            group.Children.Add(Node(
                ExplorerNodeKind.Material,
                string.IsNullOrWhiteSpace(mat.Name) ? $"Material {mat.Id}" : mat.Name,
                GlyphMaterials,
                2,
                group.GlyphColor,
                payload: mat));
        }
        SetCount(soil, soil.Children.Count);
        SetCount(rock, rock.Children.Count);
        SetCount(structural, structural.Children.Count);
        materials.Children.Add(soil);
        materials.Children.Add(rock);
        materials.Children.Add(structural);
        root.Children.Add(materials);

        var features = Node(ExplorerNodeKind.Folder, "Features", GlyphFolder, 1, expanded: true);
        var lines = Node(ExplorerNodeKind.FeatureGroup, "Line", GlyphLine, 1, expanded: false);
        foreach (var seg in _host.GeometryDefinitionVM.GeometryLineSegments.Where(s => !s.IsCircle))
            lines.Children.Add(Node(ExplorerNodeKind.FeatureGroup, seg.LineNumberLabel, GlyphLine, 1, payload: seg));
        foreach (var feature in _host.GeometryDefinitionVM.GeologicalFeatures)
        {
            lines.Children.Add(Node(
                ExplorerNodeKind.GeologicalFeature,
                string.IsNullOrWhiteSpace(feature.Name) ? feature.Kind.ToString() : feature.Name,
                GlyphLine,
                1,
                payload: feature));
        }
        SetCount(lines, lines.Children.Count);

        var surfaces = Node(ExplorerNodeKind.FeatureGroup, "Surface", GlyphSurface, 1, expanded: false);
        foreach (var surface in _host.GeometryDefinitionVM.GeometrySurfaces)
        {
            surfaces.Children.Add(Node(
                ExplorerNodeKind.FeatureGroup,
                string.IsNullOrWhiteSpace(surface.SurfaceName) ? $"Surface {surface.SurfaceNumber}" : surface.SurfaceName,
                GlyphSurface,
                1,
                payload: surface));
        }
        SetCount(surfaces, surfaces.Children.Count);
        features.Children.Add(lines);
        features.Children.Add(surfaces);
        root.Children.Add(features);

        var mesh = Node(ExplorerNodeKind.Folder, "Mesh", GlyphMesh, 3, expanded: true);
        mesh.Children.Add(Node(ExplorerNodeKind.Mesh, "Global Mesh", GlyphMesh, 3));
        var refineCount = CountEnabledRefinements();
        mesh.Children.Add(Node(ExplorerNodeKind.Refinement, "Refinement", GlyphRefine, 3, count: refineCount));
        root.Children.Add(mesh);

        var bcs = Node(ExplorerNodeKind.Folder, "Boundary Conditions", GlyphBc, 5, expanded: false);
        foreach (var bc in _host.BoundaryConditionsVM.BoundaryConditions)
        {
            var label = string.IsNullOrWhiteSpace(bc.BoundaryName)
                ? $"{bc.BCType} N{bc.NodeId}"
                : bc.BoundaryName;
            bcs.Children.Add(Node(ExplorerNodeKind.BoundaryCondition, label, GlyphBc, 5, payload: bc));
        }
        root.Children.Add(bcs);

        var loads = Node(ExplorerNodeKind.Folder, "Loads", GlyphFolder, 4, expanded: true);
        AddLoadGroup(loads, "Nodal Load", LoadType.Point, GlyphNodalLoad);
        AddLoadGroup(loads, "Line Load", LoadType.Line, GlyphLineLoad);
        AddLoadGroup(loads, "Surface Load", LoadType.Triangular, GlyphSurfaceLoad);
        root.Children.Add(loads);

        var staged = Node(
            ExplorerNodeKind.Folder,
            "Staged Construction",
            GlyphStages,
            7,
            count: _host.StagedConstructionVM.Stages.Count,
            expanded: false);
        foreach (var stage in _host.StagedConstructionVM.Stages)
        {
            staged.Children.Add(Node(
                ExplorerNodeKind.Stage,
                string.IsNullOrWhiteSpace(stage.StageName) ? $"Stage {stage.StageNumber}" : stage.StageName,
                GlyphStages,
                7,
                payload: stage));
        }
        root.Children.Add(staged);
        }

        var analysis = Node(ExplorerNodeKind.Folder, "Analysis", GlyphAnalysis, 7, expanded: true);
        analysis.Children.Add(Node(
            ExplorerNodeKind.Folder,
            "Analysis Settings",
            GlyphAnalysis,
            7,
            payload: AnalysisWorkspaceViewModel.PayloadAnalysisSettings));
        analysis.Children.Add(Node(
            ExplorerNodeKind.Folder,
            "Solver Settings",
            GlyphAnalysis,
            7,
            payload: AnalysisWorkspaceViewModel.PayloadSolverSettings));

        var analysisItems = _host.FEAnalysisVM.StageDefinitions.Count > 0
            ? _host.FEAnalysisVM.StageDefinitions.Select((s, i) => (
                Index: s.StageNumber > 0 ? s.StageNumber : i + 1,
                Name: string.IsNullOrWhiteSpace(s.StageName) ? s.StageType : s.StageName,
                Payload: (object)s)).ToList()
            : _host.StagedConstructionVM.Stages.Select((s, i) => (
                Index: s.StageNumber > 0 ? s.StageNumber : i + 1,
                Name: string.IsNullOrWhiteSpace(s.StageName) ? s.StageType : s.StageName,
                Payload: (object)s)).ToList();

        var stages = Node(
            ExplorerNodeKind.Folder,
            "Stages",
            GlyphStages,
            7,
            count: analysisItems.Count,
            expanded: true,
            payload: AnalysisWorkspaceViewModel.PayloadAnalysisStages);

        foreach (var item in analysisItems)
        {
            stages.Children.Add(Node(
                ExplorerNodeKind.AnalysisStage,
                $"{item.Index} - {item.Name}",
                GlyphAnalysisStep,
                7,
                payload: item.Payload));
        }

        if (_host.MaterialPropertiesVM.IsSrfAnalysisMode
            && stages.Children.All(c =>
                !c.Title.Contains("SSR", StringComparison.OrdinalIgnoreCase)
                && !c.Title.Contains("SRF", StringComparison.OrdinalIgnoreCase)))
        {
            stages.Children.Add(Node(
                ExplorerNodeKind.AnalysisStage,
                "Final Stage (SSR)",
                GlyphAnalysisStep,
                7));
            SetCount(stages, stages.Children.Count);
        }
        analysis.Children.Add(stages);
        root.Children.Add(analysis);

        if (analysisOnly)
            return root;

        var results = Node(ExplorerNodeKind.Folder, "Results", GlyphResults, 9, expanded: true);
        var resultSetCount = _host.ResultsVM.AvailableStages.Count;
        if (resultSetCount == 0 && _host.ResultsVM.LastP610Run != null)
            resultSetCount = 1;
        results.Children.Add(Node(ExplorerNodeKind.ResultsGroup, "Result Sets", GlyphMesh, 9, count: resultSetCount));
        var chartCount = 0;
        if (_host.ResultsVM.SrfConvergencePlotModel != null) chartCount++;
        if (_host.ResultsVM.TransientTimeHistoryPlotModel != null) chartCount++;
        results.Children.Add(Node(ExplorerNodeKind.ResultsGroup, "Charts", GlyphChart, 9, count: chartCount));
        var tableCount = _host.ResultsVM.SrfTrialRows.Count;
        results.Children.Add(Node(ExplorerNodeKind.ResultsGroup, "Table", GlyphTable, 9, count: tableCount));
        if (_host.ResultsVM.LastP610Run != null)
        {
            results.Children.Add(Node(
                ExplorerNodeKind.LastRun,
                "Last FE run",
                GlyphResults,
                9,
                payload: _host.ResultsVM.LastP610Run));
        }
        root.Children.Add(results);

        return root;
    }

    private void AddLoadGroup(ProjectExplorerNode parent, string title, LoadType type, string glyph)
    {
        var group = Node(ExplorerNodeKind.LoadGroup, title, glyph, 4, expanded: false);
        foreach (var load in _host.LoadConditionsVM.Loads.Where(l => l.Type == type))
        {
            var name = string.IsNullOrWhiteSpace(load.Name) ? type.ToString() : load.Name;
            if (!load.IsEnabled) name += " (off)";
            group.Children.Add(Node(ExplorerNodeKind.Load, name, glyph, 4, payload: load));
        }
        SetCount(group, group.Children.Count);
        parent.Children.Add(group);
    }

    private int CountEnabledRefinements()
    {
        var mesh = _host.MeshVM;
        var n = 0;
        if (mesh.RefineNearExcavation) n++;
        if (mesh.RefineNearLoads) n++;
        if (mesh.RefineNearMaterialInterfaces) n++;
        if (mesh.RefineNearStiffnessContrast) n++;
        return n;
    }

    private string ResolveProjectTitle()
    {
        var named = _host.ProjectSettingsVM.Project.ProjectName?.Trim();
        var path = _host.CurrentXfemProjectPath;
        if (string.IsNullOrWhiteSpace(named))
            named = string.IsNullOrWhiteSpace(path) ? "" : Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(named))
            named = "Project 1";
        if (!named.EndsWith(".xfem", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(path))
            named += ".xfem";
        return named;
    }

    private void ApplySearch()
    {
        Roots.Clear();
        if (_fullRoot == null)
            return;
        if (string.IsNullOrWhiteSpace(_searchText))
        {
            Roots.Add(_fullRoot);
            return;
        }

        var filtered = FilterNode(_fullRoot, _searchText.Trim());
        if (filtered != null)
            Roots.Add(filtered);
    }

    private static ProjectExplorerNode? FilterNode(ProjectExplorerNode source, string query)
    {
        var childMatches = source.Children
            .Select(c => FilterNode(c, query))
            .Where(c => c != null)
            .Cast<ProjectExplorerNode>()
            .ToList();
        var selfMatch = source.Title.Contains(query, StringComparison.OrdinalIgnoreCase);
        if (!selfMatch && childMatches.Count == 0)
            return null;

        var copy = CloneShallow(source);
        copy.IsExpanded = true;
        foreach (var child in childMatches)
            copy.Children.Add(child);
        return copy;
    }

    private static ProjectExplorerNode CloneShallow(ProjectExplorerNode source) =>
        new()
        {
            Kind = source.Kind,
            Title = source.Title,
            Count = source.Count,
            Glyph = source.Glyph,
            GlyphColor = source.GlyphColor,
            TabIndex = source.TabIndex,
            Payload = source.Payload,
            IsExpanded = source.IsExpanded
        };

    private static HashSet<string> CaptureExpanded(ProjectExplorerNode? root)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        void Walk(ProjectExplorerNode? node, string path)
        {
            if (node == null) return;
            var key = path + "/" + node.Title;
            if (node.IsExpanded)
                set.Add(key);
            foreach (var child in node.Children)
                Walk(child, key);
        }
        Walk(root, "");
        return set;
    }

    private static void RestoreExpanded(ProjectExplorerNode? node, HashSet<string> expanded, string path = "")
    {
        if (node == null) return;
        var key = path + "/" + node.Title;
        if (expanded.Contains(key))
            node.IsExpanded = true;
        foreach (var child in node.Children)
            RestoreExpanded(child, expanded, key);
    }

    private static ProjectExplorerNode Node(
        ExplorerNodeKind kind,
        string title,
        string glyph,
        int tabIndex,
        string glyphColor = "",
        int? count = null,
        bool expanded = false,
        object? payload = null)
        => new()
        {
            Kind = kind,
            Title = title,
            Glyph = glyph,
            GlyphColor = glyphColor,
            TabIndex = tabIndex,
            Count = count,
            IsExpanded = expanded,
            Payload = payload
        };

    private static void SetCount(ProjectExplorerNode node, int count) => node.Count = count;

    private void OnSelect(ProjectExplorerNode? node)
    {
        if (node == null)
            return;
        if (node.TabIndex >= 0)
            _host.SelectedTabIndex = node.TabIndex;
        _host.Workspace?.ApplyExplorerPayload(node.Payload);
    }

    private void Hook(INotifyCollectionChanged collection)
    {
        collection.CollectionChanged += (_, _) => Refresh();
    }

    private void OnMeshPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MeshViewModel.RefineNearExcavation)
            or nameof(MeshViewModel.RefineNearLoads)
            or nameof(MeshViewModel.RefineNearMaterialInterfaces)
            or nameof(MeshViewModel.RefineNearStiffnessContrast)
            or nameof(MeshViewModel.IsDiscretized))
            Refresh();
    }

    private void OnResultsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ResultsViewModel.LastP610Run)
            or nameof(ResultsViewModel.AvailableStages)
            or nameof(ResultsViewModel.SrfConvergencePlotModel)
            or nameof(ResultsViewModel.TransientTimeHistoryPlotModel)
            or nameof(ResultsViewModel.SrfTrialRows))
            Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
