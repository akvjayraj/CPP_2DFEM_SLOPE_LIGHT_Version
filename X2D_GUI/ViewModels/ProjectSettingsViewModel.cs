using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.ViewModels;

public partial class ProjectSettingsViewModel : INotifyPropertyChanged
{
    private GeometryDefinitionViewModel? _geometryDefinitionVM;
    private MaterialPropertiesViewModel? _materialPropertiesVM;
    private FEAnalysisViewModel? _feAnalysisVM;
    private int _selectedSettingsTabIndex;
    private bool _isDeterministicAnalysis = true;
    private bool _isProbabilisticAnalysis;
    private ProblemDimension _selectedDimension = ProblemDimension.TwoD;
    private AnalysisTypeNode? _selectedAnalysisTreeNode;
    private AnalysisCategoryNode? _selectedAnalysisCategoryNode;
    private bool _syncingAnalysisTreeSelection;
    private bool _syncingCategoryOnFromGroundwater;
    /// <summary>Persisted ON state per analysis category name (defaults to true).</summary>
    private readonly Dictionary<string, bool> _categoryOnByName = new(StringComparer.Ordinal);
    private bool _srfAxisymmetric;
    private bool _isStageEditorExpanded;

    /// <summary>
    /// Mechanical continuum axisymmetric SRF (routes to <c>*_SRF_axisym</c>). Independent of seepage p72 Axisymmetric.
    /// </summary>
    public bool SrfAxisymmetric
    {
        get => _srfAxisymmetric;
        set
        {
            if (_srfAxisymmetric == value) return;
            _srfAxisymmetric = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Idealization));
            OnPropertyChanged(nameof(IsPlaneStrainIdealization));
            OnPropertyChanged(nameof(IsAxisymmetricIdealization));
            _feAnalysisVM?.RefreshResolvedRunRoutingDisplay();
            OnPropertyChanged(nameof(AnalysisEngineSummary));
        }
    }

    /// <summary>Continuum idealization; same persist field as <see cref="SrfAxisymmetric"/>.</summary>
    public AnalysisIdealization Idealization
    {
        get => _srfAxisymmetric ? AnalysisIdealization.Axisymmetric : AnalysisIdealization.PlaneStrain;
        set => SrfAxisymmetric = value == AnalysisIdealization.Axisymmetric;
    }

    public bool IsPlaneStrainIdealization
    {
        get => !_srfAxisymmetric;
        set { if (value) SrfAxisymmetric = false; }
    }

    public bool IsAxisymmetricIdealization
    {
        get => _srfAxisymmetric;
        set { if (value) SrfAxisymmetric = true; }
    }

    /// <summary>0=General, 1=Analysis Type, 2=Grid &amp; Axes.</summary>
    public int SelectedSettingsTabIndex
    {
        get => _selectedSettingsTabIndex;
        set
        {
            var clamped = SettingsTabs.Count == 0 ? 0 : Math.Clamp(value, 0, SettingsTabs.Count - 1);
            if (_selectedSettingsTabIndex == clamped)
                return;
            _selectedSettingsTabIndex = clamped;
            OnPropertyChanged();
        }
    }

    public ProjectSettingsViewModel()
    {
        Project = new ProjectSettingsModel();
        Project.PropertyChanged += OnProjectModelPropertyChanged;
        GroundwaterStaticSettings = new GroundwaterStaticAnalysisSettings();
        DrawdownSettings = new FeDrawdownSettings();
        GroundwaterStaticSettings.PropertyChanged += OnGroundwaterStaticSettingsPropertyChanged;
        SeepageSettings = new SeepageAnalysisSettings();
        SeepageSettings.PropertyChanged += OnSeepageSettingsPropertyChanged;
        TransientSettings = new TransientAnalysisSettings();
        AvailableUnits = new List<string> { ProjectUnitSystem.Si, ProjectUnitSystem.Imperial };
        ApplyAxisToAllCanvasesCommand = new RelayCommand(ApplyAxisToAllCanvases);
        ApplyPreDrawdownToGroundwaterCommand = new RelayCommand(() => ApplyDrawdownSurfaceToGroundwater(post: false));
        ApplyPostDrawdownToGroundwaterCommand = new RelayCommand(() => ApplyDrawdownSurfaceToGroundwater(post: true));
        InitializeSettingsTabs();
        InitFieldStationCommands();
    }

    public ICommand ApplyPreDrawdownToGroundwaterCommand { get; }
    public ICommand ApplyPostDrawdownToGroundwaterCommand { get; }

    private void ApplyDrawdownSurfaceToGroundwater(bool post)
    {
        if (GroundwaterVM == null)
            return;
        GroundwaterStaticSettings.IncludeWaterSurfaces = true;
        var current = GroundwaterVM.BuildModel();
        var stage = DrawdownSettings.BuildStageModel(current, post);
        GroundwaterVM.ApplyImportedModel(stage);
    }

    private void OnProjectModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectSettingsModel.Units))
            NotifyUnitSystemLabels();
    }

    /// <summary>Raise when General → Unit System changes so all bound unit suffixes refresh.</summary>
    public void NotifyUnitSystemLabels()
    {
        OnPropertyChanged(nameof(IsImperialUnits));
        OnPropertyChanged(nameof(LengthUnitLabel));
        OnPropertyChanged(nameof(UnitWeightUnitLabel));
        OnPropertyChanged(nameof(StressUnitLabel));
        OnPropertyChanged(nameof(GammaWPrompt));
    }

    public bool IsImperialUnits => ProjectUnitSystem.IsImperial(Project.Units);
    public string LengthUnitLabel => ProjectUnitSystem.LengthLabel(Project.Units);
    public string UnitWeightUnitLabel => ProjectUnitSystem.UnitWeightLabel(Project.Units);
    public string StressUnitLabel => ProjectUnitSystem.StressLabel(Project.Units);
    public string GammaWPrompt => $"Unit weight of water γw ({UnitWeightUnitLabel}):";

    /// <summary>Static Flow-Seepage/Groundwater component toggles (Analysis Type detail panel).</summary>
    public GroundwaterStaticAnalysisSettings GroundwaterStaticSettings { get; }

    /// <summary>Rapid drawdown procedure and pre/post reservoir surfaces.</summary>
    public FeDrawdownSettings DrawdownSettings { get; }

    /// <summary>True when Flow-Seepage/Groundwater → Static is the selected analysis type (gates GW inputs / results).</summary>
    public bool IsGroundwaterStaticSelected =>
        MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(_materialPropertiesVM?.SelectedAnalysisCategory)
        && string.Equals(_materialPropertiesVM?.SelectedDetailedAnalysisType,
            MaterialPropertiesViewModel.GroundwaterStaticType, StringComparison.Ordinal);

    /// <summary>True when the Analysis tree has the Flow-Seepage/Groundwater category node selected (category detail panel).</summary>
    public bool IsGroundwaterCategorySelected =>
        _selectedAnalysisCategoryNode != null
        && MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(_selectedAnalysisCategoryNode.Name);

    /// <summary>True when a non-groundwater category node is selected (generic category panel).</summary>
    public bool IsGenericCategoryDetailSelected =>
        _selectedAnalysisCategoryNode != null && !IsGroundwaterCategorySelected;

    /// <summary>True when the Static type node is selected in the tree (Static options panel).</summary>
    public bool IsGroundwaterStaticDetailSelected =>
        _selectedAnalysisTreeNode != null
        && MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(_selectedAnalysisTreeNode.Category)
        && string.Equals(_selectedAnalysisTreeNode.Name, MaterialPropertiesViewModel.GroundwaterStaticType, StringComparison.Ordinal);

    /// <summary>True when a Flow-Seepage/Groundwater type node other than Static / Steady / Transient is selected.</summary>
    public bool IsGroundwaterNonStaticTypeSelected =>
        _selectedAnalysisTreeNode != null
        && MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(_selectedAnalysisTreeNode.Category)
        && !string.Equals(_selectedAnalysisTreeNode.Name, MaterialPropertiesViewModel.GroundwaterStaticType, StringComparison.Ordinal)
        && !MaterialPropertiesViewModel.IsGroundwaterSteadyStateTreeType(_selectedAnalysisTreeNode.Name)
        && !MaterialPropertiesViewModel.IsGroundwaterTransientStateTreeType(_selectedAnalysisTreeNode.Name)
        && !MaterialPropertiesViewModel.IsGroundwaterFutureTreeType(_selectedAnalysisTreeNode.Name);

    /// <summary>Options for the steady seepage solves (p72/p73/p74): boundary heads, p73 iteration control, Water Head.</summary>
    public SeepageAnalysisSettings SeepageSettings { get; }

    /// <summary>Options for Transient State solves (p84 / p88): θ-method time controls and drain-face BCs.</summary>
    public TransientAnalysisSettings TransientSettings { get; }

    /// <summary>True when the Steady State tree leaf is selected (solver checkboxes + seepage options panel).</summary>
    public bool IsSteadyStateDetailSelected =>
        _selectedAnalysisTreeNode != null
        && MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(_selectedAnalysisTreeNode.Category)
        && MaterialPropertiesViewModel.IsGroundwaterSteadyStateTreeType(_selectedAnalysisTreeNode.Name);

    /// <summary>True when the Transient State tree leaf is selected.</summary>
    public bool IsTransientStateDetailSelected =>
        _selectedAnalysisTreeNode != null
        && MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(_selectedAnalysisTreeNode.Category)
        && MaterialPropertiesViewModel.IsGroundwaterTransientStateTreeType(_selectedAnalysisTreeNode.Name);

    /// <summary>True when a disabled future Flow-Seepage leaf is selected (should be rare; clicks are ignored).</summary>
    public bool IsGroundwaterFutureDetailSelected =>
        _selectedAnalysisTreeNode != null
        && MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(_selectedAnalysisTreeNode.Category)
        && MaterialPropertiesViewModel.IsGroundwaterFutureTreeType(_selectedAnalysisTreeNode.Name);

    /// <summary>Rapid drawdown only when SRF is ON and the Static Flow-Seepage leaf is selected.</summary>
    public bool IsRapidDrawdownSectionVisible =>
        IsCategoryOn(MaterialPropertiesViewModel.SrfAnalysisCategory)
        && IsGroundwaterStaticDetailSelected;

    /// <summary>Derived water-coupling label from the selected (or last) Flow-Seepage leaf.</summary>
    public WaterParticipation WaterParticipation =>
        AnalysisKindMapper.ResolveWaterParticipation(ResolveGroundwaterTreeLeafForWater(), AreGroundwaterTypeOptionsEnabled);

    public string WaterParticipationSummary =>
        AnalysisKindMapper.WaterParticipationSummary(WaterParticipation);

    /// <summary>Derived Run-FE kind (read-only; tree leaf is the picker).</summary>
    public AnalysisKind ResolvedAnalysisKind =>
        AnalysisKindMapper.Resolve(
            _materialPropertiesVM?.SelectedAnalysisCategory,
            _materialPropertiesVM?.SelectedDetailedAnalysisType,
            _materialPropertiesVM?.LastGroundwaterTreeMode,
            IsCategoryOn(MaterialPropertiesViewModel.SrfAnalysisCategory),
            AreGroundwaterTypeOptionsEnabled,
            IsActiveAnalysisCategoryOn,
            _materialPropertiesVM?.Is2DProblem != false);

    /// <summary>Read-only engine line for the Analysis header.</summary>
    public string AnalysisEngineSummary
    {
        get
        {
            var fromFe = _feAnalysisVM?.ActiveAnalysisModeSummary;
            if (!string.IsNullOrWhiteSpace(fromFe))
                return fromFe;
            return ResolvedAnalysisKind switch
            {
                AnalysisKind.CoupledSeepageSrf => "Engine: FE seepage → one-way u transfer → Slope SRF",
                AnalysisKind.CoupledStaticSrf => "Engine: prescribed u in the mechanical / SRF deck",
                AnalysisKind.GroundwaterTransient => "Engine: fem_cpp p84 / p88 (θ-method dissipation)",
                AnalysisKind.GroundwaterSteady => "Engine: fem_cpp p73 / p74 (steady seepage)",
                AnalysisKind.GroundwaterStatic => "Engine: prescribed pore pressure (no seepage solve)",
                AnalysisKind.Srf => "Engine: Slope SRF",
                AnalysisKind.Off => "Engine: category is OFF",
                _ => "Engine: Structural / p610"
            };
        }
    }

    private string? ResolveGroundwaterTreeLeafForWater()
    {
        if (_selectedAnalysisTreeNode != null
            && MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(_selectedAnalysisTreeNode.Category))
            return _selectedAnalysisTreeNode.Name;
        return _materialPropertiesVM?.LastGroundwaterTreeMode
            ?? _materialPropertiesVM?.SelectedDetailedAnalysisType;
    }

    /// <summary>Legacy alias for Steady State detail panel binding.</summary>
    public bool IsSeepageDetailSelected => IsSteadyStateDetailSelected;

    /// <summary>True when the active analysis type is a transient consolidation solve (p84 / p88).</summary>
    public bool IsTransientAnalysisSelected =>
        MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(_materialPropertiesVM?.SelectedAnalysisCategory)
        && MaterialPropertiesViewModel.IsTransientFlowAnalysisType(_materialPropertiesVM?.SelectedDetailedAnalysisType);

    /// <summary>p84 parameter panel when Transient (plane/axisymmetric) is active.</summary>
    public bool IsTransientP84Selected =>
        string.Equals(
            _materialPropertiesVM?.SelectedDetailedAnalysisType,
            MaterialPropertiesViewModel.GroundwaterTransientPlaneAxisymType,
            StringComparison.Ordinal);

    /// <summary>p88 note panel when General 2D consolidation is active.</summary>
    public bool IsTransientP88Selected =>
        string.Equals(
            _materialPropertiesVM?.SelectedDetailedAnalysisType,
            MaterialPropertiesViewModel.GroundwaterTransientGeneral2DType,
            StringComparison.Ordinal);

    /// <summary>True when the active analysis type is one of the seepage flow solves (drives Run FE routing).</summary>
    public bool IsSeepageAnalysisSelected =>
        MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(_materialPropertiesVM?.SelectedAnalysisCategory)
        && MaterialPropertiesViewModel.IsSeepageFlowAnalysisType(_materialPropertiesVM?.SelectedDetailedAnalysisType);

    /// <summary>True when Water Head is enabled for the currently active seepage solver.</summary>
    public bool IsActiveSeepageWaterHeadEnabled =>
        IsSeepageAnalysisSelected
        && SeepageSettings.GetIncludeWaterHeadForType(_materialPropertiesVM?.SelectedDetailedAnalysisType);

    /// <summary>p73 iteration options only apply when Unconfined / free-surface flow is the active solver.</summary>
    public bool IsFreeSurfaceSeepageSelected =>
        string.Equals(
            _materialPropertiesVM?.SelectedDetailedAnalysisType,
            MaterialPropertiesViewModel.GroundwaterSeepageFreeSurfaceType,
            StringComparison.Ordinal);

    /// <summary>True when Steady State panel should show 2D solver checkboxes.</summary>
    public bool IsSteadyState2DPanelVisible =>
        IsSteadyStateDetailSelected && (_materialPropertiesVM?.Is2DProblem != false)
        && _materialPropertiesVM?.Is3DProblem != true;

    /// <summary>True when Steady State panel should show the 3D General seepage checkbox.</summary>
    public bool IsSteadyState3DPanelVisible =>
        IsSteadyStateDetailSelected && _materialPropertiesVM?.Is3DProblem == true;

    /// <summary>Exclusive Steady State solver checkbox: Unconfined / free-surface flow (p73).</summary>
    public bool IsSteadyStateFreeSurfaceChecked
    {
        get => string.Equals(
            _materialPropertiesVM?.SelectedDetailedAnalysisType,
            MaterialPropertiesViewModel.GroundwaterSeepageFreeSurfaceType,
            StringComparison.Ordinal);
        set
        {
            if (value)
                ActivateSteadyStateSolver(MaterialPropertiesViewModel.GroundwaterSeepageFreeSurfaceType);
            else if (IsSteadyStateFreeSurfaceChecked)
                ActivateSteadyStateSolver(MaterialPropertiesViewModel.GroundwaterSeepageFreeSurfaceType);
        }
    }

    /// <summary>Exclusive Steady State solver checkbox: General 2D (p74).</summary>
    public bool IsSteadyStateGeneral2DChecked
    {
        get => string.Equals(
            _materialPropertiesVM?.SelectedDetailedAnalysisType,
            MaterialPropertiesViewModel.GroundwaterSeepageGeneralType,
            StringComparison.Ordinal);
        set
        {
            if (value)
                ActivateSteadyStateSolver(MaterialPropertiesViewModel.GroundwaterSeepageGeneralType);
            else if (IsSteadyStateGeneral2DChecked)
                ActivateSteadyStateSolver(MaterialPropertiesViewModel.GroundwaterSeepageGeneralType);
        }
    }

    /// <summary>Exclusive Steady State solver checkbox: General 3D seepage.</summary>
    public bool IsSteadyStateGeneral3DChecked
    {
        get => string.Equals(
            _materialPropertiesVM?.SelectedDetailedAnalysisType,
            MaterialPropertiesViewModel.GroundwaterSeepageGeneral3DType,
            StringComparison.Ordinal);
        set
        {
            if (value)
                ActivateSteadyStateSolver(MaterialPropertiesViewModel.GroundwaterSeepageGeneral3DType);
            else if (IsSteadyStateGeneral3DChecked)
                ActivateSteadyStateSolver(MaterialPropertiesViewModel.GroundwaterSeepageGeneral3DType);
        }
    }

    /// <summary>Solver-program note shown in the Steady State options panel header.</summary>
    public string SeepageSolverNote => _materialPropertiesVM?.SelectedDetailedAnalysisType switch
    {
        MaterialPropertiesViewModel.GroundwaterSeepageFreeSurfaceType =>
            "Solver: fem_cpp p73 — unconfined flow; the phreatic surface is located iteratively.",
        MaterialPropertiesViewModel.GroundwaterSeepageGeneralType =>
            "Solver: fem_cpp p74 — steady seepage on a general T3/T6/Q4/Q8 mesh.",
        MaterialPropertiesViewModel.GroundwaterSeepageGeneral3DType =>
            "Solver: fem_cpp — general 3D seepage (when available).",
        _ => "Select a steady seepage solver below to activate analysis."
    };

    /// <summary>Exclusive Transient State solver checkbox: p84 structured Q4.</summary>
    public bool IsTransientP84Checked
    {
        get => IsTransientP84Selected;
        set
        {
            if (value)
                ActivateTransientSolver(MaterialPropertiesViewModel.GroundwaterTransientPlaneAxisymType);
            else if (IsTransientP84Checked)
                ActivateTransientSolver(MaterialPropertiesViewModel.GroundwaterTransientPlaneAxisymType);
        }
    }

    /// <summary>Exclusive Transient State solver checkbox: p88 general mesh.</summary>
    public bool IsTransientP88Checked
    {
        get => IsTransientP88Selected;
        set
        {
            if (value)
                ActivateTransientSolver(MaterialPropertiesViewModel.GroundwaterTransientGeneral2DType);
            else if (IsTransientP88Checked)
                ActivateTransientSolver(MaterialPropertiesViewModel.GroundwaterTransientGeneral2DType);
        }
    }

    /// <summary>Solver-program note shown in the Transient State options panel header.</summary>
    public string TransientSolverNote => _materialPropertiesVM?.SelectedDetailedAnalysisType switch
    {
        MaterialPropertiesViewModel.GroundwaterTransientPlaneAxisymType =>
            "Solver: fem_cpp p84 on structured Q4 (optional axisymmetric). Non-structured T3/T6/Q4/Q8 meshes auto-route to p88.",
        MaterialPropertiesViewModel.GroundwaterTransientGeneral2DType =>
            "Solver: fem_cpp p88 — θ-method consolidation on homogeneous T3, T6, Q4, or Q8 meshes.",
        _ => "Select a transient consolidation solver below. All homogeneous 2D mesh types (T3/T6/Q4/Q8) are supported."
    };

    /// <summary>True when Include Groundwater / Flow-Seepage category ON is checked (enables GW type option panels).</summary>
    public bool AreGroundwaterTypeOptionsEnabled => IsCategoryOn(MaterialPropertiesViewModel.GroundwaterAnalysisCategory);

    /// <summary>
    /// True when the currently selected analysis category (or the category of the selected type)
    /// has its ON checkbox checked — gates type-option panels and related analysis functions.
    /// </summary>
    public bool AreSelectedCategoryTypeOptionsEnabled
    {
        get
        {
            if (_selectedAnalysisCategoryNode != null)
                return _selectedAnalysisCategoryNode.IsOn;
            if (_selectedAnalysisTreeNode != null)
                return IsCategoryOn(_selectedAnalysisTreeNode.Category);
            var cat = _materialPropertiesVM?.SelectedAnalysisCategory;
            return string.IsNullOrEmpty(cat) || IsCategoryOn(cat);
        }
    }

    /// <summary>True when the project's active analysis category is ON (for Run FE / feature gating).</summary>
    public bool IsActiveAnalysisCategoryOn =>
        IsCategoryOn(_materialPropertiesVM?.SelectedAnalysisCategory);

    public bool IsCategoryOn(string? categoryName)
    {
        categoryName = MaterialPropertiesViewModel.NormalizeAnalysisCategory(categoryName);
        if (string.IsNullOrEmpty(categoryName)) return true;
        if (MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(categoryName))
            return GroundwaterVM?.IsEnabled == true;
        return _categoryOnByName.TryGetValue(categoryName, out var on) && on;
    }

    public void SetCategoryOn(string categoryName, bool isOn)
    {
        categoryName = MaterialPropertiesViewModel.NormalizeAnalysisCategory(categoryName) ?? categoryName;
        if (string.IsNullOrEmpty(categoryName)) return;

        if (MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(categoryName))
        {
            _categoryOnByName[MaterialPropertiesViewModel.GroundwaterAnalysisCategory] = isOn;
            if (GroundwaterVM != null && GroundwaterVM.IsEnabled != isOn)
            {
                _syncingCategoryOnFromGroundwater = true;
                try { GroundwaterVM.IsEnabled = isOn; }
                finally { _syncingCategoryOnFromGroundwater = false; }
            }
        }
        else
        {
            _categoryOnByName[categoryName] = isOn;
        }

        var node = AnalysisTree.FirstOrDefault(c =>
            string.Equals(c.Name, categoryName, StringComparison.Ordinal)
            || (MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(categoryName)
                && MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(c.Name)));
        if (node != null && node.IsOn != isOn)
            node.IsOn = isOn;

        NotifyCategoryOnBindings();
    }

    /// <summary>Snapshot of category ON flags for .xfem persistence.</summary>
    public IReadOnlyDictionary<string, bool> GetCategoryOnSnapshot()
    {
        EnsureCategoryOnDefaults();
        if (GroundwaterVM != null)
            _categoryOnByName[MaterialPropertiesViewModel.GroundwaterAnalysisCategory] = GroundwaterVM.IsEnabled;
        return new Dictionary<string, bool>(_categoryOnByName, StringComparer.Ordinal);
    }

    public void ApplyCategoryOnSnapshot(IEnumerable<(string Category, bool IsOn)>? flags)
    {
        _categoryOnByName.Clear();
        EnsureCategoryOnDefaults();
        if (flags != null)
        {
            foreach (var (category, isOn) in flags)
            {
                var key = MaterialPropertiesViewModel.NormalizeAnalysisCategory(category) ?? category;
                if (!string.IsNullOrEmpty(key))
                    _categoryOnByName[key] = isOn;
            }
        }

        if (GroundwaterVM != null)
        {
            _syncingCategoryOnFromGroundwater = true;
            try
            {
                var gwOn = _categoryOnByName.TryGetValue(
                    MaterialPropertiesViewModel.GroundwaterAnalysisCategory, out var on) && on;
                GroundwaterVM.IsEnabled = gwOn;
            }
            finally { _syncingCategoryOnFromGroundwater = false; }
        }

        foreach (var node in AnalysisTree)
        {
            if (MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(node.Name))
                node.IsOn = GroundwaterVM?.IsEnabled == true;
            else
                node.IsOn = _categoryOnByName.TryGetValue(node.Name, out var on) && on;
        }

        NotifyCategoryOnBindings();
    }

    private void EnsureCategoryOnDefaults()
    {
        if (_materialPropertiesVM == null) return;
        foreach (var cat in _materialPropertiesVM.AnalysisCategories)
        {
            if (!_categoryOnByName.ContainsKey(cat))
                _categoryOnByName[cat] = false;
        }
    }

    private void NotifyCategoryOnBindings()
    {
        OnPropertyChanged(nameof(AreGroundwaterTypeOptionsEnabled));
        OnPropertyChanged(nameof(AreSelectedCategoryTypeOptionsEnabled));
        OnPropertyChanged(nameof(IsActiveAnalysisCategoryOn));
        NotifyWaterAndEngineBindings();
    }

    private void NotifyWaterAndEngineBindings()
    {
        OnPropertyChanged(nameof(WaterParticipation));
        OnPropertyChanged(nameof(WaterParticipationSummary));
        OnPropertyChanged(nameof(ResolvedAnalysisKind));
        OnPropertyChanged(nameof(AnalysisEngineSummary));
        OnPropertyChanged(nameof(IsRapidDrawdownSectionVisible));
        OnPropertyChanged(nameof(IsGroundwaterFutureDetailSelected));
    }

    public ObservableCollection<ProjectSettingsTabDescriptor> SettingsTabs { get; } = new();

    /// <summary>Analysis Type left panel: categories with detailed type children.</summary>
    public ObservableCollection<AnalysisCategoryNode> AnalysisTree { get; } = new();

    /// <summary>Category or type node shown in the right detail panel.</summary>
    public object? SelectedAnalysisDetail =>
        (object?)_selectedAnalysisCategoryNode ?? _selectedAnalysisTreeNode;

    /// <summary>Currently selected category node (e.g. Flow-Seepage/Groundwater category panel).</summary>
    public AnalysisCategoryNode? SelectedAnalysisCategoryNode
    {
        get => _selectedAnalysisCategoryNode;
        set
        {
            if (ReferenceEquals(_selectedAnalysisCategoryNode, value))
                return;

            if (_selectedAnalysisCategoryNode != null)
                _selectedAnalysisCategoryNode.IsSelected = false;

            _selectedAnalysisCategoryNode = value;
            if (_selectedAnalysisCategoryNode != null)
            {
                _selectedAnalysisCategoryNode.IsSelected = true;
                _selectedAnalysisCategoryNode.IsExpanded = true;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedAnalysisDetail));
            OnPropertyChanged(nameof(IsGroundwaterCategorySelected));
            OnPropertyChanged(nameof(IsGenericCategoryDetailSelected));

            if (value != null && !_syncingAnalysisTreeSelection)
            {
                ClearTypeNodeSelectionHighlights();
                _syncingAnalysisTreeSelection = true;
                try
                {
                    _selectedAnalysisTreeNode = null;
                    OnPropertyChanged(nameof(SelectedAnalysisTreeNode));
                    OnPropertyChanged(nameof(IsGroundwaterStaticDetailSelected));
                    if (_materialPropertiesVM != null)
                        _materialPropertiesVM.SelectedAnalysisCategory = value.Name;
                }
                finally
                {
                    _syncingAnalysisTreeSelection = false;
                }
                NotifyGroundwaterStaticBindings();
            }
        }
    }

    /// <summary>Currently selected type node in the Analysis tree; drives type-specific right detail panels.</summary>
    public AnalysisTypeNode? SelectedAnalysisTreeNode
    {
        get => _selectedAnalysisTreeNode;
        set
        {
            if (ReferenceEquals(_selectedAnalysisTreeNode, value))
                return;
            if (value is { IsEnabled: false })
                return;
            _selectedAnalysisTreeNode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedAnalysisDetail));
            OnPropertyChanged(nameof(IsGroundwaterStaticDetailSelected));
            OnPropertyChanged(nameof(IsGroundwaterNonStaticTypeSelected));
            OnPropertyChanged(nameof(AreGroundwaterTypeOptionsEnabled));
            NotifySeepageDetailBindings();

            if (value != null && !_syncingAnalysisTreeSelection && _materialPropertiesVM != null)
            {
                if (_selectedAnalysisCategoryNode != null)
                {
                    _selectedAnalysisCategoryNode.IsSelected = false;
                    _selectedAnalysisCategoryNode = null;
                    OnPropertyChanged(nameof(SelectedAnalysisCategoryNode));
                    OnPropertyChanged(nameof(IsGroundwaterCategorySelected));
                    OnPropertyChanged(nameof(IsGenericCategoryDetailSelected));
                }

                _syncingAnalysisTreeSelection = true;
                try
                {
                    _materialPropertiesVM.SelectedAnalysisCategory = value.Category;
                    if (MaterialPropertiesViewModel.IsGroundwaterSteadyStateTreeType(value.Name))
                    {
                        // Steady State is a UI group — keep or default a selectable seepage solver id.
                        // p72 Plane/axisymmetric is retired from Project Settings; migrate to General 2D.
                        var current = _materialPropertiesVM.SelectedDetailedAnalysisType;
                        if (!MaterialPropertiesViewModel.IsSelectableSteadyStateSolver(current))
                        {
                            var remembered = _materialPropertiesVM.LastSteadyStateSolverType;
                            _materialPropertiesVM.SelectedDetailedAnalysisType =
                                MaterialPropertiesViewModel.IsSelectableSteadyStateSolver(remembered)
                                    ? remembered!
                                    : ResolveDefaultSteadyStateSolver();
                        }
                        _materialPropertiesVM.RememberGroundwaterTreeMode(
                            _materialPropertiesVM.SelectedDetailedAnalysisType);
                    }
                    else if (MaterialPropertiesViewModel.IsGroundwaterTransientStateTreeType(value.Name))
                    {
                        var current = _materialPropertiesVM.SelectedDetailedAnalysisType;
                        if (!MaterialPropertiesViewModel.IsSelectableTransientSolver(current))
                        {
                            var remembered = _materialPropertiesVM.LastTransientSolverType;
                            _materialPropertiesVM.SelectedDetailedAnalysisType =
                                MaterialPropertiesViewModel.IsSelectableTransientSolver(remembered)
                                    ? remembered!
                                    : MaterialPropertiesViewModel.GroundwaterTransientPlaneAxisymType;
                        }
                        _materialPropertiesVM.RememberGroundwaterTreeMode(
                            _materialPropertiesVM.SelectedDetailedAnalysisType);
                    }
                    else
                    {
                        _materialPropertiesVM.SelectedDetailedAnalysisType = value.Name;
                        if (MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(value.Category))
                            _materialPropertiesVM.RememberGroundwaterTreeMode(value.Name);
                    }
                }
                finally
                {
                    _syncingAnalysisTreeSelection = false;
                }
                NotifyGroundwaterStaticBindings();
                NotifySteadyStateCheckboxBindings();
                NotifyTransientCheckboxBindings();
            }
            else if (value == null)
            {
                OnPropertyChanged(nameof(IsGroundwaterCategorySelected));
            }
        }
    }

    /// <summary>Rebuilds the Analysis tree from the current categories and dimension-aware types.</summary>
    public void RebuildAnalysisTree()
    {
        var selectedCategory = _materialPropertiesVM?.SelectedAnalysisCategory;
        var selectedType = _materialPropertiesVM?.SelectedDetailedAnalysisType;
        var keepCategoryPanel = IsGroundwaterCategorySelected
            && MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(selectedCategory);

        AnalysisTree.Clear();
        if (_materialPropertiesVM == null)
        {
            _syncingAnalysisTreeSelection = true;
            try
            {
                SelectedAnalysisCategoryNode = null;
                SelectedAnalysisTreeNode = null;
            }
            finally
            {
                _syncingAnalysisTreeSelection = false;
            }
            NotifyAnalysisDetailBindings();
            return;
        }

        foreach (var category in _materialPropertiesVM.AnalysisCategories)
        {
            var node = new AnalysisCategoryNode(category) { IsOn = IsCategoryOn(category) };
            node.PropertyChanged += OnAnalysisCategoryNodePropertyChanged;
            foreach (var type in _materialPropertiesVM.GetAnalysisTreeTypesForCategory(category))
            {
                var enabled = !MaterialPropertiesViewModel.IsGroundwaterFutureTreeType(type);
                node.Types.Add(new AnalysisTypeNode(
                    category,
                    type,
                    isEnabled: enabled,
                    disabledReason: enabled ? null : "Not in this release"));
            }
            AnalysisTree.Add(node);
        }

        if (keepCategoryPanel)
            SelectGroundwaterCategoryNode();
        else
            SyncAnalysisTreeSelection(selectedCategory, selectedType);
    }

    private void OnAnalysisCategoryNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AnalysisCategoryNode.IsOn) || sender is not AnalysisCategoryNode node)
            return;
        if (_syncingCategoryOnFromGroundwater) return;
        SetCategoryOn(node.Name, node.IsOn);
    }

    /// <summary>Selects the Flow-Seepage/Groundwater category node for its dedicated detail panel.</summary>
    public void SelectGroundwaterCategoryNode()
    {
        var node = AnalysisTree.FirstOrDefault(c =>
            MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(c.Name));
        if (node == null)
            return;
        SelectedAnalysisCategoryNode = node;
    }

    private void ClearTypeNodeSelectionHighlights()
    {
        foreach (var categoryNode in AnalysisTree)
        {
            foreach (var typeNode in categoryNode.Types)
                typeNode.IsSelected = false;
        }
    }

    private void SyncAnalysisTreeSelection(string? category, string? detailedType)
    {
        category = MaterialPropertiesViewModel.NormalizeAnalysisCategory(category);
        detailedType = MaterialPropertiesViewModel.NormalizeDetailedAnalysisType(detailedType);
        // Seepage solvers live under Steady State; transient under Transient State.
        var treeTypeName = MaterialPropertiesViewModel.IsSeepageFlowAnalysisType(detailedType)
            ? MaterialPropertiesViewModel.GroundwaterSteadyStateType
            : MaterialPropertiesViewModel.IsTransientFlowAnalysisType(detailedType)
                ? MaterialPropertiesViewModel.GroundwaterTransientStateType
                : detailedType;

        AnalysisTypeNode? match = null;
        foreach (var categoryNode in AnalysisTree)
        {
            categoryNode.IsSelected = false;
            foreach (var typeNode in categoryNode.Types)
            {
                var isMatch = string.Equals(categoryNode.Name, category, StringComparison.Ordinal)
                    && string.Equals(typeNode.Name, treeTypeName, StringComparison.Ordinal);
                typeNode.IsSelected = isMatch;
                if (isMatch)
                {
                    match = typeNode;
                    categoryNode.IsExpanded = true;
                }
            }
        }

        _syncingAnalysisTreeSelection = true;
        try
        {
            if (_selectedAnalysisCategoryNode != null)
            {
                _selectedAnalysisCategoryNode.IsSelected = false;
                _selectedAnalysisCategoryNode = null;
                OnPropertyChanged(nameof(SelectedAnalysisCategoryNode));
            }
            SelectedAnalysisTreeNode = match;
        }
        finally
        {
            _syncingAnalysisTreeSelection = false;
        }
        NotifyAnalysisDetailBindings();
        NotifySteadyStateCheckboxBindings();
        NotifyTransientCheckboxBindings();
    }

    private string ResolveDefaultSteadyStateSolver()
    {
        if (_materialPropertiesVM?.Is3DProblem == true)
            return MaterialPropertiesViewModel.GroundwaterSeepageGeneral3DType;
        return MaterialPropertiesViewModel.GroundwaterSeepageGeneralType;
    }

    /// <summary>Activate a steady seepage solver from a Steady State checkbox (mutually exclusive).</summary>
    private void ActivateSteadyStateSolver(string solverType)
    {
        if (_materialPropertiesVM == null) return;

        if (!MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(_materialPropertiesVM.SelectedAnalysisCategory))
            _materialPropertiesVM.SelectedAnalysisCategory = MaterialPropertiesViewModel.GroundwaterAnalysisCategory;

        if (!string.Equals(_materialPropertiesVM.SelectedDetailedAnalysisType, solverType, StringComparison.Ordinal))
        {
            _syncingAnalysisTreeSelection = true;
            try
            {
                _materialPropertiesVM.SelectedDetailedAnalysisType = solverType;
            }
            finally
            {
                _syncingAnalysisTreeSelection = false;
            }
        }
        _materialPropertiesVM.RememberGroundwaterTreeMode(solverType);

        // Ensure Steady State tree leaf is selected for the detail panel.
        if (!IsSteadyStateDetailSelected)
            SelectGroundwaterTreeLeaf(MaterialPropertiesViewModel.GroundwaterSteadyStateType);

        NotifySeepageDetailBindings();
        NotifySteadyStateCheckboxBindings();
        NotifyGroundwaterStaticBindings();
        _feAnalysisVM?.RefreshResolvedRunRoutingDisplay();
    }

    /// <summary>Activate a transient consolidation solver from a Transient State checkbox (mutually exclusive).</summary>
    private void ActivateTransientSolver(string solverType)
    {
        if (_materialPropertiesVM == null) return;

        if (!MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(_materialPropertiesVM.SelectedAnalysisCategory))
            _materialPropertiesVM.SelectedAnalysisCategory = MaterialPropertiesViewModel.GroundwaterAnalysisCategory;

        if (!string.Equals(_materialPropertiesVM.SelectedDetailedAnalysisType, solverType, StringComparison.Ordinal))
        {
            _syncingAnalysisTreeSelection = true;
            try
            {
                _materialPropertiesVM.SelectedDetailedAnalysisType = solverType;
            }
            finally
            {
                _syncingAnalysisTreeSelection = false;
            }
        }
        _materialPropertiesVM.RememberGroundwaterTreeMode(solverType);

        if (!IsTransientStateDetailSelected)
            SelectGroundwaterTreeLeaf(MaterialPropertiesViewModel.GroundwaterTransientStateType);

        NotifyTransientDetailBindings();
        NotifyTransientCheckboxBindings();
        NotifyGroundwaterStaticBindings();
        _feAnalysisVM?.RefreshResolvedRunRoutingDisplay();
    }

    private void SelectGroundwaterTreeLeaf(string treeLeafName)
    {
        var match = AnalysisTree
            .SelectMany(c => c.Types.Select(t => (Category: c, Type: t)))
            .FirstOrDefault(x =>
                MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(x.Category.Name)
                && string.Equals(x.Type.Name, treeLeafName, StringComparison.Ordinal));
        if (match.Type == null) return;

        foreach (var categoryNode in AnalysisTree)
        {
            categoryNode.IsSelected = false;
            foreach (var typeNode in categoryNode.Types)
                typeNode.IsSelected = ReferenceEquals(typeNode, match.Type);
            if (ReferenceEquals(categoryNode, match.Category))
                categoryNode.IsExpanded = true;
        }
        _syncingAnalysisTreeSelection = true;
        try
        {
            if (_selectedAnalysisCategoryNode != null)
            {
                _selectedAnalysisCategoryNode.IsSelected = false;
                _selectedAnalysisCategoryNode = null;
                OnPropertyChanged(nameof(SelectedAnalysisCategoryNode));
            }
            _selectedAnalysisTreeNode = match.Type;
            OnPropertyChanged(nameof(SelectedAnalysisTreeNode));
            OnPropertyChanged(nameof(SelectedAnalysisDetail));
        }
        finally
        {
            _syncingAnalysisTreeSelection = false;
        }
    }

    private void NotifyAnalysisDetailBindings()
    {
        OnPropertyChanged(nameof(SelectedAnalysisDetail));
        OnPropertyChanged(nameof(IsGroundwaterCategorySelected));
        OnPropertyChanged(nameof(IsGenericCategoryDetailSelected));
        OnPropertyChanged(nameof(IsGroundwaterStaticDetailSelected));
        OnPropertyChanged(nameof(IsGroundwaterNonStaticTypeSelected));
        OnPropertyChanged(nameof(AreGroundwaterTypeOptionsEnabled));
        OnPropertyChanged(nameof(AreSelectedCategoryTypeOptionsEnabled));
        NotifySeepageDetailBindings();
        NotifyWaterAndEngineBindings();
    }

    private void NotifySeepageDetailBindings()
    {
        OnPropertyChanged(nameof(IsSteadyStateDetailSelected));
        OnPropertyChanged(nameof(IsSeepageDetailSelected));
        OnPropertyChanged(nameof(IsSeepageAnalysisSelected));
        OnPropertyChanged(nameof(IsActiveSeepageWaterHeadEnabled));
        OnPropertyChanged(nameof(IsFreeSurfaceSeepageSelected));
        OnPropertyChanged(nameof(IsSteadyState2DPanelVisible));
        OnPropertyChanged(nameof(IsSteadyState3DPanelVisible));
        OnPropertyChanged(nameof(SeepageSolverNote));
        OnPropertyChanged(nameof(AreSelectedCategoryTypeOptionsEnabled));
        OnPropertyChanged(nameof(IsActiveAnalysisCategoryOn));
        NotifyTransientDetailBindings();
    }

    private void NotifyTransientDetailBindings()
    {
        OnPropertyChanged(nameof(IsTransientStateDetailSelected));
        OnPropertyChanged(nameof(IsTransientAnalysisSelected));
        OnPropertyChanged(nameof(IsTransientP84Selected));
        OnPropertyChanged(nameof(IsTransientP88Selected));
        OnPropertyChanged(nameof(TransientSolverNote));
        OnPropertyChanged(nameof(AreSelectedCategoryTypeOptionsEnabled));
        OnPropertyChanged(nameof(IsActiveAnalysisCategoryOn));
    }

    private void NotifySteadyStateCheckboxBindings()
    {
        OnPropertyChanged(nameof(IsSteadyStateFreeSurfaceChecked));
        OnPropertyChanged(nameof(IsSteadyStateGeneral2DChecked));
        OnPropertyChanged(nameof(IsSteadyStateGeneral3DChecked));
        OnPropertyChanged(nameof(IsFreeSurfaceSeepageSelected));
        OnPropertyChanged(nameof(SeepageSolverNote));
        OnPropertyChanged(nameof(IsSeepageAnalysisSelected));
        OnPropertyChanged(nameof(IsActiveSeepageWaterHeadEnabled));
    }

    private void NotifyTransientCheckboxBindings()
    {
        OnPropertyChanged(nameof(IsTransientP84Checked));
        OnPropertyChanged(nameof(IsTransientP88Checked));
        OnPropertyChanged(nameof(IsTransientP84Selected));
        OnPropertyChanged(nameof(IsTransientP88Selected));
        OnPropertyChanged(nameof(IsTransientAnalysisSelected));
        OnPropertyChanged(nameof(TransientSolverNote));
    }

    private void OnSeepageSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SeepageAnalysisSettings.IncludeWaterHeadPlaneAxisym)
            or nameof(SeepageAnalysisSettings.IncludeWaterHeadFreeSurface)
            or nameof(SeepageAnalysisSettings.IncludeWaterHeadGeneral2D)
            or nameof(SeepageAnalysisSettings.IncludeWaterHeadGeneral3D))
        {
            OnPropertyChanged(nameof(IsActiveSeepageWaterHeadEnabled));
            OnPropertyChanged(nameof(SeepageSettings));
        }
    }

    /// <summary>Set with <see cref="SetAxisSyncTargets"/>; binds Analysis category/type in Project Settings → Analysis Type.</summary>
    public MaterialPropertiesViewModel? MaterialPropertiesVM => _materialPropertiesVM;

    private GroundwaterViewModel? _groundwaterVM;

    /// <summary>Set by MainWindowViewModel; binds Include Groundwater in the Flow-Seepage/Groundwater detail panel.</summary>
    public GroundwaterViewModel? GroundwaterVM => _groundwaterVM;

    public void SetGroundwaterViewModel(GroundwaterViewModel groundwaterVM)
    {
        if (_groundwaterVM != null)
            _groundwaterVM.PropertyChanged -= OnGroundwaterVmPropertyChanged;

        _groundwaterVM = groundwaterVM;

        if (_groundwaterVM != null)
            _groundwaterVM.PropertyChanged += OnGroundwaterVmPropertyChanged;

        OnPropertyChanged(nameof(GroundwaterVM));
        OnPropertyChanged(nameof(AreGroundwaterTypeOptionsEnabled));
        var gwNode = AnalysisTree.FirstOrDefault(c =>
            MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(c.Name));
        if (gwNode != null)
            gwNode.IsOn = groundwaterVM.IsEnabled;
        NotifyWaterAndEngineBindings();
    }

    private void OnGroundwaterVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GroundwaterViewModel.IsEnabled)
            or nameof(GroundwaterViewModel.IsGroundwaterEnabled))
        {
            if (!_syncingCategoryOnFromGroundwater)
            {
                _categoryOnByName[MaterialPropertiesViewModel.GroundwaterAnalysisCategory] =
                    GroundwaterVM?.IsEnabled == true;
                var gwNode = AnalysisTree.FirstOrDefault(c =>
                    MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(c.Name));
                if (gwNode != null && gwNode.IsOn != (GroundwaterVM?.IsEnabled == true))
                {
                    _syncingCategoryOnFromGroundwater = true;
                    try { gwNode.IsOn = GroundwaterVM?.IsEnabled == true; }
                    finally { _syncingCategoryOnFromGroundwater = false; }
                }
            }
            NotifyCategoryOnBindings();
        }
    }

    /// <summary>Set with <see cref="SetFeAnalysisViewModel"/>; binds staged analysis in Project Settings → Analysis Type.</summary>
    public FEAnalysisViewModel? FEAnalysisVM => _feAnalysisVM;

    public string SelectedDimensionLabel => _selectedDimension switch
    {
        ProblemDimension.OneD => "1D Problem",
        ProblemDimension.TwoD => "2D Problem",
        ProblemDimension.ThreeD => "3D Problem",
        _ => _selectedDimension.ToString()
    };

    /// <summary>Called by MainWindowViewModel to enable Apply axis → all canvases.</summary>
    public void SetAxisSyncTargets(GeometryDefinitionViewModel geometryDefinitionVM, MaterialPropertiesViewModel materialPropertiesVM)
    {
        if (_materialPropertiesVM != null)
            _materialPropertiesVM.PropertyChanged -= OnMaterialPropertiesVmPropertyChanged;

        _geometryDefinitionVM = geometryDefinitionVM;
        _materialPropertiesVM = materialPropertiesVM;

        if (_materialPropertiesVM != null)
            _materialPropertiesVM.PropertyChanged += OnMaterialPropertiesVmPropertyChanged;

        OnPropertyChanged(nameof(MaterialPropertiesVM));
        RebuildAnalysisTree();
        NotifyGroundwaterStaticBindings();
        // Initialize Grid & Axis fields from current canvas limits
        Project.AxisXMin = geometryDefinitionVM.AxisXMin;
        Project.AxisXMax = geometryDefinitionVM.AxisXMax;
        Project.AxisYMin = geometryDefinitionVM.AxisYMin;
        Project.AxisYMax = geometryDefinitionVM.AxisYMax;
        RebuildFieldStationMaterialChoices();
        RefreshFieldStationCanvas();
    }

    /// <summary>Wires FE analysis options (e.g. staged excavation) into Project Settings.</summary>
    public void SetFeAnalysisViewModel(FEAnalysisViewModel feAnalysisVM)
    {
        if (_feAnalysisVM != null)
        {
            _feAnalysisVM.PropertyChanged -= OnFeAnalysisVmPropertyChanged;
            _feAnalysisVM.StageDefinitions.CollectionChanged -= OnStageDefinitionsCollectionChanged;
        }

        _feAnalysisVM = feAnalysisVM;

        if (_feAnalysisVM != null)
        {
            _feAnalysisVM.PropertyChanged += OnFeAnalysisVmPropertyChanged;
            _feAnalysisVM.StageDefinitions.CollectionChanged += OnStageDefinitionsCollectionChanged;
        }

        NotifyStagedAnalysisBindings();
    }

    /// <summary>Rebind staged-analysis UI (File → Project Settings popup creates a fresh view).</summary>
    public void RefreshStagedAnalysisBindings() => NotifyStagedAnalysisBindings();

    /// <summary>Project Settings → Analysis Type → Enable Staged Analysis (passthrough to <see cref="FEAnalysisViewModel"/>).</summary>
    public bool StagedExcavationEnabled
    {
        get => _feAnalysisVM?.StagedExcavationEnabled ?? false;
        set
        {
            if (_feAnalysisVM == null || _feAnalysisVM.StagedExcavationEnabled == value)
                return;
            _feAnalysisVM.StagedExcavationEnabled = value;
            OnPropertyChanged(nameof(StagedExcavationEnabled));
        }
    }

    public ObservableCollection<StageDefinition>? StageDefinitions => _feAnalysisVM?.StageDefinitions;

    public int StageDefinitionCount => _feAnalysisVM?.StageDefinitionCount ?? 0;

    public StageDefinition? SelectedStageDefinition
    {
        get => _feAnalysisVM?.SelectedStageDefinition;
        set
        {
            if (_feAnalysisVM == null) return;
            _feAnalysisVM.SelectedStageDefinition = value;
        }
    }

    /// <summary>Stage DataGrid is collapsed by default; enable + count stay visible.</summary>
    public bool IsStageEditorExpanded
    {
        get => _isStageEditorExpanded;
        set
        {
            if (_isStageEditorExpanded == value) return;
            _isStageEditorExpanded = value;
            OnPropertyChanged();
        }
    }

    public ICommand? AddStageDefinitionCommand => _feAnalysisVM?.AddStageDefinitionCommand;
    public ICommand? RemoveStageDefinitionCommand => _feAnalysisVM?.RemoveStageDefinitionCommand;
    public ICommand? MoveStageUpCommand => _feAnalysisVM?.MoveStageUpCommand;
    public ICommand? MoveStageDownCommand => _feAnalysisVM?.MoveStageDownCommand;

    private void OnFeAnalysisVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FEAnalysisViewModel.StagedExcavationEnabled):
                OnPropertyChanged(nameof(StagedExcavationEnabled));
                break;
            case nameof(FEAnalysisViewModel.StageDefinitionCount):
                OnPropertyChanged(nameof(StageDefinitionCount));
                break;
            case nameof(FEAnalysisViewModel.SelectedStageDefinition):
                OnPropertyChanged(nameof(SelectedStageDefinition));
                break;
            case nameof(FEAnalysisViewModel.ActiveAnalysisModeSummary):
            case nameof(FEAnalysisViewModel.ResolvedSolverForRun):
                OnPropertyChanged(nameof(AnalysisEngineSummary));
                OnPropertyChanged(nameof(ResolvedAnalysisKind));
                break;
        }
    }

    private void OnMaterialPropertiesVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MaterialPropertiesViewModel.SelectedDetailedAnalysisType)
            or nameof(MaterialPropertiesViewModel.SelectedAnalysisCategory))
        {
            if (!_syncingAnalysisTreeSelection)
            {
                SyncAnalysisTreeSelection(
                    _materialPropertiesVM?.SelectedAnalysisCategory,
                    _materialPropertiesVM?.SelectedDetailedAnalysisType);
            }
            NotifyGroundwaterStaticBindings();
            NotifySeepageDetailBindings();
            NotifySteadyStateCheckboxBindings();
            NotifyWaterAndEngineBindings();
        }
        else if (e.PropertyName is nameof(MaterialPropertiesViewModel.LastGroundwaterTreeMode))
        {
            NotifyWaterAndEngineBindings();
        }
    }

    private void OnStageDefinitionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(StageDefinitionCount));

    private void NotifyStagedAnalysisBindings()
    {
        OnPropertyChanged(nameof(FEAnalysisVM));
        OnPropertyChanged(nameof(StagedExcavationEnabled));
        OnPropertyChanged(nameof(StageDefinitions));
        OnPropertyChanged(nameof(StageDefinitionCount));
        OnPropertyChanged(nameof(SelectedStageDefinition));
        OnPropertyChanged(nameof(AddStageDefinitionCommand));
        OnPropertyChanged(nameof(RemoveStageDefinitionCommand));
        OnPropertyChanged(nameof(MoveStageUpCommand));
        OnPropertyChanged(nameof(MoveStageDownCommand));
    }

    public ProjectSettingsModel Project { get; }
    public IList<string> AvailableUnits { get; }

    public ICommand ApplyAxisToAllCanvasesCommand { get; }

    public bool IsDeterministicAnalysis { get => _isDeterministicAnalysis; set { _isDeterministicAnalysis = value; OnPropertyChanged(); } }
    public bool IsProbabilisticAnalysis { get => _isProbabilisticAnalysis; set { _isProbabilisticAnalysis = value; OnPropertyChanged(); } }

    public ProblemDimension SelectedDimension
    {
        get => _selectedDimension;
        set
        {
            if (_selectedDimension == value)
                return;
            _selectedDimension = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedDimensionLabel));
            // MaterialPropertiesViewModel reacts to this change synchronously (dimension-aware
            // type lists), so the tree can be rebuilt against the refreshed lists here.
            RebuildAnalysisTree();
        }
    }

    public void SelectSettingsTab(ProjectSettingsTabKind kind)
    {
        for (var i = 0; i < SettingsTabs.Count; i++)
        {
            if (SettingsTabs[i].Kind == kind)
            {
                SelectedSettingsTabIndex = i;
                return;
            }
        }
    }

    public void ClampSelectedSettingsTabIndex()
    {
        if (SettingsTabs.Count == 0)
        {
            _selectedSettingsTabIndex = 0;
            OnPropertyChanged(nameof(SelectedSettingsTabIndex));
            return;
        }

        var clamped = Math.Clamp(_selectedSettingsTabIndex, 0, SettingsTabs.Count - 1);
        if (_selectedSettingsTabIndex != clamped)
        {
            _selectedSettingsTabIndex = clamped;
            OnPropertyChanged(nameof(SelectedSettingsTabIndex));
        }
    }

    /// <summary>Copies geometry-definition axis limits into Project Settings (after mesh boundary import).</summary>
    public void PullAxisFromGeometry()
    {
        if (_geometryDefinitionVM == null) return;
        Project.AxisXMin = _geometryDefinitionVM.AxisXMin;
        Project.AxisXMax = _geometryDefinitionVM.AxisXMax;
        Project.AxisYMin = _geometryDefinitionVM.AxisYMin;
        Project.AxisYMax = _geometryDefinitionVM.AxisYMax;
        OnPropertyChanged(nameof(Project));
    }

    private void ApplyAxisToAllCanvases()
    {
        if (_geometryDefinitionVM == null || _materialPropertiesVM?.GeometryDefinition == null)
            return;
        // Shared Geometry + Materials GeometryDefinition drive Geometry/Mesh/BC/Load/GW/FE Analysis canvases.
        _geometryDefinitionVM.AxisXMin = Project.AxisXMin;
        _geometryDefinitionVM.AxisXMax = Project.AxisXMax;
        _geometryDefinitionVM.AxisYMin = Project.AxisYMin;
        _geometryDefinitionVM.AxisYMax = Project.AxisYMax;
        var mat = _materialPropertiesVM.GeometryDefinition;
        mat.AxisXMin = Project.AxisXMin;
        mat.AxisXMax = Project.AxisXMax;
        mat.AxisYMin = Project.AxisYMin;
        mat.AxisYMax = Project.AxisYMax;
        _geometryDefinitionVM.SyncGridSpacing(Project.GridSpacingMeters);
        mat.SyncGridSpacing(Project.GridSpacingMeters);
        _geometryDefinitionVM.ShowGrid = Project.ShowGrid;
        mat.ShowGrid = Project.ShowGrid;
        _geometryDefinitionVM.ShowAxes = Project.ShowAxes;
        mat.ShowAxes = Project.ShowAxes;
    }

    public void UpdateStageFooterTabs(IEnumerable<(int StageNumber, string StageName)> stages)
    {
        var list = new List<StageFooterTab>();
        foreach (var (num, name) in stages)
            list.Add(new StageFooterTab { StageNumber = num, StageName = name });
        StageFooterTabs = list;
    }

    public void ClearStageFooterTabs()
    {
        StageFooterTabs = new List<StageFooterTab>();
        NumberOfStages = 0;
        Project.NumberOfStages = 0;
        SelectedStageFooterTabIndex = null;
    }

    private int _numberOfStages;
    private int? _selectedStageFooterTabIndex;
    private List<StageFooterTab> _stageFooterTabs = new();

    public int NumberOfStages
    {
        get => _numberOfStages;
        set { _numberOfStages = value; OnPropertyChanged(); }
    }

    public int? SelectedStageFooterTabIndex
    {
        get => _selectedStageFooterTabIndex;
        set { _selectedStageFooterTabIndex = value; OnPropertyChanged(); }
    }

    public List<StageFooterTab> StageFooterTabs
    {
        get => _stageFooterTabs;
        set { _stageFooterTabs = value; OnPropertyChanged(); }
    }

    private void InitializeSettingsTabs()
    {
        SettingsTabs.Clear();
        SettingsTabs.Add(new ProjectSettingsTabDescriptor
        {
            Kind = ProjectSettingsTabKind.General,
            Header = "General"
        });
        SettingsTabs.Add(new ProjectSettingsTabDescriptor
        {
            Kind = ProjectSettingsTabKind.AnalysisType,
            Header = "Analysis Type"
        });
        SettingsTabs.Add(new ProjectSettingsTabDescriptor
        {
            Kind = ProjectSettingsTabKind.GridAxes,
            Header = "Grid & Axes"
        });
        SettingsTabs.Add(new ProjectSettingsTabDescriptor
        {
            Kind = ProjectSettingsTabKind.FieldStations,
            Header = "Field Stations"
        });
        SettingsTabs.Add(new ProjectSettingsTabDescriptor
        {
            Kind = ProjectSettingsTabKind.Appearance,
            Header = "Appearance"
        });
    }

    private void OnGroundwaterStaticSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => NotifyGroundwaterStaticBindings();

    public void NotifyGroundwaterStaticBindings()
    {
        OnPropertyChanged(nameof(IsGroundwaterStaticSelected));
        OnPropertyChanged(nameof(IsGroundwaterCategorySelected));
        OnPropertyChanged(nameof(IsGenericCategoryDetailSelected));
        OnPropertyChanged(nameof(IsGroundwaterStaticDetailSelected));
        OnPropertyChanged(nameof(IsSteadyStateDetailSelected));
        OnPropertyChanged(nameof(IsSeepageDetailSelected));
        OnPropertyChanged(nameof(IsGroundwaterNonStaticTypeSelected));
        OnPropertyChanged(nameof(AreGroundwaterTypeOptionsEnabled));
        OnPropertyChanged(nameof(SelectedAnalysisDetail));
        OnPropertyChanged(nameof(GroundwaterStaticSettings));
        NotifyWaterAndEngineBindings();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class StageFooterTab
{
    public int StageNumber { get; set; }
    public string StageName { get; set; } = "";
}
