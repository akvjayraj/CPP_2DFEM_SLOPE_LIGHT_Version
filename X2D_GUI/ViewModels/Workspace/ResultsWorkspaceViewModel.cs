using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Models.Analysis;
using GeotechSlopeApp.Models.FiniteElement;

namespace GeotechSlopeApp.ViewModels.Workspace;

public enum ResultsManagerNodeKind
{
    Root,
    ResultSet,
    Folder,
    ResultType
}

public sealed class ResultsManagerNode : INotifyPropertyChanged
{
    private bool _isExpanded = true;
    private bool _isChecked;
    private bool _isSelected;

    public ResultsManagerNodeKind Kind { get; init; }
    public string Title { get; init; } = "";
    public string? OutputType { get; init; }
    public string VisualizationMode { get; init; } = ResultsViewModel.VisualizationModeContour2D;
    public ObservableCollection<ResultsManagerNode> Children { get; } = new();

    public bool IsCheckable => Kind == ResultsManagerNodeKind.ResultType || Kind == ResultsManagerNodeKind.ResultSet;

    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded == value) return; _isExpanded = value; OnPropertyChanged(); }
    }

    public bool IsChecked
    {
        get => _isChecked;
        set { if (_isChecked == value) return; _isChecked = value; OnPropertyChanged(); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ResultsMinMaxRow
{
    public string Quantity { get; init; } = "";
    public string MinText { get; init; } = "—";
    public string MaxText { get; init; } = "—";
    public string MinLocation { get; init; } = "—";
    public string MaxLocation { get; init; } = "—";
}

/// <summary>
/// RESULTS-ribbon chrome for a completed Static analysis: manager tree, plot options, messages, min/max.
/// Hosts existing ResultsViewModel; does not invent solver fields.
/// </summary>
public sealed class ResultsWorkspaceViewModel : INotifyPropertyChanged
{
    public const string DisplayFilledContours = "Filled Contours";
    public const string DisplayVectors = "Vectors";
    public const string DisplayDeformedMesh = "Deformed Mesh";
    public const string ComponentMagnitude = "Magnitude";
    public const string ComponentX = "X";
    public const string ComponentY = "Y";
    public const string GlyphChevronExpanded = "\uE70D";
    public const string GlyphChevronCollapsed = "\uE70E";

    private readonly MainWindowViewModel _host;
    private ResultsManagerNode? _selectedNode;
    private bool _isExplorerExpanded = true;
    private bool _isPropertiesExpanded = true;
    private bool _isTableExpanded = true;
    private bool _showContourLines = true;
    private bool _showLoads = true;
    private bool _showSupports = true;
    private bool _rangeAutomatic = true;
    private int _contourCount = 20;
    private string _contourScheme = "Rainbow";
    private string _legendPosition = "Right";
    private double _transparencyPercent;
    private string _loadCaseLabel = "1 - Static";
    private int _stepIndex = 1;
    private int _stepCount = 1;

    public ResultsWorkspaceViewModel(MainWindowViewModel host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        Roots = new ObservableCollection<ResultsManagerNode>();
        MinMaxRows = new ObservableCollection<ResultsMinMaxRow>();
        DisplayModes = new[] { DisplayFilledContours, DisplayVectors, DisplayDeformedMesh };
        Components = new[] { ComponentMagnitude, ComponentX, ComponentY };
        ContourSchemes = new[] { "Rainbow", "Grayscale", "Terrain" };
        LegendPositions = new[] { "Right", "Left", "Bottom" };
        LoadCases = new ObservableCollection<string> { "1 - Static" };

        SelectNodeCommand = new RelayCommand<ResultsManagerNode>(OnSelectNode);
        ToggleExplorerCommand = new RelayCommand(() => IsExplorerExpanded = !IsExplorerExpanded);
        TogglePropertiesCommand = new RelayCommand(() => IsPropertiesExpanded = !IsPropertiesExpanded);
        ToggleTableCommand = new RelayCommand(() => IsTableExpanded = !IsTableExpanded);
        FirstStepCommand = new RelayCommand(() => StepIndex = 1);
        PreviousStepCommand = new RelayCommand(() => StepIndex = Math.Max(1, StepIndex - 1));
        NextStepCommand = new RelayCommand(() => StepIndex = Math.Min(StepCount, StepIndex + 1));
        LastStepCommand = new RelayCommand(() => StepIndex = StepCount);
        SelectContoursCommand = new RelayCommand(() => ApplyDisplay(DisplayFilledContours));
        SelectVectorsCommand = new RelayCommand(() => ApplyDisplay(DisplayVectors));
        SelectDeformationCommand = new RelayCommand(() => ApplyDisplay(DisplayDeformedMesh));

        Results.PropertyChanged += OnResultsPropertyChanged;
        RebuildTree();
        RefreshSummaries();
    }

    public ResultsViewModel Results => _host.ResultsVM;
    public ObservableCollection<ResultsManagerNode> Roots { get; }
    public ObservableCollection<ResultsMinMaxRow> MinMaxRows { get; }
    public IReadOnlyList<string> DisplayModes { get; }
    public IReadOnlyList<string> Components { get; }
    public IReadOnlyList<string> ContourSchemes { get; }
    public IReadOnlyList<string> LegendPositions { get; }
    public ObservableCollection<string> LoadCases { get; }

    public ICommand SelectNodeCommand { get; }
    public ICommand ToggleExplorerCommand { get; }
    public ICommand TogglePropertiesCommand { get; }
    public ICommand ToggleTableCommand { get; }
    public ICommand FirstStepCommand { get; }
    public ICommand PreviousStepCommand { get; }
    public ICommand NextStepCommand { get; }
    public ICommand LastStepCommand { get; }
    public ICommand SelectContoursCommand { get; }
    public ICommand SelectVectorsCommand { get; }
    public ICommand SelectDeformationCommand { get; }

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

    public string ExplorerChevronGlyph => IsExplorerExpanded ? GlyphChevronExpanded : GlyphChevronCollapsed;

    public ResultsManagerNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (ReferenceEquals(_selectedNode, value)) return;
            if (_selectedNode != null) _selectedNode.IsSelected = false;
            _selectedNode = value;
            if (_selectedNode != null) _selectedNode.IsSelected = true;
            OnPropertyChanged();
        }
    }

    public bool HasCompletedStaticResults =>
        Results.LastP610Run != null
        || Results.FENodes.Count > 0
        || Results.FeNodalDisplacements.Count > 0;

    public string RootTitle => HasCompletedStaticResults
        ? $"{AnalysisTypeLabel} (Completed)"
        : $"{AnalysisTypeLabel} (No results)";

    public string AnalysisTypeLabel => _host.ProjectSettingsVM.ResolvedAnalysisKind switch
    {
        AnalysisKind.Srf or AnalysisKind.CoupledStaticSrf => "SRF",
        AnalysisKind.GroundwaterSteady or AnalysisKind.CoupledSeepageSrf => "Seepage",
        AnalysisKind.GroundwaterTransient => "Transient",
        _ => "Static"
    };

    public string SolverTypeLabel =>
        string.Equals(_host.FEAnalysisVM.ResolvedSolverForRun, "p610_t3_elastic", StringComparison.OrdinalIgnoreCase)
            ? "Direct"
            : string.IsNullOrWhiteSpace(_host.FEAnalysisVM.ResolvedSolverForRun)
                ? "Direct"
                : _host.FEAnalysisVM.ResolvedSolverForRun;

    public int TotalStages => Math.Max(1, Results.AvailableStages.Count);
    public int ActiveStage => Math.Max(1, StepIndex);
    public string LoadCaseLabel
    {
        get => _loadCaseLabel;
        set { if (_loadCaseLabel == value) return; _loadCaseLabel = value; OnPropertyChanged(); }
    }

    public string StepLabel => $"{StepIndex} / {StepCount}";

    public int StepIndex
    {
        get => _stepIndex;
        set
        {
            var next = Math.Clamp(value, 1, Math.Max(1, StepCount));
            if (_stepIndex == next) return;
            _stepIndex = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StepLabel));
            OnPropertyChanged(nameof(ActiveStage));
            OnPropertyChanged(nameof(PlotOverlayText));
        }
    }

    public int StepCount
    {
        get => _stepCount;
        private set
        {
            var next = Math.Max(1, value);
            if (_stepCount == next) return;
            _stepCount = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StepLabel));
        }
    }

    public string UnitsLabel => "kN, m, s, °C";
    public string DimensionLabel =>
        _host.ProjectSettingsVM.IsAxisymmetricIdealization ? "2D Axisymmetric" : "2D Plane Strain";

    public string ResultTypeDisplay
    {
        get => ToResultTypeDisplay(Results.SelectedOutputType);
        set => ApplyResultTypeDisplay(value);
    }

    public IReadOnlyList<string> ResultTypeChoices { get; } =
    [
        "Total Displacement",
        "X Displacement",
        "Y Displacement",
        "Sigma XX",
        "Sigma YY",
        "Sigma XY",
        "Maximum Shear Stress",
        "Mean Effective Stress",
        "Epsilon XX",
        "Epsilon YY",
        "Epsilon XY",
        "Volumetric Strain",
        "Reaction Forces"
    ];

    public string ComponentDisplay
    {
        get
        {
            if (string.Equals(Results.SelectedOutputType, ResultsViewModel.OutputDisplacementX, StringComparison.Ordinal))
                return ComponentX;
            if (string.Equals(Results.SelectedOutputType, ResultsViewModel.OutputDisplacementY, StringComparison.Ordinal))
                return ComponentY;
            return ComponentMagnitude;
        }
        set
        {
            if (string.Equals(value, ComponentX, StringComparison.Ordinal))
                ApplyOutput(ResultsViewModel.OutputDisplacementX);
            else if (string.Equals(value, ComponentY, StringComparison.Ordinal))
                ApplyOutput(ResultsViewModel.OutputDisplacementY);
            else
                ApplyOutput(ResultsViewModel.OutputDisplacementMagnitude);
        }
    }

    public string DisplayMode
    {
        get
        {
            if (Results.IsVectorMode) return DisplayVectors;
            if (Results.IsDeformedMeshMode) return DisplayDeformedMesh;
            return DisplayFilledContours;
        }
        set => ApplyDisplay(value);
    }

    public bool IsContoursTool => Results.IsContourLikeMode;
    public bool IsVectorsTool => Results.IsVectorMode;
    public bool IsDeformationTool => Results.IsDeformedMeshMode;

    public bool ShowContourLines
    {
        get => _showContourLines;
        set { if (_showContourLines == value) return; _showContourLines = value; OnPropertyChanged(); }
    }

    public int ContourCount
    {
        get => _contourCount;
        set { if (_contourCount == value) return; _contourCount = Math.Clamp(value, 2, 64); OnPropertyChanged(); }
    }

    public bool RangeAutomatic
    {
        get => _rangeAutomatic;
        set { if (_rangeAutomatic == value) return; _rangeAutomatic = value; OnPropertyChanged(); }
    }

    public string ContourScheme
    {
        get => _contourScheme;
        set { if (_contourScheme == value) return; _contourScheme = value; OnPropertyChanged(); }
    }

    public string LegendPosition
    {
        get => _legendPosition;
        set { if (_legendPosition == value) return; _legendPosition = value; OnPropertyChanged(); }
    }

    public bool ShowDeformedMesh
    {
        get => Results.IsDeformedMeshMode;
        set
        {
            if (value)
                ApplyDisplay(DisplayDeformedMesh);
            else if (Results.IsDeformedMeshMode)
                ApplyDisplay(DisplayFilledContours);
        }
    }

    public bool ShowLoads
    {
        get => _showLoads;
        set { if (_showLoads == value) return; _showLoads = value; OnPropertyChanged(); }
    }

    public bool ShowSupports
    {
        get => _showSupports;
        set { if (_showSupports == value) return; _showSupports = value; OnPropertyChanged(); }
    }

    public double TransparencyPercent
    {
        get => _transparencyPercent;
        set { if (Math.Abs(_transparencyPercent - value) < 0.01) return; _transparencyPercent = value; OnPropertyChanged(); }
    }

    public string RangeMaxText => FormatScalar(CurrentFieldMax);
    public string RangeMinText => FormatScalar(CurrentFieldMin);
    public double? CurrentFieldMax { get; private set; }
    public double? CurrentFieldMin { get; private set; }

    public string MessagesText
    {
        get
        {
            var log = _host.Workspace?.SolverLog;
            var status = Results.FeStatusMessage;
            if (string.IsNullOrWhiteSpace(log))
                return string.IsNullOrWhiteSpace(status) ? "Run a Static analysis to populate results." : status;
            return string.IsNullOrWhiteSpace(status) ? log : log + Environment.NewLine + status;
        }
    }

    public string PlotOverlayTitle => ResultTypeDisplay;
    public string PlotOverlayText =>
        $"Load Case: {LoadCaseLabel}{Environment.NewLine}Stage: {ActiveStage}{Environment.NewLine}Scaling: 1.000";

    /// <summary>Static RESULTS landing: contours + Total Displacement, matching the screenshot default.</summary>
    public void ApplyStaticDefaults()
    {
        Results.VisualizationMode = ResultsViewModel.VisualizationModeContour2D;
        Results.SelectedOutputType = ResultsViewModel.OutputDisplacementMagnitude;
        Results.ResultsSubTabIndex = 1;
        Results.ShowContourLegend = true;
        LoadCaseLabel = "1 - Static";
        if (LoadCases.Count == 0)
            LoadCases.Add("1 - Static");
        StepCount = Math.Max(1, Results.AvailableStages.Count);
        StepIndex = 1;
        RebuildTree();
        RefreshSummaries();
        CheckNodeForOutput(ResultsViewModel.OutputDisplacementMagnitude);
    }

    public void Refresh()
    {
        RebuildTree();
        RefreshSummaries();
    }

    private void OnSelectNode(ResultsManagerNode? node)
    {
        if (node == null) return;
        SelectedNode = node;
        if (node.Kind == ResultsManagerNodeKind.ResultType && !string.IsNullOrWhiteSpace(node.OutputType))
        {
            ApplyOutput(node.OutputType, node.VisualizationMode);
            CheckNodeForOutput(node.OutputType);
        }
        else if (node.Kind == ResultsManagerNodeKind.ResultSet)
        {
            ApplyStaticDefaults();
        }
    }

    private void ApplyDisplay(string display)
    {
        Results.VisualizationMode = display switch
        {
            DisplayVectors => ResultsViewModel.VisualizationModeVector,
            DisplayDeformedMesh => ResultsViewModel.VisualizationModeDeformedMesh,
            _ => ResultsViewModel.VisualizationModeContour2D
        };
        Results.ResultsSubTabIndex = 1;
        RaisePlotBindings();
    }

    private void ApplyResultTypeDisplay(string display)
    {
        ApplyOutput(display switch
        {
            "X Displacement" => ResultsViewModel.OutputDisplacementX,
            "Y Displacement" => ResultsViewModel.OutputDisplacementY,
            "Sigma XX" => ResultsViewModel.OutputStressXX,
            "Sigma YY" => ResultsViewModel.OutputStressYY,
            "Sigma XY" => ResultsViewModel.OutputStressXY,
            "Maximum Shear Stress" => ResultsViewModel.OutputVonMises,
            "Mean Effective Stress" => ResultsViewModel.OutputEffectivePrincipalMax,
            "Epsilon XX" => ResultsViewModel.OutputStrainXX,
            "Epsilon YY" => ResultsViewModel.OutputStrainYY,
            "Epsilon XY" => ResultsViewModel.OutputShearStrain,
            "Volumetric Strain" => ResultsViewModel.OutputStrainXX,
            "Reaction Forces" => ResultsViewModel.OutputDisplacementMagnitude,
            _ => ResultsViewModel.OutputDisplacementMagnitude
        });
    }

    private void ApplyOutput(string outputType, string? visualizationMode = null)
    {
        if (!string.IsNullOrWhiteSpace(visualizationMode))
            Results.VisualizationMode = visualizationMode;
        else if (!Results.IsContourLikeMode && !Results.IsVectorMode)
            Results.VisualizationMode = ResultsViewModel.VisualizationModeContour2D;
        Results.SelectedOutputType = outputType;
        Results.ResultsSubTabIndex = 1;
        CheckNodeForOutput(outputType);
        RaisePlotBindings();
        RefreshSummaries();
    }

    private void CheckNodeForOutput(string outputType)
    {
        foreach (var node in Flatten(Roots))
        {
            node.IsChecked = node.Kind == ResultsManagerNodeKind.ResultType
                && string.Equals(node.OutputType, outputType, StringComparison.Ordinal);
            if (node.IsChecked)
                SelectedNode = node;
        }
    }

    private void RebuildTree()
    {
        Roots.Clear();
        var root = new ResultsManagerNode
        {
            Kind = ResultsManagerNodeKind.Root,
            Title = RootTitle,
            IsExpanded = true
        };
        var sets = new ResultsManagerNode { Kind = ResultsManagerNodeKind.Folder, Title = "Result Sets", IsExpanded = true };
        sets.Children.Add(new ResultsManagerNode
        {
            Kind = ResultsManagerNodeKind.ResultSet,
            Title = "1 - Static (Active)",
            IsChecked = true
        });

        var types = new ResultsManagerNode { Kind = ResultsManagerNodeKind.Folder, Title = "Result Types", IsExpanded = true };
        types.Children.Add(Folder("Displacement",
            Type("Total Displacement", ResultsViewModel.OutputDisplacementMagnitude),
            Type("X Displacement", ResultsViewModel.OutputDisplacementX),
            Type("Y Displacement", ResultsViewModel.OutputDisplacementY)));
        types.Children.Add(Folder("Stress",
            Type("Sigma XX", ResultsViewModel.OutputStressXX),
            Type("Sigma YY", ResultsViewModel.OutputStressYY),
            Type("Sigma XY", ResultsViewModel.OutputStressXY),
            Type("Maximum Shear Stress", ResultsViewModel.OutputVonMises),
            Type("Mean Effective Stress", ResultsViewModel.OutputEffectivePrincipalMax)));
        types.Children.Add(Folder("Strain",
            Type("Epsilon XX", ResultsViewModel.OutputStrainXX),
            Type("Epsilon YY", ResultsViewModel.OutputStrainYY),
            Type("Epsilon XY", ResultsViewModel.OutputShearStrain),
            Type("Volumetric Strain", ResultsViewModel.OutputStrainXX)));
        types.Children.Add(Folder("Forces",
            Type("Reaction Forces", ResultsViewModel.OutputDisplacementMagnitude, ResultsViewModel.VisualizationModeVector)));

        root.Children.Add(sets);
        root.Children.Add(types);
        Roots.Add(root);
        CheckNodeForOutput(Results.SelectedOutputType);
        OnPropertyChanged(nameof(RootTitle));
        OnPropertyChanged(nameof(HasCompletedStaticResults));
    }

    private static ResultsManagerNode Folder(string title, params ResultsManagerNode[] children)
    {
        var folder = new ResultsManagerNode { Kind = ResultsManagerNodeKind.Folder, Title = title, IsExpanded = true };
        foreach (var child in children)
            folder.Children.Add(child);
        return folder;
    }

    private static ResultsManagerNode Type(string title, string output, string mode = ResultsViewModel.VisualizationModeContour2D)
        => new()
        {
            Kind = ResultsManagerNodeKind.ResultType,
            Title = title,
            OutputType = output,
            VisualizationMode = mode
        };

    private void RefreshSummaries()
    {
        StepCount = Math.Max(1, Results.AvailableStages.Count);
        RebuildMinMax();
        OnPropertyChanged(nameof(AnalysisTypeLabel));
        OnPropertyChanged(nameof(SolverTypeLabel));
        OnPropertyChanged(nameof(TotalStages));
        OnPropertyChanged(nameof(ActiveStage));
        OnPropertyChanged(nameof(RootTitle));
        OnPropertyChanged(nameof(HasCompletedStaticResults));
        OnPropertyChanged(nameof(MessagesText));
        OnPropertyChanged(nameof(RangeMaxText));
        OnPropertyChanged(nameof(RangeMinText));
        OnPropertyChanged(nameof(PlotOverlayTitle));
        OnPropertyChanged(nameof(PlotOverlayText));
        RaisePlotBindings();
    }

    private void RebuildMinMax()
    {
        MinMaxRows.Clear();
        AddDisplacementRow("Total Displacement", n => Magnitude(n));
        AddDisplacementRow("X Displacement", n => Ux(n));
        AddDisplacementRow("Y Displacement", n => Uy(n));
        AddStressRow("Sigma XX", s => s.StressXX);
        AddStressRow("Sigma YY", s => s.StressYY);

        var current = MinMaxRows.FirstOrDefault(r =>
            string.Equals(r.Quantity, ResultTypeDisplay, StringComparison.OrdinalIgnoreCase)
            || (ResultTypeDisplay == "Total Displacement" && r.Quantity == "Total Displacement"));
        if (current != null
            && double.TryParse(current.MaxText, NumberStyles.Float, CultureInfo.InvariantCulture, out var max)
            && double.TryParse(current.MinText, NumberStyles.Float, CultureInfo.InvariantCulture, out var min))
        {
            CurrentFieldMax = max;
            CurrentFieldMin = min;
        }
        else
        {
            CurrentFieldMax = null;
            CurrentFieldMin = null;
        }
    }

    private void AddDisplacementRow(string name, Func<Node, double> getter)
    {
        if (Results.FENodes.Count == 0)
        {
            MinMaxRows.Add(new ResultsMinMaxRow { Quantity = name });
            return;
        }

        var min = Results.FENodes[0];
        var max = Results.FENodes[0];
        var minV = getter(min);
        var maxV = getter(max);
        foreach (var node in Results.FENodes)
        {
            var v = getter(node);
            if (v < minV) { minV = v; min = node; }
            if (v > maxV) { maxV = v; max = node; }
        }

        MinMaxRows.Add(new ResultsMinMaxRow
        {
            Quantity = name,
            MinText = FormatScalar(minV),
            MaxText = FormatScalar(maxV),
            MinLocation = FormatXy(min.X, min.Y),
            MaxLocation = FormatXy(max.X, max.Y)
        });
    }

    private void AddStressRow(string name, Func<ElementStress, double> getter)
    {
        if (Results.FeElementStresses.Count == 0)
        {
            MinMaxRows.Add(new ResultsMinMaxRow { Quantity = name });
            return;
        }

        var first = Results.FeElementStresses[0];
        var minV = getter(first);
        var maxV = getter(first);
        var minId = first.ElementId;
        var maxId = first.ElementId;
        foreach (var s in Results.FeElementStresses)
        {
            var v = getter(s);
            if (v < minV) { minV = v; minId = s.ElementId; }
            if (v > maxV) { maxV = v; maxId = s.ElementId; }
        }

        MinMaxRows.Add(new ResultsMinMaxRow
        {
            Quantity = name,
            MinText = FormatScalar(minV),
            MaxText = FormatScalar(maxV),
            MinLocation = $"El {minId}",
            MaxLocation = $"El {maxId}"
        });
    }

    private static double Ux(Node n) => n.Displacements.Length > 0 ? n.Displacements[0] : 0;
    private static double Uy(Node n) => n.Displacements.Length > 1 ? n.Displacements[1] : 0;
    private static double Magnitude(Node n)
    {
        var u = Ux(n);
        var v = Uy(n);
        return Math.Sqrt(u * u + v * v);
    }

    private static string FormatScalar(double? value)
        => value is { } v ? v.ToString("0.0000", CultureInfo.InvariantCulture) : "—";

    private static string FormatXy(double x, double y)
        => $"({x.ToString("0.00", CultureInfo.InvariantCulture)}, {y.ToString("0.00", CultureInfo.InvariantCulture)})";

    private static string ToResultTypeDisplay(string output) => output switch
    {
        ResultsViewModel.OutputDisplacementX => "X Displacement",
        ResultsViewModel.OutputDisplacementY => "Y Displacement",
        ResultsViewModel.OutputStressXX => "Sigma XX",
        ResultsViewModel.OutputStressYY => "Sigma YY",
        ResultsViewModel.OutputStressXY => "Sigma XY",
        ResultsViewModel.OutputVonMises => "Maximum Shear Stress",
        ResultsViewModel.OutputEffectivePrincipalMax => "Mean Effective Stress",
        ResultsViewModel.OutputStrainXX => "Epsilon XX",
        ResultsViewModel.OutputStrainYY => "Epsilon YY",
        ResultsViewModel.OutputShearStrain => "Epsilon XY",
        _ => "Total Displacement"
    };

    private static IEnumerable<ResultsManagerNode> Flatten(IEnumerable<ResultsManagerNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
                yield return child;
        }
    }

    private void OnResultsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ResultsViewModel.SelectedOutputType)
            or nameof(ResultsViewModel.VisualizationMode)
            or nameof(ResultsViewModel.LastP610Run)
            or nameof(ResultsViewModel.FENodes)
            or nameof(ResultsViewModel.FeElementStresses)
            or nameof(ResultsViewModel.FeStatusMessage)
            or nameof(ResultsViewModel.AvailableStages))
        {
            RefreshSummaries();
            if (e.PropertyName is nameof(ResultsViewModel.LastP610Run) or nameof(ResultsViewModel.FENodes))
                RebuildTree();
        }
    }

    private void RaisePlotBindings()
    {
        OnPropertyChanged(nameof(ResultTypeDisplay));
        OnPropertyChanged(nameof(ComponentDisplay));
        OnPropertyChanged(nameof(DisplayMode));
        OnPropertyChanged(nameof(IsContoursTool));
        OnPropertyChanged(nameof(IsVectorsTool));
        OnPropertyChanged(nameof(IsDeformationTool));
        OnPropertyChanged(nameof(ShowDeformedMesh));
        OnPropertyChanged(nameof(PlotOverlayTitle));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
