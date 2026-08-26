using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Services;
using GeotechSlopeApp.ViewModels.Dialogs;
using GeotechSlopeApp.Views.Dialogs;

namespace GeotechSlopeApp.ViewModels;

public class GroundwaterViewModel : INotifyPropertyChanged
{
    private bool _isEnabled;
    private double _gammaW = 9.81;
    private string _waterTableInput = "";
    private GroundwaterDrawMode _drawMode = GroundwaterDrawMode.Horizontal;
    private GroundwaterMethod _method = GroundwaterMethod.Hydrostatic;
    private double _ru;
    private double _bbar;
    private string _polylineStatus = "";
    private bool _isPolylineDrawActive;
    private GroundwaterSurfPoint? _selectedSurfPoint;
    private ScriptViewModel? _script;
    private IProjectUndoHost? _undoHost;
    private ProjectSettingsViewModel? _projectSettings;
    private MaterialPropertiesViewModel? _materialProperties;
    private bool _showSaturatedMaterialZone = true;
    private bool _showUnsaturatedMaterialZone;
    private bool _showPondedFreeWaterRegion = true;
    private PondedWaterDisplayPattern _pondedWaterDisplayPattern = PondedWaterDisplayPattern.DepthGradient;
    private bool _showPondedFacePressureArrows = true;
    private bool _showPondedHeadIsobars = true;
    private PondedWaterLevelMethod _pondedWaterLevelMethod = PondedWaterLevelMethod.PhreaticLine;
    private double _pondedTotalHead;
    private MeshViewModel? _mesh;
    private WaterHeadModel? _selectedWaterHead;
    private WaterHeadModel? _linePlacementAwaitingEnd;
    private bool _syncingWaterHeadDraft;
    /// <summary>When true, form edits push into the selected grid row (set by picking a row).</summary>
    private bool _waterHeadDraftEditsSelection;
    private string _waterHeadPlacementStatus = "";
    private bool _syncingSeepageHeadFromRow;
    private TransientFixedPressureNode? _selectedTransientFixedPressureNode;
    private TransientFixedPressureNode? _transientLinePlacementAwaitingEnd;
    private bool _syncingTransientFixedPressureDraft;
    private bool _transientFixedPressureDraftEditsSelection;
    private string _transientFixedPressurePlacementStatus =
        "UserDefined: pick a drain face. MouseSelect: place a Line on the canvas, then Add.";
    private bool _isTransientBulkFaceSectionEnabled = true;
    private bool _isTransientInteractiveBcSectionEnabled;
    private bool _isTransientNodeSelectionEnabled;
    private bool _suppressTransientSectionSync;
    private string _transientNodeSelectionIdsText = "";
    private double _transientNodeSelectionPressure;
    private string _transientNodeSelectionStatus =
        "Enable node selection to type Node ID(s) or drag a box on the canvas.";
    private readonly List<(int NodeId, Point Axis)> _transientNodeSelectionPreview = new();

    private enum TransientBcUiSection
    {
        None,
        BulkFaces,
        InteractiveBcs,
        NodeSelection,
    }

    public sealed record PondedWaterPatternOption(PondedWaterDisplayPattern Pattern, string DisplayName);

    public IReadOnlyList<PondedWaterPatternOption> AvailablePondedWaterPatterns { get; } =
    [
        new(PondedWaterDisplayPattern.DepthGradient, "Depth gradient"),
        new(PondedWaterDisplayPattern.Solid, "Solid fill"),
        new(PondedWaterDisplayPattern.DiagonalHatch, "Diagonal hatch"),
        new(PondedWaterDisplayPattern.CrossHatch, "Cross hatch"),
        new(PondedWaterDisplayPattern.Waves, "Wave lines"),
        new(PondedWaterDisplayPattern.Dots, "Dot stipple"),
    ];

    public void SetUndoHost(IProjectUndoHost? host) => _undoHost = host;
    public void SetMaterialPropertiesViewModel(MaterialPropertiesViewModel? materialProperties)
    {
        if (_materialProperties != null)
            _materialProperties.PropertyChanged -= OnMaterialPropertiesPropertyChanged;

        _materialProperties = materialProperties;

        if (_materialProperties != null)
            _materialProperties.PropertyChanged += OnMaterialPropertiesPropertyChanged;

        OnPropertyChanged(nameof(Materials));
        OnPropertyChanged(nameof(IsTransientBcAllowed));
        OnPropertyChanged(nameof(IsPhreaticEditorAllowed));
        OnPropertyChanged(nameof(IsPondedFreeWaterAllowed));
        OnPropertyChanged(nameof(TransientSettings));
    }

    /// <summary>Live, read-only-in-this-popup material source for the groundwater summary table.</summary>
    public ObservableCollection<MaterialModel>? Materials => _materialProperties?.Materials;

    public void SetProjectSettingsViewModel(ProjectSettingsViewModel? projectSettings)
    {
        if (_projectSettings != null)
        {
            _projectSettings.PropertyChanged -= OnProjectSettingsPropertyChanged;
            _projectSettings.Project.PropertyChanged -= OnProjectModelPropertyChanged;
            _projectSettings.GroundwaterStaticSettings.PropertyChanged -= OnGroundwaterStaticSettingsPropertyChanged;
            _projectSettings.SeepageSettings.PropertyChanged -= OnSeepageSettingsPropertyChanged;
            _projectSettings.TransientSettings.PropertyChanged -= OnTransientSettingsPropertyChanged;
        }

        _projectSettings = projectSettings;

        if (_projectSettings != null)
        {
            _projectSettings.PropertyChanged += OnProjectSettingsPropertyChanged;
            _projectSettings.Project.PropertyChanged += OnProjectModelPropertyChanged;
            _projectSettings.GroundwaterStaticSettings.PropertyChanged += OnGroundwaterStaticSettingsPropertyChanged;
            _projectSettings.SeepageSettings.PropertyChanged += OnSeepageSettingsPropertyChanged;
            _projectSettings.TransientSettings.PropertyChanged += OnTransientSettingsPropertyChanged;
        }

        RefreshStaticMethodAvailability();
        NotifyUnitSystemLabels();
        OnPropertyChanged(nameof(IsTransientBcAllowed));
        OnPropertyChanged(nameof(TransientSettings));
        if (_projectSettings != null)
            TransientFixedPressureDraft.Pressure = _projectSettings.TransientSettings.FixedPressure;
        UpdateTransientFixedPressurePlacementStatus();
        GeometryDefinition?.RefreshTransientPressureOverlays();
    }
    private void RecordUndo(string description) => _undoHost?.RecordUndoPoint(description);

    public GroundwaterViewModel()
    {
        AddWaterTableCommand = new RelayCommand(AddHorizontalWaterTable, () => IsEnabled && IsHydrostaticMode && IsPhreaticEditorAllowed && DrawMode == GroundwaterDrawMode.Horizontal);
        StartPolylineDrawCommand = new RelayCommand(StartPolylineDraw, () => IsEnabled && IsHydrostaticMode && IsPhreaticEditorAllowed && DrawMode == GroundwaterDrawMode.Polyline);
        FinishPolylineDrawCommand = new RelayCommand(FinishPolylineDraw, () => IsPolylineDrawActive);
        ClearGroundwaterCommand = new RelayCommand(ClearGroundwater, () => IsEnabled && IsHydrostaticMode && IsPhreaticEditorAllowed);
        RemoveSelectedSurfPointCommand = new RelayCommand(RemoveSelectedSurfPoint, () => SelectedSurfPoint != null);
        OpenWaterPressureGridDialogCommand = new RelayCommand<object?>(
            OpenWaterPressureGridDialog,
            _ => IsEnabled && IsAnyWaterPressureGridAllowed);
        AddWaterHeadCommand = new RelayCommand(OnAddWaterHead, () => IsWaterHeadAllowed);
        RemoveWaterHeadCommand = new RelayCommand(OnRemoveWaterHead, () => IsWaterHeadAllowed && SelectedWaterHead != null);
        WaterHeadRoles = Enum.GetValues<WaterHeadRole>();
        WaterHeadDefinitionMethods = Enum.GetValues<WaterHeadDefinitionMethod>();
        WaterHeadPlacementTypes = Enum.GetValues<WaterHeadPlacementType>();
        WaterHeadDraft = CreateDefaultWaterHeadDraft();
        WaterHeadDraft.PropertyChanged += OnWaterHeadDraftPropertyChanged;
        TransientFixedPressureDefinitionMethods = Enum.GetValues<WaterHeadDefinitionMethod>();
        TransientDrainFaces = Enum.GetValues<TransientDrainFace>();
        // Section 2 intentionally supports Line placement only. Point rows are added in section 3.
        TransientFixedPressurePlacementTypes = [WaterHeadPlacementType.Line];
        AddTransientFixedPressureNodeCommand = new RelayCommand(
            OnAddTransientFixedPressureNode,
            CanAddTransientFixedPressureNode);
        RemoveTransientFixedPressureNodeCommand = new RelayCommand(
            OnRemoveTransientFixedPressureNode,
            () => IsTransientBcAllowed && SelectedTransientFixedPressureNode != null);
        TransientFixedPressureDraft.PropertyChanged += OnTransientFixedPressureDraftPropertyChanged;
        TransientFixedPressureNodes.CollectionChanged += OnTransientFixedPressureNodesChanged;
        AddTransientNodeSelectionCommand = new RelayCommand(
            OnAddTransientNodeSelection,
            CanAddTransientNodeSelection);
        ResetTransientFixedPressureDraftAfterAdd();
    }

    public GeometryDefinitionViewModel? GeometryDefinition { get; set; }

    public void SetScriptViewModel(ScriptViewModel script) => _script = script;

    public void SetMeshViewModel(MeshViewModel? mesh)
    {
        _mesh = mesh;
        (AddTransientFixedPressureNodeCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    public ObservableCollection<WaterHeadModel> WaterHeads { get; } = new();

    /// <summary>Form editor for a water head; <see cref="AddWaterHeadCommand"/> commits this into <see cref="WaterHeads"/>.</summary>
    public WaterHeadModel WaterHeadDraft { get; }

    public IReadOnlyList<WaterHeadRole> WaterHeadRoles { get; }
    public IReadOnlyList<WaterHeadDefinitionMethod> WaterHeadDefinitionMethods { get; }
    public IReadOnlyList<WaterHeadPlacementType> WaterHeadPlacementTypes { get; }

    public ICommand AddWaterHeadCommand { get; }
    public ICommand RemoveWaterHeadCommand { get; }

    public ObservableCollection<TransientFixedPressureNode> TransientFixedPressureNodes { get; } = new();
    public TransientFixedPressureNode TransientFixedPressureDraft { get; } = new();
    public IReadOnlyList<WaterHeadDefinitionMethod> TransientFixedPressureDefinitionMethods { get; private set; } = [];
    public IReadOnlyList<TransientDrainFace> TransientDrainFaces { get; private set; } = [];
    public IReadOnlyList<WaterHeadPlacementType> TransientFixedPressurePlacementTypes { get; private set; } = [];
    public ICommand AddTransientFixedPressureNodeCommand { get; }
    public ICommand RemoveTransientFixedPressureNodeCommand { get; }

    public ICommand AddTransientNodeSelectionCommand { get; }

    /// <summary>UI accordion: section 1 (bulk faces). Presentation only — does not clear saved BCs.</summary>
    public bool IsTransientBulkFaceSectionEnabled
    {
        get => _isTransientBulkFaceSectionEnabled;
        set => SetTransientBcUiSection(TransientBcUiSection.BulkFaces, value);
    }

    /// <summary>UI accordion: section 2 (UserDefined / MouseSelect Line). Presentation only.</summary>
    public bool IsTransientInteractiveBcSectionEnabled
    {
        get => _isTransientInteractiveBcSectionEnabled;
        set => SetTransientBcUiSection(TransientBcUiSection.InteractiveBcs, value);
    }

    public bool IsTransientNodeSelectionEnabled
    {
        get => _isTransientNodeSelectionEnabled;
        set => SetTransientBcUiSection(TransientBcUiSection.NodeSelection, value);
    }

    private void SetTransientBcUiSection(TransientBcUiSection section, bool enabled)
    {
        if (_suppressTransientSectionSync) return;

        var target = enabled ? section : TransientBcUiSection.None;
        if (!enabled)
        {
            // Unchecking a section that is already off is a no-op.
            var currentlyOn = _isTransientBulkFaceSectionEnabled ? TransientBcUiSection.BulkFaces
                : _isTransientInteractiveBcSectionEnabled ? TransientBcUiSection.InteractiveBcs
                : _isTransientNodeSelectionEnabled ? TransientBcUiSection.NodeSelection
                : TransientBcUiSection.None;
            if (currentlyOn != section) return;
        }

        bool nextBulk = target == TransientBcUiSection.BulkFaces;
        bool nextInteractive = target == TransientBcUiSection.InteractiveBcs;
        bool nextNode = target == TransientBcUiSection.NodeSelection;
        if (nextBulk == _isTransientBulkFaceSectionEnabled
            && nextInteractive == _isTransientInteractiveBcSectionEnabled
            && nextNode == _isTransientNodeSelectionEnabled)
            return;

        _suppressTransientSectionSync = true;
        try
        {
            _isTransientBulkFaceSectionEnabled = nextBulk;
            _isTransientInteractiveBcSectionEnabled = nextInteractive;
            _isTransientNodeSelectionEnabled = nextNode;
        }
        finally
        {
            _suppressTransientSectionSync = false;
        }

        OnPropertyChanged(nameof(IsTransientBulkFaceSectionEnabled));
        OnPropertyChanged(nameof(IsTransientInteractiveBcSectionEnabled));
        OnPropertyChanged(nameof(IsTransientNodeSelectionEnabled));
        OnPropertyChanged(nameof(IsTransientFixedPressureMousePlacementActive));

        if (!nextInteractive)
        {
            _transientLinePlacementAwaitingEnd = null;
        }

        if (nextNode)
        {
            TransientNodeSelectionPressure =
                _projectSettings?.TransientSettings.FixedPressure
                ?? TransientFixedPressureDraft.Pressure;
            TransientNodeSelectionStatus =
                "Type Node ID(s), or drag a box / click a mesh node on the Groundwater canvas.";
        }
        else
        {
            ClearTransientNodeSelectionPreview();
            TransientNodeSelectionStatus =
                "Enable node selection to type Node ID(s) or drag a box on the canvas.";
            GeometryDefinition?.ClearTransientNodeSelectionRect();
        }

        UpdateTransientFixedPressurePlacementStatus();
        (AddTransientNodeSelectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (AddTransientFixedPressureNodeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        GeometryDefinition?.RefreshTransientPressureOverlays();
    }

    public string TransientNodeSelectionIdsText
    {
        get => _transientNodeSelectionIdsText;
        set
        {
            if (_transientNodeSelectionIdsText == value) return;
            _transientNodeSelectionIdsText = value ?? "";
            OnPropertyChanged();
            SyncTransientNodeSelectionPreviewFromText();
            (AddTransientNodeSelectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public double TransientNodeSelectionPressure
    {
        get => _transientNodeSelectionPressure;
        set
        {
            if (Math.Abs(_transientNodeSelectionPressure - value) < 1e-12) return;
            _transientNodeSelectionPressure = value;
            OnPropertyChanged();
        }
    }

    public string TransientNodeSelectionStatus
    {
        get => _transientNodeSelectionStatus;
        private set
        {
            if (_transientNodeSelectionStatus == value) return;
            _transientNodeSelectionStatus = value;
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<(int NodeId, Point Axis)> TransientNodeSelectionPreview =>
        _transientNodeSelectionPreview;

    public TransientFixedPressureNode? SelectedTransientFixedPressureNode
    {
        get => _selectedTransientFixedPressureNode;
        set
        {
            if (ReferenceEquals(_selectedTransientFixedPressureNode, value)) return;
            _selectedTransientFixedPressureNode = value;
            _transientLinePlacementAwaitingEnd = null;
            _transientFixedPressureDraftEditsSelection = value != null;
            if (value != null)
            {
                _syncingTransientFixedPressureDraft = true;
                try { TransientFixedPressureDraft.CopyFrom(value); }
                finally { _syncingTransientFixedPressureDraft = false; }
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsTransientFixedPressureMousePlacementActive));
            (RemoveTransientFixedPressureNodeCommand as RelayCommand)?.RaiseCanExecuteChanged();
            UpdateTransientFixedPressurePlacementStatus();
            GeometryDefinition?.RefreshTransientPressureOverlays();
        }
    }

    /// <summary>Section 2 MouseSelect Line placement. Requires section 2 accordion open.</summary>
    public bool IsTransientFixedPressureMousePlacementActive =>
        IsTransientBcAllowed
        && IsTransientInteractiveBcSectionEnabled
        && TransientFixedPressureDraft is
        {
            IsEnabled: true,
            DefinitionMethod: WaterHeadDefinitionMethod.MouseSelect,
            PlacementType: WaterHeadPlacementType.Line
        };

    public bool IsTransientFixedPressureLineAwaitingEnd =>
        ReferenceEquals(_transientLinePlacementAwaitingEnd, TransientFixedPressureDraft);

    public string TransientFixedPressurePlacementStatus
    {
        get => _transientFixedPressurePlacementStatus;
        private set
        {
            if (_transientFixedPressurePlacementStatus == value) return;
            _transientFixedPressurePlacementStatus = value;
            OnPropertyChanged();
        }
    }

    public WaterHeadModel? SelectedWaterHead
    {
        get => _selectedWaterHead;
        set
        {
            if (ReferenceEquals(_selectedWaterHead, value)) return;
            if (_selectedWaterHead != null)
                _selectedWaterHead.PropertyChanged -= OnSelectedWaterHeadPropertyChanged;
            _selectedWaterHead = value;
            _linePlacementAwaitingEnd = null;
            if (_selectedWaterHead != null)
                _selectedWaterHead.PropertyChanged += OnSelectedWaterHeadPropertyChanged;
            _waterHeadDraftEditsSelection = _selectedWaterHead != null;
            LoadWaterHeadDraftFromSelection();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsWaterHeadMousePlaceActive));
            (RemoveWaterHeadCommand as RelayCommand)?.RaiseCanExecuteChanged();
            UpdateWaterHeadPlacementStatus();
            GeometryDefinition?.RefreshWaterHeadOverlays();
        }
    }

    public string WaterHeadPlacementStatus
    {
        get => _waterHeadPlacementStatus;
        private set { if (_waterHeadPlacementStatus == value) return; _waterHeadPlacementStatus = value ?? ""; OnPropertyChanged(); }
    }

    /// <summary>True when the draft (or selected) head uses Mouse select — canvas clicks place the draft.</summary>
    public bool IsWaterHeadMousePlaceActive =>
        IsWaterHeadAllowed
        && WaterHeadDraft is { IsEnabled: true, DefinitionMethod: WaterHeadDefinitionMethod.MouseSelect };

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            RecordUndo(value ? "Enable groundwater" : "Disable groundwater");
            _isEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsGroundwaterEnabled));
            OnPropertyChanged(nameof(IsTransientBcAllowed));
            OnPropertyChanged(nameof(IsPhreaticEditorAllowed));
            OnPropertyChanged(nameof(IsPondedFreeWaterAllowed));
            GeometryDefinition?.RefreshGroundwaterOverlays();
            (AddWaterTableCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (StartPolylineDrawCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ClearGroundwaterCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (OpenWaterPressureGridDialogCommand as RelayCommand<object?>)?.RaiseCanExecuteChanged();
            SyncToScript();
        }
    }

    public bool IsGroundwaterEnabled => IsEnabled;

    /// <summary>True when Project Settings → Static → Water Surfaces is checked (gates phreatic UI in the Groundwater popup).</summary>
    public bool IsWaterSurfacesAllowed =>
        _projectSettings?.GroundwaterStaticSettings.IncludeWaterSurfaces == true;

    /// <summary>Phreatic editor for Static Water Surfaces or Transient General mesh.</summary>
    public bool IsPhreaticEditorAllowed => IsWaterSurfacesAllowed || IsTransientBcAllowed;

    /// <summary>True when Static → Water Surfaces and Ponded free-water pressures are checked, or Transient BCs are on.</summary>
    public bool IsPondedFreeWaterAllowed =>
        (IsWaterSurfacesAllowed
         && _projectSettings?.GroundwaterStaticSettings.IncludePondedFreeWater == true)
        || IsTransientBcAllowed;

    /// <summary>True when Project Settings → Static → Ru Coefficients is checked (gates Ru UI in the Groundwater popup).</summary>
    public bool IsRuCoefficientsAllowed =>
        _projectSettings?.GroundwaterStaticSettings.IncludeRuCoefficients == true;

    /// <summary>
    /// True when Steady State seepage is active and Water Head is checked for that solver
    /// (gates Add Water Head UI on the Groundwater tab).
    /// </summary>
    public bool IsWaterHeadAllowed =>
        _projectSettings?.IsActiveSeepageWaterHeadEnabled == true;

    /// <summary>
    /// True when Flow-Seepage is ON and Transient State (tree or solver) is active —
    /// gates editable transient consolidation BCs in the Groundwater popup.
    /// </summary>
    public bool IsTransientBcAllowed
    {
        get
        {
            if (!IsEnabled || _projectSettings == null)
                return false;
            if (_projectSettings.IsTransientStateDetailSelected || _projectSettings.IsTransientAnalysisSelected)
                return true;
            return string.Equals(
                _materialProperties?.LastGroundwaterTreeMode,
                MaterialPropertiesViewModel.GroundwaterTransientStateType,
                StringComparison.Ordinal);
        }
    }

    /// <summary>Same instance as Project Settings → Transient State (two-way edit from this popup).</summary>
    public TransientAnalysisSettings? TransientSettings => _projectSettings?.TransientSettings;

    /// <summary>True when Static → Water Pressure Grid (Total Head) is checked.</summary>
    public bool IsTotalHeadGridAllowed =>
        _projectSettings?.GroundwaterStaticSettings.IncludeWaterPressureGridTotalHead == true;

    /// <summary>True when Static → Water Pressure Grid (Pressure Head) is checked.</summary>
    public bool IsPressureHeadGridAllowed =>
        _projectSettings?.GroundwaterStaticSettings.IncludeWaterPressureGridPressureHead == true;

    /// <summary>True when Static → Water Pressure Grid (Pore Pressure) is checked.</summary>
    public bool IsPorePressureGridAllowed =>
        _projectSettings?.GroundwaterStaticSettings.IncludeWaterPressureGridPorePressure == true;

    public bool IsAnyWaterPressureGridAllowed =>
        IsTotalHeadGridAllowed || IsPressureHeadGridAllowed || IsPorePressureGridAllowed;

    public string StaticMethodRestrictionMessage
    {
        get
        {
            if (!IsEnabled || !(_projectSettings?.IsGroundwaterStaticSelected ?? false))
                return "";
            if (!IsWaterSurfacesAllowed && !IsRuCoefficientsAllowed)
                return "Enable Water Surfaces or Ru / B-bar Coefficients in Project Settings → Analysis Type → Static.";
            if (!IsWaterSurfacesAllowed && IsHydrostaticMode)
                return "Phreatic surface is disabled by Static analysis settings; switch to Ru / B-bar or enable Water Surfaces.";
            if (!IsRuCoefficientsAllowed && (IsRuMode || IsBbarMode))
                return "Ru / B-bar coefficients are disabled by Static analysis settings; switch to phreatic surface or enable Ru / B-bar Coefficients.";
            return "";
        }
    }

    public double GammaW
    {
        get => _gammaW;
        set
        {
            if (Math.Abs(_gammaW - value) < 1e-12) return;
            _gammaW = value;
            OnPropertyChanged();
            SyncToScript();
        }
    }

    public string WaterTableInput
    {
        get => _waterTableInput;
        set { _waterTableInput = value ?? ""; OnPropertyChanged(); }
    }

    public GroundwaterMethod Method
    {
        get => _method;
        set
        {
            if (_method == value) return;
            RecordUndo(value switch
            {
                GroundwaterMethod.Ru => "Switch groundwater to Ru",
                GroundwaterMethod.Bbar => "Switch groundwater to B-bar",
                GroundwaterMethod.Grid => "Switch groundwater to grid",
                _ => "Switch groundwater to hydrostatic"
            });
            _method = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRuMode));
            OnPropertyChanged(nameof(IsBbarMode));
            OnPropertyChanged(nameof(IsHydrostaticMode));
            OnPropertyChanged(nameof(PondedFreeWaterStatus));
            if (IsRuMode || IsBbarMode)
            {
                GeometryDefinition?.ClearGroundwater();
                FinishPolylineDraw();
            }
            GeometryDefinition?.RefreshGroundwaterOverlays();
            SyncToScript();
        }
    }

    public bool IsRuMode
    {
        get => _method == GroundwaterMethod.Ru;
        set { if (value) Method = GroundwaterMethod.Ru; }
    }

    public bool IsBbarMode
    {
        get => _method == GroundwaterMethod.Bbar;
        set { if (value) Method = GroundwaterMethod.Bbar; }
    }

    public bool IsHydrostaticMode
    {
        get => _method == GroundwaterMethod.Hydrostatic;
        set { if (value) Method = GroundwaterMethod.Hydrostatic; }
    }

    /// <summary>Canvas-only visibility of the soil region below the phreatic surface.</summary>
    public bool ShowSaturatedMaterialZone
    {
        get => _showSaturatedMaterialZone;
        set
        {
            if (_showSaturatedMaterialZone == value) return;
            RecordUndo(value ? "Show saturated material zone" : "Hide saturated material zone");
            _showSaturatedMaterialZone = value;
            OnPropertyChanged();
            GeometryDefinition?.RefreshGroundwaterOverlays();
        }
    }

    /// <summary>Canvas-only visibility of the soil region above the phreatic surface.</summary>
    public bool ShowUnsaturatedMaterialZone
    {
        get => _showUnsaturatedMaterialZone;
        set
        {
            if (_showUnsaturatedMaterialZone == value) return;
            RecordUndo(value ? "Show unsaturated material zone" : "Hide unsaturated material zone");
            _showUnsaturatedMaterialZone = value;
            OnPropertyChanged();
            GeometryDefinition?.RefreshGroundwaterOverlays();
        }
    }

    /// <summary>Canvas-only: hydrostatic depth field for free water above the ground.</summary>
    public bool ShowPondedFreeWaterRegion
    {
        get => _showPondedFreeWaterRegion;
        set
        {
            if (_showPondedFreeWaterRegion == value) return;
            RecordUndo(value ? "Show ponded free-water region" : "Hide ponded free-water region");
            _showPondedFreeWaterRegion = value;
            OnPropertyChanged();
            GeometryDefinition?.RefreshGroundwaterOverlays();
        }
    }

    /// <summary>Canvas-only fill pattern for the ponded free-water region.</summary>
    public PondedWaterDisplayPattern PondedWaterDisplayPattern
    {
        get => _pondedWaterDisplayPattern;
        set
        {
            if (_pondedWaterDisplayPattern == value) return;
            RecordUndo($"Ponded water pattern: {value}");
            _pondedWaterDisplayPattern = value;
            OnPropertyChanged();
            GeometryDefinition?.RefreshGroundwaterOverlays();
        }
    }

    /// <summary>Canvas-only: auto-assigned ponded face pressures as load arrows (needs mesh).</summary>
    public bool ShowPondedFacePressureArrows
    {
        get => _showPondedFacePressureArrows;
        set
        {
            if (_showPondedFacePressureArrows == value) return;
            RecordUndo(value ? "Show ponded face-pressure arrows" : "Hide ponded face-pressure arrows");
            _showPondedFacePressureArrows = value;
            OnPropertyChanged();
            GeometryDefinition?.RefreshGroundwaterOverlays();
        }
    }

    /// <summary>Canvas-only: head isobars in the ponded free-water region.</summary>
    public bool ShowPondedHeadIsobars
    {
        get => _showPondedHeadIsobars;
        set
        {
            if (_showPondedHeadIsobars == value) return;
            RecordUndo(value ? "Show ponded head isobars" : "Hide ponded head isobars");
            _showPondedHeadIsobars = value;
            OnPropertyChanged();
            GeometryDefinition?.RefreshGroundwaterOverlays();
        }
    }

    public PondedWaterLevelMethod PondedWaterLevelMethod
    {
        get => _pondedWaterLevelMethod;
        set
        {
            if (_pondedWaterLevelMethod == value) return;
            RecordUndo(value == PondedWaterLevelMethod.TotalHead
                ? "Ponded water level: Total Head"
                : "Ponded water level: Phreatic line");
            _pondedWaterLevelMethod = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPondedPhreaticMethod));
            OnPropertyChanged(nameof(IsPondedTotalHeadMethod));
            OnPropertyChanged(nameof(PondedFreeWaterStatus));
            GeometryDefinition?.RefreshGroundwaterOverlays();
            SyncToScript();
        }
    }

    public bool IsPondedPhreaticMethod
    {
        get => _pondedWaterLevelMethod == PondedWaterLevelMethod.PhreaticLine;
        set { if (value) PondedWaterLevelMethod = PondedWaterLevelMethod.PhreaticLine; }
    }

    public bool IsPondedTotalHeadMethod
    {
        get => _pondedWaterLevelMethod == PondedWaterLevelMethod.TotalHead;
        set { if (value) PondedWaterLevelMethod = PondedWaterLevelMethod.TotalHead; }
    }

    /// <summary>Constant free-water surface elevation (axis Y) when using Total Head method.</summary>
    public double PondedTotalHead
    {
        get => _pondedTotalHead;
        set
        {
            if (Math.Abs(_pondedTotalHead - value) < 1e-12) return;
            _pondedTotalHead = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PondedFreeWaterStatus));
            GeometryDefinition?.RefreshGroundwaterOverlays();
            SyncToScript();
        }
    }

    /// <summary>Read-only status for the Ponded free water group.</summary>
    public string PondedFreeWaterStatus
    {
        get
        {
            if (!IsEnabled || !IsHydrostaticMode || !IsPondedFreeWaterAllowed)
                return "Inactive — enable Static → Water Surfaces and Ponded free-water pressures.";
            if (GeometryDefinition == null || GeometryDefinition.GeometryVertexRows.Count < 3)
                return "Inactive — define geometry first.";

            var heads = GeometryDefinition.ComputePondedHeadSummary(
                PondedWaterLevelMethod,
                PondedTotalHead);
            if (heads is null || heads.Value.MaxHead <= 1e-9)
                return "Inactive — water level at or below ground (no free-water head).";

            var unit = LengthUnitLabel;
            return $"Active — max free-water head {heads.Value.MaxHead:G3} {unit}"
                   + (heads.Value.ToeHead is double toe && heads.Value.CrestHead is double crest
                       ? $" (toe {toe:G3}, crest {crest:G3})"
                       : "");
        }
    }

    /// <summary>Project length unit for groundwater tables and status (ft or m).</summary>
    public string LengthUnitLabel =>
        ProjectUnitSystem.LengthLabel(_projectSettings?.Project.Units);

    public string UnitWeightUnitLabel =>
        ProjectUnitSystem.UnitWeightLabel(_projectSettings?.Project.Units);

    public string StressUnitLabel =>
        ProjectUnitSystem.StressLabel(_projectSettings?.Project.Units);

    public string WaterTableElevationPrompt => $"Water Table Elevation ({LengthUnitLabel}):";
    public string SurfPointXHeader => $"X ({LengthUnitLabel})";
    public string SurfPointYHeader => $"Y / Elevation ({LengthUnitLabel})";
    public string GammaDryHeader => $"γ dry ({UnitWeightUnitLabel})";
    public string GammaSatHeader => $"γsat ({UnitWeightUnitLabel})";
    public string CohesionHeader => $"c′ ({StressUnitLabel})";
    public string YoungsModulusHeader => $"E ({StressUnitLabel})";

    public double Ru
    {
        get => _ru;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_ru - clamped) < 1e-12) return;
            _ru = clamped;
            OnPropertyChanged();
            SyncToScript();
        }
    }

    public double Bbar
    {
        get => _bbar;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_bbar - clamped) < 1e-12) return;
            _bbar = clamped;
            OnPropertyChanged();
            SyncToScript();
        }
    }

    public GroundwaterDrawMode DrawMode
    {
        get => _drawMode;
        set
        {
            if (_drawMode == value) return;
            _drawMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsHorizontalMode));
            OnPropertyChanged(nameof(IsPolylineMode));
            if (IsPolylineDrawActive && value == GroundwaterDrawMode.Horizontal)
                FinishPolylineDraw();
        }
    }

    public bool IsHorizontalMode
    {
        get => _drawMode == GroundwaterDrawMode.Horizontal;
        set { if (value) DrawMode = GroundwaterDrawMode.Horizontal; }
    }

    public bool IsPolylineMode
    {
        get => _drawMode == GroundwaterDrawMode.Polyline;
        set { if (value) DrawMode = GroundwaterDrawMode.Polyline; }
    }

    public bool IsPolylineDrawActive
    {
        get => _isPolylineDrawActive;
        private set { _isPolylineDrawActive = value; OnPropertyChanged(); }
    }

    public string PolylineStatus
    {
        get => _polylineStatus;
        private set { _polylineStatus = value ?? ""; OnPropertyChanged(); }
    }

    public ObservableCollection<GroundwaterSurfPoint>? GroundwaterSurfPoints =>
        GeometryDefinition?.GroundwaterSurfPoints;

    public ObservableCollection<GroundwaterLine>? GroundwaterLines =>
        GeometryDefinition?.GroundwaterLines;

    public GroundwaterSurfPoint? SelectedSurfPoint
    {
        get => _selectedSurfPoint;
        set
        {
            _selectedSurfPoint = value;
            OnPropertyChanged();
            GeometryDefinition?.UpdateGroundwaterSurfPointHighlight(value);
            (RemoveSelectedSurfPointCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public ObservableCollection<WaterPressureGrid> WaterPressureGrids { get; } = new();

    public int TotalHeadGridPointCount => CountMeaningfulPoints(WaterPressureGridType.TotalHead);
    public int PressureHeadGridPointCount => CountMeaningfulPoints(WaterPressureGridType.PressureHead);
    public int PorePressureGridPointCount => CountMeaningfulPoints(WaterPressureGridType.PorePressure);

    public ICommand AddWaterTableCommand { get; }
    public ICommand StartPolylineDrawCommand { get; }
    public ICommand FinishPolylineDrawCommand { get; }
    public ICommand ClearGroundwaterCommand { get; }
    public ICommand RemoveSelectedSurfPointCommand { get; }
    public ICommand OpenWaterPressureGridDialogCommand { get; }

    public void ApplyImportedModel(GroundwaterModel model)
    {
        _isEnabled = model.IsEnabled;
        _gammaW = model.GammaW;
        _drawMode = model.DrawMode;
        _method = model.Method;
        _ru = model.Ru;
        _bbar = model.Bbar;
        _showSaturatedMaterialZone = model.ShowSaturatedMaterialZone;
        _showUnsaturatedMaterialZone = model.ShowUnsaturatedMaterialZone;
        _showPondedFreeWaterRegion = model.ShowPondedFreeWaterRegion;
        _pondedWaterDisplayPattern = model.PondedWaterDisplayPattern;
        _showPondedFacePressureArrows = model.ShowPondedFacePressureArrows;
        _showPondedHeadIsobars = model.ShowPondedHeadIsobars;
        _pondedWaterLevelMethod = model.PondedWaterLevelMethod;
        _pondedTotalHead = model.PondedTotalHead;
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(IsGroundwaterEnabled));
        OnPropertyChanged(nameof(GammaW));
        OnPropertyChanged(nameof(Method));
        OnPropertyChanged(nameof(IsRuMode));
        OnPropertyChanged(nameof(IsBbarMode));
        OnPropertyChanged(nameof(IsHydrostaticMode));
        OnPropertyChanged(nameof(Ru));
        OnPropertyChanged(nameof(Bbar));
        OnPropertyChanged(nameof(DrawMode));
        OnPropertyChanged(nameof(IsHorizontalMode));
        OnPropertyChanged(nameof(IsPolylineMode));
        OnPropertyChanged(nameof(ShowSaturatedMaterialZone));
        OnPropertyChanged(nameof(ShowUnsaturatedMaterialZone));
        OnPropertyChanged(nameof(ShowPondedFreeWaterRegion));
        OnPropertyChanged(nameof(PondedWaterDisplayPattern));
        OnPropertyChanged(nameof(ShowPondedFacePressureArrows));
        OnPropertyChanged(nameof(ShowPondedHeadIsobars));
        OnPropertyChanged(nameof(PondedWaterLevelMethod));
        OnPropertyChanged(nameof(IsPondedPhreaticMethod));
        OnPropertyChanged(nameof(IsPondedTotalHeadMethod));
        OnPropertyChanged(nameof(PondedTotalHead));
        OnPropertyChanged(nameof(PondedFreeWaterStatus));
        GeometryDefinition?.ApplyGroundwaterSurfPoints(model.SurfPoints);
        GeometryDefinition?.RefreshGroundwaterOverlays();
        ReplaceWaterPressureGrids(ResolveGridsFromModel(model));
    }

    public GroundwaterModel BuildModel()
    {
        var pts = GeometryDefinition?.GroundwaterSurfPoints;
        var staticSettings = _projectSettings?.IsGroundwaterStaticSelected == true
            ? _projectSettings.GroundwaterStaticSettings
            : null;
        var named = SnapshotNamedGrids(WaterPressureGrids);
        return new GroundwaterModel
        {
            IsEnabled = IsEnabled,
            GammaW = GammaW,
            Method = Method,
            Ru = Ru,
            Bbar = Bbar,
            DrawMode = DrawMode,
            ShowSaturatedMaterialZone = ShowSaturatedMaterialZone,
            ShowUnsaturatedMaterialZone = ShowUnsaturatedMaterialZone,
            ShowPondedFreeWaterRegion = ShowPondedFreeWaterRegion,
            PondedWaterDisplayPattern = PondedWaterDisplayPattern,
            ShowPondedFacePressureArrows = ShowPondedFacePressureArrows,
            ShowPondedHeadIsobars = ShowPondedHeadIsobars,
            PondedWaterLevelMethod = PondedWaterLevelMethod,
            PondedTotalHead = PondedTotalHead,
            SurfPoints = pts?.Select(p => (p.AxisX, p.AxisY)).ToList() ?? new(),
            WaterPressureGrids = named,
            TotalHeadGridPoints = FlattenByType(named, WaterPressureGridType.TotalHead),
            PressureHeadGridPoints = FlattenByType(named, WaterPressureGridType.PressureHead),
            PorePressureGridPoints = FlattenByType(named, WaterPressureGridType.PorePressure),
            StaticSettings = staticSettings
        };
    }

    /// <summary>
    /// RS2-style coupling: after a steady seepage run, install nodal pore pressures
    /// (u = γw · max(0, pressure_head)) as a Static water-pressure grid for SRF export.
    /// </summary>
    public void ApplySeepageResultsAsPorePressureGrid(SeepageRunResult result)
    {
        if (result.Nodes.Count == 0) return;

        RecordUndo("Apply seepage results as pore-pressure grid");
        IsEnabled = true;
        // Grid must win over Ru / empty hydrostatic so SRF solvers see gw.active().
        Method = GroundwaterMethod.Grid;

        var grid = new WaterPressureGrid("Seepage (from FEA)", WaterPressureGridType.PorePressure);
        var idx = 1;
        foreach (var n in result.Nodes.OrderBy(n => n.Id))
        {
            var u = GammaW * Math.Max(0.0, n.PressureHead);
            grid.Points.Add(new WaterPressureGridPoint(idx++, n.X, n.Y, u));
        }

        var keep = WaterPressureGrids
            .Where(g => g.GridType != WaterPressureGridType.PorePressure
                        && !string.Equals(g.Name, "Seepage (from FEA)", StringComparison.OrdinalIgnoreCase))
            .Select(g => g.Clone())
            .ToList();
        keep.Add(grid);
        ReplaceWaterPressureGrids(keep);

        // Ensure the seepage-derived pore-pressure grid is exported into SRF .dat
        // even when Static checkboxes were left at defaults (all off).
        if (_projectSettings?.GroundwaterStaticSettings != null)
            _projectSettings.GroundwaterStaticSettings.IncludeWaterPressureGridPorePressure = true;

        // Keep Steady State as the GW mode so coupled SRF→seepage re-runs stay armed.
        _projectSettings?.MaterialPropertiesVM?.RememberGroundwaterTreeMode(
            MaterialPropertiesViewModel.GroundwaterSteadyStateType);

        SyncToScript();
    }

    private void OpenWaterPressureGridDialog(object? parameter)
    {
        if (!IsEnabled || !IsAnyWaterPressureGridAllowed)
            return;

        var preferred = ResolvePreferredGridType(parameter);
        var allowed = GetAllowedGridTypes();
        if (allowed.Count == 0)
            return;
        if (!allowed.Contains(preferred))
            preferred = allowed[0];

        var dialogVm = new WaterPressureGridDialogViewModel(
            WaterPressureGrids, allowed, preferred, LengthUnitLabel);
        var dlg = new WaterPressureGridDialog(dialogVm)
        {
            Owner = Application.Current?.MainWindow
        };
        if (dlg.ShowDialog() != true)
            return;

        RecordUndo("Edit water pressure grids");
        ReplaceWaterPressureGrids(dialogVm.Grids.Select(g => g.Clone()).ToList());
    }

    private List<WaterPressureGridType> GetAllowedGridTypes()
    {
        var list = new List<WaterPressureGridType>();
        if (IsTotalHeadGridAllowed) list.Add(WaterPressureGridType.TotalHead);
        if (IsPressureHeadGridAllowed) list.Add(WaterPressureGridType.PressureHead);
        if (IsPorePressureGridAllowed) list.Add(WaterPressureGridType.PorePressure);
        return list;
    }

    private static WaterPressureGridType ResolvePreferredGridType(object? parameter)
    {
        if (parameter is WaterPressureGridType t)
            return t;
        if (parameter is string s && WaterPressureGridTypeExtensions.TryParse(s, out var parsed))
            return parsed;
        return WaterPressureGridType.TotalHead;
    }

    private void OnProjectSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Analysis-tree selection only changes which detail panel is shown. Do not rebuild
        // phreatic/ponded overlays or rewrite the Script .dat on every click.
        if (e.PropertyName is nameof(ProjectSettingsViewModel.IsGroundwaterStaticSelected)
            or nameof(ProjectSettingsViewModel.IsSeepageAnalysisSelected)
            or nameof(ProjectSettingsViewModel.IsActiveSeepageWaterHeadEnabled)
            or nameof(ProjectSettingsViewModel.IsTransientStateDetailSelected)
            or nameof(ProjectSettingsViewModel.IsTransientAnalysisSelected)
            or nameof(ProjectSettingsViewModel.IsTransientP84Selected)
            or nameof(ProjectSettingsViewModel.IsTransientP88Selected)
            or nameof(ProjectSettingsViewModel.AreSelectedCategoryTypeOptionsEnabled)
            or nameof(ProjectSettingsViewModel.AreGroundwaterTypeOptionsEnabled))
        {
            NotifyStaticUiAvailability();
            GeometryDefinition?.RefreshWaterHeadOverlays();
            GeometryDefinition?.RefreshTransientPressureOverlays();
            return;
        }

        if (e.PropertyName is nameof(ProjectSettingsViewModel.LengthUnitLabel)
                 or nameof(ProjectSettingsViewModel.UnitWeightUnitLabel)
                 or nameof(ProjectSettingsViewModel.StressUnitLabel))
            NotifyUnitSystemLabels();
    }

    private void OnMaterialPropertiesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MaterialPropertiesViewModel.SelectedDetailedAnalysisType)
            or nameof(MaterialPropertiesViewModel.LastGroundwaterTreeMode)
            or nameof(MaterialPropertiesViewModel.LastTransientSolverType)
            or nameof(MaterialPropertiesViewModel.SelectedAnalysisCategory))
        {
            OnPropertyChanged(nameof(IsTransientBcAllowed));
            OnPropertyChanged(nameof(IsPhreaticEditorAllowed));
            OnPropertyChanged(nameof(IsPondedFreeWaterAllowed));
            (AddTransientFixedPressureNodeCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RemoveTransientFixedPressureNodeCommand as RelayCommand)?.RaiseCanExecuteChanged();
            GeometryDefinition?.RefreshTransientPressureOverlays();
        }
    }

    private void OnTransientSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(TransientSettings));
        GeometryDefinition?.RefreshTransientPressureOverlays();
    }

    private void OnProjectModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectSettingsModel.Units))
            NotifyUnitSystemLabels();
    }

    private void NotifyUnitSystemLabels()
    {
        OnPropertyChanged(nameof(LengthUnitLabel));
        OnPropertyChanged(nameof(UnitWeightUnitLabel));
        OnPropertyChanged(nameof(StressUnitLabel));
        OnPropertyChanged(nameof(WaterTableElevationPrompt));
        OnPropertyChanged(nameof(SurfPointXHeader));
        OnPropertyChanged(nameof(SurfPointYHeader));
        OnPropertyChanged(nameof(GammaDryHeader));
        OnPropertyChanged(nameof(GammaSatHeader));
        OnPropertyChanged(nameof(CohesionHeader));
        OnPropertyChanged(nameof(YoungsModulusHeader));
        OnPropertyChanged(nameof(PondedFreeWaterStatus));
        GeometryDefinition?.RefreshTransientPressureOverlays();
    }

    private void OnGroundwaterStaticSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => RefreshStaticMethodAvailability();

    private void OnSeepageSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SeepageAnalysisSettings.IncludeWaterHeadPlaneAxisym)
            or nameof(SeepageAnalysisSettings.IncludeWaterHeadFreeSurface)
            or nameof(SeepageAnalysisSettings.IncludeWaterHeadGeneral2D)
            or nameof(SeepageAnalysisSettings.IncludeWaterHeadGeneral3D))
        {
            OnPropertyChanged(nameof(IsWaterHeadAllowed));
            OnPropertyChanged(nameof(IsWaterHeadMousePlaceActive));
            (AddWaterHeadCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RemoveWaterHeadCommand as RelayCommand)?.RaiseCanExecuteChanged();
            UpdateWaterHeadPlacementStatus();
            GeometryDefinition?.RefreshWaterHeadOverlays();
        }
    }

    /// <summary>UI gate notifications only (Analysis tree / detail panel). No overlay or .dat work.</summary>
    private void NotifyStaticUiAvailability()
    {
        OnPropertyChanged(nameof(IsWaterSurfacesAllowed));
        OnPropertyChanged(nameof(IsPhreaticEditorAllowed));
        OnPropertyChanged(nameof(IsPondedFreeWaterAllowed));
        OnPropertyChanged(nameof(IsRuCoefficientsAllowed));
        OnPropertyChanged(nameof(IsWaterHeadAllowed));
        OnPropertyChanged(nameof(IsTransientBcAllowed));
        OnPropertyChanged(nameof(TransientSettings));
        OnPropertyChanged(nameof(IsWaterHeadMousePlaceActive));
        OnPropertyChanged(nameof(IsTransientFixedPressureMousePlacementActive));
        OnPropertyChanged(nameof(IsTotalHeadGridAllowed));
        UpdateTransientFixedPressurePlacementStatus();
        OnPropertyChanged(nameof(IsPressureHeadGridAllowed));
        OnPropertyChanged(nameof(IsPorePressureGridAllowed));
        OnPropertyChanged(nameof(IsAnyWaterPressureGridAllowed));
        OnPropertyChanged(nameof(StaticMethodRestrictionMessage));
        (AddWaterTableCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (StartPolylineDrawCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ClearGroundwaterCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (OpenWaterPressureGridDialogCommand as RelayCommand<object?>)?.RaiseCanExecuteChanged();
        (AddWaterHeadCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RemoveWaterHeadCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (AddTransientFixedPressureNodeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RemoveTransientFixedPressureNodeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (AddTransientNodeSelectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        UpdateWaterHeadPlacementStatus();
    }

    /// <summary>
    /// Full refresh after Static component checkboxes change: overlays, method reconcile, script sync.
    /// </summary>
    private void RefreshStaticMethodAvailability()
    {
        NotifyStaticUiAvailability();
        NotifyUnitSystemLabels();
        GeometryDefinition?.RefreshGroundwaterOverlays();

        // Include Groundwater is the master switch; do not clear it when Static component
        // flags are unchecked. Only reconcile the pore-pressure method against allowed modes.
        if (IsEnabled)
        {
            if (IsWaterSurfacesAllowed && !IsRuCoefficientsAllowed)
                Method = GroundwaterMethod.Hydrostatic;
            else if (IsRuCoefficientsAllowed && !IsWaterSurfacesAllowed)
                Method = GroundwaterMethod.Ru;
        }

        SyncToScript();
    }

    private void OnAddWaterHead()
    {
        if (!IsWaterHeadAllowed) return;
        RecordUndo("Add water head");

        // Commit the form (WaterHeadDraft) into the DataGrid as a new row.
        var row = WaterHeadDraft.Clone();
        if (string.IsNullOrWhiteSpace(row.Name))
            row.Name = SuggestWaterHeadName(row.Role);

        WaterHeads.Add(row);

        // Highlight the new row without linking the form to it (form stays ready for the next Add).
        if (_selectedWaterHead != null)
            _selectedWaterHead.PropertyChanged -= OnSelectedWaterHeadPropertyChanged;
        _selectedWaterHead = row;
        _selectedWaterHead.PropertyChanged += OnSelectedWaterHeadPropertyChanged;
        _waterHeadDraftEditsSelection = false;
        OnPropertyChanged(nameof(SelectedWaterHead));

        // Prepare draft for the next entry (keep role/head defaults; clear placement).
        ResetWaterHeadDraftAfterAdd(row);
        _linePlacementAwaitingEnd = null;
        (RemoveWaterHeadCommand as RelayCommand)?.RaiseCanExecuteChanged();
        SyncSeepageSettingsFromWaterHeads();
        UpdateWaterHeadPlacementStatus();
        GeometryDefinition?.RefreshWaterHeadOverlays();
    }

    private void OnRemoveWaterHead()
    {
        if (SelectedWaterHead == null || !WaterHeads.Contains(SelectedWaterHead)) return;
        RecordUndo("Remove water head");
        var row = SelectedWaterHead;
        WaterHeads.Remove(row);
        SelectedWaterHead = WaterHeads.LastOrDefault();
        SyncSeepageSettingsFromWaterHeads();
        GeometryDefinition?.RefreshWaterHeadOverlays();
    }

    private WaterHeadModel CreateDefaultWaterHeadDraft()
    {
        var seepage = _projectSettings?.SeepageSettings;
        return new WaterHeadModel
        {
            Name = "Upstream Head 1",
            Role = WaterHeadRole.Upstream,
            DefinitionMethod = WaterHeadDefinitionMethod.MouseSelect,
            PlacementType = WaterHeadPlacementType.Line,
            Head = seepage?.UpstreamHead ?? 10.0
        };
    }

    private void LoadWaterHeadDraftFromSelection()
    {
        if (_syncingWaterHeadDraft) return;
        _syncingWaterHeadDraft = true;
        try
        {
            if (_selectedWaterHead != null)
                WaterHeadDraft.CopyFrom(_selectedWaterHead);
        }
        finally
        {
            _syncingWaterHeadDraft = false;
        }
    }

    private void ResetWaterHeadDraftAfterAdd(WaterHeadModel justAdded)
    {
        _syncingWaterHeadDraft = true;
        try
        {
            int n = WaterHeads.Count + 1;
            WaterHeadDraft.Name = $"{justAdded.RoleLabel} Head {n}";
            WaterHeadDraft.Role = justAdded.Role;
            WaterHeadDraft.DefinitionMethod = WaterHeadDefinitionMethod.MouseSelect;
            WaterHeadDraft.PlacementType = WaterHeadPlacementType.Line;
            WaterHeadDraft.Head = justAdded.Head;
            WaterHeadDraft.IsEnabled = true;
            WaterHeadDraft.StartX = 0;
            WaterHeadDraft.StartY = 0;
            WaterHeadDraft.EndX = 0;
            WaterHeadDraft.EndY = 0;
            WaterHeadDraft.AppliedToNodeId = null;
            WaterHeadDraft.HasPlacement = false;
        }
        finally
        {
            _syncingWaterHeadDraft = false;
        }
    }

    private static string SuggestWaterHeadName(WaterHeadRole role) => role switch
    {
        WaterHeadRole.Upstream => "Upstream Head",
        WaterHeadRole.Downstream => "Downstream Head",
        _ => "Custom Head"
    };

    private void OnWaterHeadDraftPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_syncingWaterHeadDraft) return;

        if (e.PropertyName == nameof(WaterHeadModel.DefinitionMethod)
            && WaterHeadDraft.DefinitionMethod == WaterHeadDefinitionMethod.MouseSelect
            && WaterHeadDraft.PlacementType == WaterHeadPlacementType.Point)
        {
            WaterHeadDraft.PlacementType = WaterHeadPlacementType.Line;
        }

        if (e.PropertyName is nameof(WaterHeadModel.DefinitionMethod)
            or nameof(WaterHeadModel.PlacementType))
            _linePlacementAwaitingEnd = null;

        // Keep the selected grid row in sync only when the user picked that row in the grid.
        if (_waterHeadDraftEditsSelection
            && _selectedWaterHead != null
            && WaterHeads.Contains(_selectedWaterHead))
        {
            _syncingWaterHeadDraft = true;
            try
            {
                _selectedWaterHead.CopyFrom(WaterHeadDraft);
            }
            finally
            {
                _syncingWaterHeadDraft = false;
            }
        }

        if (e.PropertyName is nameof(WaterHeadModel.Head)
            or nameof(WaterHeadModel.Role)
            or nameof(WaterHeadModel.DefinitionMethod)
            or nameof(WaterHeadModel.IsEnabled))
            SyncSeepageSettingsFromWaterHeads();

        if (e.PropertyName is nameof(WaterHeadModel.DefinitionMethod)
            or nameof(WaterHeadModel.PlacementType)
            or nameof(WaterHeadModel.HasPlacement)
            or nameof(WaterHeadModel.IsEnabled)
            or nameof(WaterHeadModel.StartX)
            or nameof(WaterHeadModel.StartY)
            or nameof(WaterHeadModel.EndX)
            or nameof(WaterHeadModel.EndY)
            or nameof(WaterHeadModel.Head)
            or nameof(WaterHeadModel.Name)
            or nameof(WaterHeadModel.Role))
        {
            OnPropertyChanged(nameof(IsWaterHeadMousePlaceActive));
            UpdateWaterHeadPlacementStatus();
            GeometryDefinition?.RefreshWaterHeadOverlays();
        }
    }

    private void OnSelectedWaterHeadPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_syncingWaterHeadDraft) return;
        // Grid cell edits → refresh draft + overlays.
        if (e.PropertyName is nameof(WaterHeadModel.Head)
            or nameof(WaterHeadModel.Role)
            or nameof(WaterHeadModel.DefinitionMethod)
            or nameof(WaterHeadModel.IsEnabled)
            or nameof(WaterHeadModel.Name))
        {
            LoadWaterHeadDraftFromSelection();
            SyncSeepageSettingsFromWaterHeads();
        }
        GeometryDefinition?.RefreshWaterHeadOverlays();
    }

    /// <summary>
    /// Keep the scalar seepage heads used by p73 in sync with enabled Upstream/Downstream
    /// rows. Prefer face-wide UserDefined rows; MouseSelect rows are also valid because p73
    /// uses the row role/head while its structured solver owns the fixed-face node lists.
    /// </summary>
    public void SyncSeepageSettingsFromWaterHeads()
    {
        if (_syncingSeepageHeadFromRow || _projectSettings == null) return;
        var seepage = _projectSettings.SeepageSettings;
        _syncingSeepageHeadFromRow = true;
        try
        {
            var up = WaterHeads.FirstOrDefault(h => h.IsEnabled && h.Role == WaterHeadRole.Upstream
                    && h.DefinitionMethod == WaterHeadDefinitionMethod.UserDefined)
                ?? WaterHeads.FirstOrDefault(h => h.IsEnabled && h.Role == WaterHeadRole.Upstream);
            if (up != null)
                seepage.UpstreamHead = up.Head;
            var down = WaterHeads.FirstOrDefault(h => h.IsEnabled && h.Role == WaterHeadRole.Downstream
                    && h.DefinitionMethod == WaterHeadDefinitionMethod.UserDefined)
                ?? WaterHeads.FirstOrDefault(h => h.IsEnabled && h.Role == WaterHeadRole.Downstream);
            if (down != null)
                seepage.DownstreamHead = down.Head;
        }
        finally
        {
            _syncingSeepageHeadFromRow = false;
        }
    }

    private void UpdateWaterHeadPlacementStatus()
    {
        if (!IsWaterHeadAllowed)
        {
            WaterHeadPlacementStatus = "";
            return;
        }
        var h = WaterHeadDraft;
        if (h.DefinitionMethod == WaterHeadDefinitionMethod.UserDefined && !h.HasPlacement)
        {
            WaterHeadPlacementStatus = h.Role switch
            {
                WaterHeadRole.Upstream => "User-defined Upstream (H₁) applies to the min-x boundary. Place with Mouse select or click Add to use face defaults.",
                WaterHeadRole.Downstream => "User-defined Downstream (H₂) applies to the max-x boundary. Place with Mouse select or click Add to use face defaults.",
                _ => "Set fields above, optionally place with Mouse select, then click Add."
            };
            return;
        }
        if (h.DefinitionMethod == WaterHeadDefinitionMethod.MouseSelect
            && h.PlacementType == WaterHeadPlacementType.Line
            && ReferenceEquals(_linePlacementAwaitingEnd, h))
        {
            WaterHeadPlacementStatus = "Click end of head segment (snaps to nearest mesh node), then click Add.";
            return;
        }
        if (SelectedWaterHead is { HasPlacement: true }
            && _waterHeadDraftEditsSelection
            && h.HasPlacement)
        {
            WaterHeadPlacementStatus = $"Showing {SelectedWaterHead.Name}: {SelectedWaterHead.PlacementSummary}";
            return;
        }

        WaterHeadPlacementStatus = h.HasPlacement
            ? $"{h.PlacementSummary} — click Add to put this row in the grid."
            : (h.PlacementType == WaterHeadPlacementType.Line
                ? "Fill the form, click start then end of the head segment on the canvas, then click Add."
                : "Fill the form, click the canvas to snap to a mesh node, then click Add.");
    }

    /// <summary>Canvas click (axis space) while the draft water head uses Mouse select.</summary>
    public void RegisterWaterHeadPoint(Point axisPoint)
    {
        if (!IsWaterHeadMousePlaceActive) return;
        var head = WaterHeadDraft;
        RecordUndo("Place water head");
        var (snap, nodeId) = SnapAxisToNearestMeshNode(axisPoint);

        if (head.PlacementType == WaterHeadPlacementType.Point)
        {
            head.StartX = snap.X;
            head.StartY = snap.Y;
            head.EndX = snap.X;
            head.EndY = snap.Y;
            head.AppliedToNodeId = nodeId;
            head.HasPlacement = true;
            _linePlacementAwaitingEnd = null;
            UpdateWaterHeadPlacementStatus();
            OnPropertyChanged(nameof(IsWaterHeadMousePlaceActive));
            GeometryDefinition?.RefreshWaterHeadOverlays();
            return;
        }

        // Line placement (two clicks on a segment, like Add Load)
        if (ReferenceEquals(_linePlacementAwaitingEnd, head))
        {
            head.EndX = snap.X;
            head.EndY = snap.Y;
            head.HasPlacement = true;
            _linePlacementAwaitingEnd = null;
        }
        else
        {
            head.StartX = snap.X;
            head.StartY = snap.Y;
            head.EndX = snap.X;
            head.EndY = snap.Y;
            head.AppliedToNodeId = nodeId;
            head.HasPlacement = false;
            _linePlacementAwaitingEnd = head;
        }
        UpdateWaterHeadPlacementStatus();
        GeometryDefinition?.RefreshWaterHeadOverlays();
    }

    /// <summary>Snap axis click to nearest mesh discretization node when a mesh exists.</summary>
    private (Point Axis, int? NodeId) SnapAxisToNearestMeshNode(Point axisClick)
    {
        if (_mesh is not { IsDiscretized: true, DiscretizationNodes.Count: > 0 })
            return (axisClick, null);

        double bestDistSq = double.MaxValue;
        int bestIndex = -1;
        Point best = axisClick;
        for (int i = 0; i < _mesh.DiscretizationNodes.Count; i++)
        {
            var loc = _mesh.DiscretizationNodes[i].Location;
            double dx = loc.X - axisClick.X;
            double dy = loc.Y - axisClick.Y;
            double d2 = dx * dx + dy * dy;
            if (d2 < bestDistSq)
            {
                bestDistSq = d2;
                bestIndex = i;
                best = loc;
            }
        }
        if (bestIndex < 0) return (best, null);
        var storedId = _mesh.DiscretizationNodes[bestIndex].NodeId;
        return (best, storedId > 0 ? storedId : bestIndex + 1);
    }

    /// <summary>Whether line placement is awaiting the second click for the draft head.</summary>
    public bool IsWaterHeadLineAwaitingEnd =>
        ReferenceEquals(_linePlacementAwaitingEnd, WaterHeadDraft);

    public void RegisterTransientFixedPressureNode(Point axisPoint)
    {
        if (!IsTransientFixedPressureMousePlacementActive) return;

        var (snap, nodeId) = SnapAxisToNearestMeshNode(axisPoint);
        if (nodeId is not int id)
        {
            TransientFixedPressurePlacementStatus = "Generate a mesh before placing a fixed-pressure Line.";
            return;
        }

        RecordUndo("Place transient fixed-pressure line");
        var draft = TransientFixedPressureDraft;
        draft.PlacementType = WaterHeadPlacementType.Line;
        if (ReferenceEquals(_transientLinePlacementAwaitingEnd, draft))
        {
            draft.EndX = snap.X;
            draft.EndY = snap.Y;
            draft.HasPlacement = true;
            _transientLinePlacementAwaitingEnd = null;
        }
        else
        {
            draft.NodeId = id;
            draft.StartX = snap.X;
            draft.StartY = snap.Y;
            draft.EndX = snap.X;
            draft.EndY = snap.Y;
            draft.HasPlacement = false;
            _transientLinePlacementAwaitingEnd = draft;
        }

        UpdateTransientFixedPressurePlacementStatus();
        (AddTransientFixedPressureNodeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        GeometryDefinition?.RefreshTransientPressureOverlays();
    }

    public void RegisterTransientFixedPressureNodesFromSelection(
        IReadOnlyList<(int NodeId, Point Axis)> nodes)
    {
        if (!IsTransientBcAllowed || !IsTransientNodeSelectionEnabled) return;
        var ordered = nodes
            .Where(n => n.NodeId > 0)
            .GroupBy(n => n.NodeId)
            .Select(g => g.First())
            .OrderBy(n => n.NodeId)
            .ToList();
        _transientNodeSelectionPreview.Clear();
        _transientNodeSelectionPreview.AddRange(ordered);
        TransientNodeSelectionIdsText = string.Join(", ", ordered.Select(n => n.NodeId));
        TransientNodeSelectionStatus = ordered.Count == 0
            ? "No mesh nodes in the selection. Drag a larger box or click nearer a node."
            : $"{ordered.Count} node(s) selected — set pressure if needed, then Add selected nodes.";
        (AddTransientNodeSelectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        GeometryDefinition?.RefreshTransientPressureOverlays();
    }

    private bool CanAddTransientFixedPressureNode()
    {
        if (!IsTransientBcAllowed) return false;
        var draft = TransientFixedPressureDraft;
        if (draft.IsUserDefined && !draft.HasPlacement)
            return true;
        return draft.IsMouseSelect
               && draft.PlacementType == WaterHeadPlacementType.Line
               && draft.HasPlacement;
    }

    private bool CanAddTransientNodeSelection()
        => IsTransientBcAllowed
           && IsTransientNodeSelectionEnabled
           && ParseTransientNodeSelectionIds().Count > 0;

    private void OnAddTransientFixedPressureNode()
    {
        if (!CanAddTransientFixedPressureNode()) return;

        var draft = TransientFixedPressureDraft;
        if (draft.IsMouseSelect)
            draft.PlacementType = WaterHeadPlacementType.Line;

        if (string.IsNullOrWhiteSpace(draft.Name))
            draft.Name = SuggestTransientFixedPressureName(draft);

        RecordUndo("Add transient fixed-pressure BC");
        TransientFixedPressureNode? existing = null;
        if (draft.IsUserDefined && !draft.HasPlacement)
            existing = TransientFixedPressureNodes.FirstOrDefault(n =>
                n.IsUserDefined && !n.HasPlacement && n.Face == draft.Face);

        if (existing != null)
        {
            existing.CopyFrom(draft);
            SelectedTransientFixedPressureNode = existing;
        }
        else
        {
            var row = draft.Clone();
            TransientFixedPressureNodes.Add(row);
            SelectedTransientFixedPressureNode = row;
        }

        _transientFixedPressureDraftEditsSelection = false;
        _transientLinePlacementAwaitingEnd = null;
        ResetTransientFixedPressureDraftAfterAdd(draft);
        UpdateTransientFixedPressurePlacementStatus();
        (AddTransientFixedPressureNodeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        GeometryDefinition?.RefreshTransientPressureOverlays();
    }

    private void OnAddTransientNodeSelection()
    {
        if (!CanAddTransientNodeSelection()) return;
        var ids = ParseTransientNodeSelectionIds();
        if (ids.Count == 0) return;

        RecordUndo("Add transient fixed-pressure nodes");
        int added = 0;
        foreach (var id in ids)
        {
            if (!TryGetMeshNodeAxis(id, out var axis))
                continue;
            var existing = TransientFixedPressureNodes.FirstOrDefault(n =>
                n.IsMouseSelect && n.IsPointPlacement && n.NodeId == id);
            var row = existing ?? new TransientFixedPressureNode();
            row.Name = string.IsNullOrWhiteSpace(row.Name) ? $"Node {id}" : row.Name;
            row.IsEnabled = true;
            row.DefinitionMethod = WaterHeadDefinitionMethod.MouseSelect;
            row.PlacementType = WaterHeadPlacementType.Point;
            row.Pressure = TransientNodeSelectionPressure;
            row.NodeId = id;
            row.StartX = axis.X;
            row.StartY = axis.Y;
            row.EndX = axis.X;
            row.EndY = axis.Y;
            row.HasPlacement = true;
            if (existing == null)
                TransientFixedPressureNodes.Add(row);
            added++;
            SelectedTransientFixedPressureNode = row;
        }

        ClearTransientNodeSelectionPreview();
        TransientNodeSelectionIdsText = "";
        TransientNodeSelectionStatus = added == 0
            ? "No valid mesh Node IDs to add. Check the IDs or regenerate the mesh."
            : $"Added {added} fixed-pressure node(s).";
        (AddTransientNodeSelectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        GeometryDefinition?.RefreshTransientPressureOverlays();
    }

    private List<int> ParseTransientNodeSelectionIds()
    {
        var result = new List<int>();
        foreach (var token in TransientNodeSelectionIdsText.Split(
                     new[] { ',', ';', ' ', '\t', '\r', '\n' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(token.Trim(), out var id) && id > 0 && !result.Contains(id))
                result.Add(id);
        }
        return result;
    }

    private void SyncTransientNodeSelectionPreviewFromText()
    {
        if (!IsTransientNodeSelectionEnabled) return;
        _transientNodeSelectionPreview.Clear();
        foreach (var id in ParseTransientNodeSelectionIds())
        {
            if (TryGetMeshNodeAxis(id, out var axis))
                _transientNodeSelectionPreview.Add((id, axis));
        }
        if (_transientNodeSelectionPreview.Count > 0)
            TransientNodeSelectionStatus =
                $"{_transientNodeSelectionPreview.Count} node(s) ready — click Add selected nodes.";
        GeometryDefinition?.RefreshTransientPressureOverlays();
    }

    private void ClearTransientNodeSelectionPreview()
    {
        _transientNodeSelectionPreview.Clear();
    }

    private void OnRemoveTransientFixedPressureNode()
    {
        if (SelectedTransientFixedPressureNode == null) return;
        RecordUndo("Remove transient fixed-pressure BC");
        TransientFixedPressureNodes.Remove(SelectedTransientFixedPressureNode);
        SelectedTransientFixedPressureNode = TransientFixedPressureNodes.LastOrDefault();
        GeometryDefinition?.RefreshTransientPressureOverlays();
    }

    private void ResetTransientFixedPressureDraftAfterAdd(TransientFixedPressureNode? justAdded = null)
    {
        _syncingTransientFixedPressureDraft = true;
        try
        {
            TransientFixedPressureDraft.Name = "";
            TransientFixedPressureDraft.IsEnabled = true;
            TransientFixedPressureDraft.DefinitionMethod =
                justAdded?.DefinitionMethod ?? WaterHeadDefinitionMethod.UserDefined;
            TransientFixedPressureDraft.Face = justAdded?.Face ?? TransientDrainFace.Top;
            TransientFixedPressureDraft.PlacementType = WaterHeadPlacementType.Line;
            TransientFixedPressureDraft.Pressure =
                _projectSettings?.TransientSettings.FixedPressure
                ?? justAdded?.Pressure
                ?? 0.0;
            TransientFixedPressureDraft.NodeId = 0;
            TransientFixedPressureDraft.StartX = 0;
            TransientFixedPressureDraft.StartY = 0;
            TransientFixedPressureDraft.EndX = 0;
            TransientFixedPressureDraft.EndY = 0;
            TransientFixedPressureDraft.HasPlacement = false;
        }
        finally
        {
            _syncingTransientFixedPressureDraft = false;
        }
        OnPropertyChanged(nameof(IsTransientFixedPressureMousePlacementActive));
        OnPropertyChanged(nameof(IsTransientFixedPressureLineAwaitingEnd));
    }

    private static string SuggestTransientFixedPressureName(TransientFixedPressureNode draft)
    {
        if (draft.IsUserDefined && !draft.HasPlacement)
            return draft.Face switch
            {
                TransientDrainFace.Bottom => "Bottom drain",
                TransientDrainFace.MinX => "Min-x drain",
                TransientDrainFace.MaxX => "Max-x drain",
                _ => "Top drain"
            };
        return draft.PlacementType == WaterHeadPlacementType.Line
            ? "Fixed pressure line"
            : "Fixed pressure node";
    }

    private void UpdateTransientFixedPressurePlacementStatus()
    {
        if (!IsTransientBcAllowed)
        {
            TransientFixedPressurePlacementStatus = "Enable Transient State to edit consolidation BCs.";
            return;
        }

        if (IsTransientNodeSelectionEnabled)
        {
            TransientFixedPressurePlacementStatus =
                "Node selection mode is on — use section 3 (typed IDs or drag box). Check section 2 to place a MouseSelect Line.";
            return;
        }

        if (!IsTransientInteractiveBcSectionEnabled)
        {
            TransientFixedPressurePlacementStatus =
                IsTransientBulkFaceSectionEnabled
                    ? "Section 1 is open — edit bulk drain faces, or check section 2 for UserDefined / MouseSelect."
                    : "Check section 1, 2, or 3 to edit transient BCs.";
            return;
        }

        var d = TransientFixedPressureDraft;
        if (d.IsUserDefined)
        {
            TransientFixedPressurePlacementStatus =
                $"UserDefined → {d.FaceLabel} at p={d.Pressure:G5}. Click Add to append a row.";
            return;
        }

        d.PlacementType = WaterHeadPlacementType.Line;
        if (IsTransientFixedPressureLineAwaitingEnd)
        {
            TransientFixedPressurePlacementStatus = "Click the end of the fixed-pressure segment on the canvas.";
            return;
        }

        TransientFixedPressurePlacementStatus = d.HasPlacement
            ? $"{d.Summary} — click Add to put this row in the grid."
            : "MouseSelect Line: click start then end on the canvas, then Add.";
    }

    private void OnTransientFixedPressureDraftPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_syncingTransientFixedPressureDraft) return;

        if (e.PropertyName is nameof(TransientFixedPressureNode.DefinitionMethod)
            or nameof(TransientFixedPressureNode.PlacementType))
            _transientLinePlacementAwaitingEnd = null;

        if (e.PropertyName == nameof(TransientFixedPressureNode.DefinitionMethod)
            && TransientFixedPressureDraft.IsMouseSelect
            && TransientFixedPressureDraft.PlacementType != WaterHeadPlacementType.Line)
        {
            _syncingTransientFixedPressureDraft = true;
            try { TransientFixedPressureDraft.PlacementType = WaterHeadPlacementType.Line; }
            finally { _syncingTransientFixedPressureDraft = false; }
        }

        if (_transientFixedPressureDraftEditsSelection
            && SelectedTransientFixedPressureNode != null
            && TransientFixedPressureNodes.Contains(SelectedTransientFixedPressureNode))
            SelectedTransientFixedPressureNode.CopyFrom(TransientFixedPressureDraft);

        OnPropertyChanged(nameof(IsTransientFixedPressureMousePlacementActive));
        OnPropertyChanged(nameof(IsTransientFixedPressureLineAwaitingEnd));
        UpdateTransientFixedPressurePlacementStatus();
        (AddTransientFixedPressureNodeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        GeometryDefinition?.RefreshTransientPressureOverlays();
    }

    private void OnTransientFixedPressureNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (TransientFixedPressureNode row in e.NewItems)
                row.PropertyChanged += OnTransientFixedPressureNodePropertyChanged;
        if (e.OldItems != null)
            foreach (TransientFixedPressureNode row in e.OldItems)
                row.PropertyChanged -= OnTransientFixedPressureNodePropertyChanged;
        GeometryDefinition?.RefreshTransientPressureOverlays();
    }

    private void OnTransientFixedPressureNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
        => GeometryDefinition?.RefreshTransientPressureOverlays();

    private bool TryGetMeshNodeAxis(int nodeId, out Point axis)
    {
        axis = default;
        if (nodeId <= 0 || _mesh == null) return false;
        foreach (var row in _mesh.MeshInformationRows)
        {
            var nodes = new[]
            {
                (row.Node1, row.N1X, row.N1Y), (row.Node2, row.N2X, row.N2Y),
                (row.Node3, row.N3X, row.N3Y), (row.Node4, row.N4X, row.N4Y),
                (row.Node5, row.N5X, row.N5Y), (row.Node6, row.N6X, row.N6Y),
                (row.Node7, row.N7X, row.N7Y), (row.Node8, row.N8X, row.N8Y)
            };
            foreach (var node in nodes)
            {
                if (node.Item1 != nodeId) continue;
                axis = new Point(node.Item2, node.Item3);
                return true;
            }
        }
        for (var i = 0; i < _mesh.DiscretizationNodes.Count; i++)
        {
            var node = _mesh.DiscretizationNodes[i];
            var id = node.NodeId > 0 ? node.NodeId : i + 1;
            if (id != nodeId) continue;
            axis = node.Location;
            return true;
        }
        return false;
    }

    private int CountMeaningfulPoints(WaterPressureGridType type)
        => WaterPressureGrids
            .Where(g => g.GridType == type)
            .SelectMany(g => g.Points)
            .Count(IsMeaningfulPoint);

    private void NotifyGridPointCounts()
    {
        OnPropertyChanged(nameof(TotalHeadGridPointCount));
        OnPropertyChanged(nameof(PressureHeadGridPointCount));
        OnPropertyChanged(nameof(PorePressureGridPointCount));
    }

    private void ReplaceWaterPressureGrids(IReadOnlyList<WaterPressureGrid> source)
    {
        WaterPressureGrids.Clear();
        foreach (var g in source)
            WaterPressureGrids.Add(g);
        NotifyGridPointCounts();
    }

    private static List<WaterPressureGrid> ResolveGridsFromModel(GroundwaterModel model)
    {
        if (model.WaterPressureGrids is { Count: > 0 })
        {
            return model.WaterPressureGrids.Select(ToUiGrid).ToList();
        }

        // Legacy flat lists → one "Default Grid" per non-empty type.
        var result = new List<WaterPressureGrid>();
        AddLegacyDefault(result, WaterPressureGridType.TotalHead, model.TotalHeadGridPoints);
        AddLegacyDefault(result, WaterPressureGridType.PressureHead, model.PressureHeadGridPoints);
        AddLegacyDefault(result, WaterPressureGridType.PorePressure, model.PorePressureGridPoints);
        return result;
    }

    private static void AddLegacyDefault(
        List<WaterPressureGrid> target,
        WaterPressureGridType type,
        List<(double X, double Y, double Value)> points)
    {
        if (points == null || points.Count == 0)
            return;
        var grid = new WaterPressureGrid("Default Grid", type);
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            grid.Points.Add(new WaterPressureGridPoint(i + 1, p.X, p.Y, p.Value));
        }
        target.Add(grid);
    }

    private static WaterPressureGrid ToUiGrid(WaterPressureGridData data)
    {
        var grid = new WaterPressureGrid(
            string.IsNullOrWhiteSpace(data.Name) ? "Default Grid" : data.Name.Trim(),
            data.GridType);
        var pts = data.Points ?? new List<(double X, double Y, double Value)>();
        for (var i = 0; i < pts.Count; i++)
        {
            var p = pts[i];
            grid.Points.Add(new WaterPressureGridPoint(i + 1, p.X, p.Y, p.Value));
        }
        return grid;
    }

    private static List<WaterPressureGridData> SnapshotNamedGrids(ObservableCollection<WaterPressureGrid> grids)
        => grids.Select(g => new WaterPressureGridData
        {
            Name = g.Name,
            GridType = g.GridType,
            Points = g.Points
                .Where(IsMeaningfulPoint)
                .Select(p => (p.X, p.Y, p.Value))
                .ToList()
        }).ToList();

    private static List<(double X, double Y, double Value)> FlattenByType(
        List<WaterPressureGridData> grids,
        WaterPressureGridType type)
        => grids
            .Where(g => g.GridType == type)
            .SelectMany(g => g.Points)
            .ToList();

    private static bool IsMeaningfulPoint(WaterPressureGridPoint p)
        => Math.Abs(p.X) > 1e-15 || Math.Abs(p.Y) > 1e-15 || Math.Abs(p.Value) > 1e-15;

    private void AddHorizontalWaterTable()
    {
        if (GeometryDefinition == null) return;
        if (!double.TryParse(WaterTableInput, NumberStyles.Float, CultureInfo.CurrentCulture, out var elev))
            return;
        RecordUndo("Add groundwater line");
        GeometryDefinition.AddHorizontalWaterTable(elev);
        WaterTableInput = "";
        SyncToScript();
    }

    private void StartPolylineDraw()
    {
        IsPolylineDrawActive = true;
        PolylineStatus = "Click on the slope canvas to add phreatic surface points (snaps to geometry). Click Finish when done.";
        GeometryDefinition?.BeginGroundwaterPolylineDraw();
    }

    private void FinishPolylineDraw()
    {
        RecordUndo("Finish groundwater polyline");
        IsPolylineDrawActive = false;
        PolylineStatus = "";
        GeometryDefinition?.EndGroundwaterPolylineDraw();
        SyncToScript();
    }

    private void ClearGroundwater()
    {
        RecordUndo("Clear groundwater");
        GeometryDefinition?.ClearGroundwater();
        SyncToScript();
    }

    private void RemoveSelectedSurfPoint()
    {
        if (SelectedSurfPoint == null || GeometryDefinition == null) return;
        RecordUndo("Remove groundwater surface point");
        GeometryDefinition.RemoveGroundwaterSurfPoint(SelectedSurfPoint);
        SelectedSurfPoint = null;
        SyncToScript();
    }

    internal void OnSurfPointsChanged()
    {
        OnPropertyChanged(nameof(GroundwaterSurfPoints));
        OnPropertyChanged(nameof(GroundwaterLines));
        OnPropertyChanged(nameof(PondedFreeWaterStatus));
        SyncToScript();
    }

    private void SyncToScript()
    {
        if (_script == null) return;
        _script.SyncGroundwaterToScript();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
