using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using GeotechSlopeApp.Models.Geometry;
using GeotechSlopeApp.Services.Geometry;

namespace GeotechSlopeApp.ViewModels.Dialogs;

public enum JointWizardDialogResult
{
    Cancelled,
    Applied,
    ManualDraw
}

public sealed partial class JointWizardViewModel : INotifyPropertyChanged
{
    private readonly GeometryDefinitionViewModel _geometry;
    private FeJointPlacementMode _mode = FeJointPlacementMode.CrestToe;
    private GeologicalFeatureKind _kind = GeologicalFeatureKind.WeakPlane;
    private string _name = "WeakPlane 1";
    private double _dipAngleDeg = -45;
    private bool _useDipOverride;
    private double _throughX;
    private double _throughY;
    private double _tensionCrackDepth = 1;
    private int _parallelCount = 3;
    private double _parallelSpacing = 2;
    private int? _layerIndex;
    private string _status = "Choose a suggested location or a placement mode.";
    private GeologicalFeature? _editingFeature;
    private FeJointPropertySet? _selectedPropertySet;
    private FeJointEndCondition _endCondition = FeJointEndCondition.BothClosed;
    private int _installStage = 1;
    private IReadOnlyList<FeJointPolyline> _previewPolylines = Array.Empty<FeJointPolyline>();

    public JointWizardViewModel(GeometryDefinitionViewModel geometry)
    {
        _geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        _geometry.EnsureDefaultJointPropertySet();
        _selectedPropertySet = _geometry.JointPropertySets.FirstOrDefault();
        ApplySuggestionCommand = new RelayCommand<FeJointSuggestion>(ApplySuggestion);
        ApplyCommand = new RelayCommand(InvokeApply, () => CanApply);
        CancelCommand = new RelayCommand(() => CancelRequested?.Invoke(this, EventArgs.Empty));
        DeleteExistingCommand = new RelayCommand(DeleteExisting, () => SelectedExisting != null);
        SelectExistingCommand = new RelayCommand(LoadSelectedExisting, () => SelectedExisting != null);
        DefinePropertiesCommand = new RelayCommand(() => DefinePropertiesRequested?.Invoke(this, EventArgs.Empty));
        InitNetworkCommands();
        SeedFromGeometry();
        RefreshPreview();
    }

    public event EventHandler? ApplyRequested;
    public event EventHandler? CancelRequested;
    public event EventHandler? ManualDrawRequested;
    public event EventHandler? DefinePropertiesRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand ApplySuggestionCommand { get; }
    public ICommand ApplyCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand DeleteExistingCommand { get; }
    public ICommand SelectExistingCommand { get; }
    public ICommand DefinePropertiesCommand { get; }

    public ObservableCollection<FeJointSuggestion> Suggestions { get; } = new();
    public ObservableCollection<GeologicalFeature> ExistingJoints => _geometry.GeologicalFeatures;
    public ObservableCollection<FeJointPropertySet> JointPropertySets => _geometry.JointPropertySets;
    public IReadOnlyList<GeologicalFeatureKind> AvailableKinds { get; } =
        Enum.GetValues<GeologicalFeatureKind>();

    public GeologicalFeature? SelectedExisting
    {
        get => _editingFeature;
        set
        {
            _editingFeature = value;
            OnPropertyChanged();
            (DeleteExistingCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SelectExistingCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public FeJointPlacementMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            _mode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCrestToe));
            OnPropertyChanged(nameof(IsLayerContact));
            OnPropertyChanged(nameof(IsTensionCrack));
            OnPropertyChanged(nameof(IsThroughPoint));
            OnPropertyChanged(nameof(IsParallelSet));
            OnPropertyChanged(nameof(IsManualDraw));
            OnPropertyChanged(nameof(IsJointNetwork));
            OnPropertyChanged(nameof(ShowDip));
            OnPropertyChanged(nameof(ShowThroughPoint));
            OnPropertyChanged(nameof(ShowTensionDepth));
            OnPropertyChanged(nameof(ShowParallel));
            OnPropertyChanged(nameof(ShowDiscretePlacement));
            NotifyNetworkVisibility();
            if (value == FeJointPlacementMode.TensionCrack)
                Kind = GeologicalFeatureKind.TensionCrack;
            else if (Kind == GeologicalFeatureKind.TensionCrack)
                Kind = GeologicalFeatureKind.WeakPlane;
            if (value == FeJointPlacementMode.JointNetwork)
                EnsureNetworkDefaults();
            RefreshPreview();
        }
    }

    public bool IsCrestToe { get => Mode == FeJointPlacementMode.CrestToe; set { if (value) Mode = FeJointPlacementMode.CrestToe; } }
    public bool IsLayerContact { get => Mode == FeJointPlacementMode.LayerContact; set { if (value) Mode = FeJointPlacementMode.LayerContact; } }
    public bool IsTensionCrack { get => Mode == FeJointPlacementMode.TensionCrack; set { if (value) Mode = FeJointPlacementMode.TensionCrack; } }
    public bool IsThroughPoint { get => Mode == FeJointPlacementMode.ThroughPoint; set { if (value) Mode = FeJointPlacementMode.ThroughPoint; } }
    public bool IsParallelSet { get => Mode == FeJointPlacementMode.ParallelSet; set { if (value) Mode = FeJointPlacementMode.ParallelSet; } }
    public bool IsManualDraw { get => Mode == FeJointPlacementMode.ManualDraw; set { if (value) Mode = FeJointPlacementMode.ManualDraw; } }
    public bool IsJointNetwork { get => Mode == FeJointPlacementMode.JointNetwork; set { if (value) Mode = FeJointPlacementMode.JointNetwork; } }

    public bool ShowDiscretePlacement => Mode != FeJointPlacementMode.JointNetwork;
    public bool ShowDip => ShowDiscretePlacement && Mode is FeJointPlacementMode.CrestToe or FeJointPlacementMode.ThroughPoint
        or FeJointPlacementMode.ParallelSet or FeJointPlacementMode.TensionCrack;
    public bool ShowThroughPoint => ShowDiscretePlacement && Mode is FeJointPlacementMode.ThroughPoint or FeJointPlacementMode.ParallelSet;
    public bool ShowTensionDepth => Mode == FeJointPlacementMode.TensionCrack;
    public bool ShowParallel => Mode == FeJointPlacementMode.ParallelSet;

    public GeologicalFeatureKind Kind
    {
        get => _kind;
        set { if (_kind == value) return; _kind = value; OnPropertyChanged(); }
    }

    public string Name { get => _name; set { _name = value ?? ""; OnPropertyChanged(); } }
    public double DipAngleDeg { get => _dipAngleDeg; set { _dipAngleDeg = value; OnPropertyChanged(); RefreshPreview(); } }
    public bool UseDipOverride
    {
        get => _useDipOverride;
        set { _useDipOverride = value; OnPropertyChanged(); RefreshPreview(); }
    }
    public double ThroughX { get => _throughX; set { _throughX = value; OnPropertyChanged(); RefreshPreview(); } }
    public double ThroughY { get => _throughY; set { _throughY = value; OnPropertyChanged(); RefreshPreview(); } }
    public double TensionCrackDepth
    {
        get => _tensionCrackDepth;
        set { _tensionCrackDepth = Math.Max(0.05, value); OnPropertyChanged(); RefreshPreview(); }
    }
    public int ParallelCount
    {
        get => _parallelCount;
        set
        {
            _parallelCount = Math.Clamp(value, 1, FeJointPlacementService.MaxParallelCount);
            OnPropertyChanged();
            RefreshPreview();
        }
    }
    public double ParallelSpacing
    {
        get => _parallelSpacing;
        set { _parallelSpacing = Math.Max(0.05, value); OnPropertyChanged(); RefreshPreview(); }
    }

    public FeJointPropertySet? SelectedPropertySet
    {
        get => _selectedPropertySet;
        set { _selectedPropertySet = value; OnPropertyChanged(); }
    }

    public FeJointEndCondition EndCondition
    {
        get => _endCondition;
        set
        {
            if (_endCondition == value) return;
            _endCondition = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBothClosed));
            OnPropertyChanged(nameof(IsBothOpen));
            OnPropertyChanged(nameof(IsFirstClosedLastOpen));
            OnPropertyChanged(nameof(IsFirstOpenLastClosed));
        }
    }

    public bool IsBothClosed { get => EndCondition == FeJointEndCondition.BothClosed; set { if (value) EndCondition = FeJointEndCondition.BothClosed; } }
    public bool IsBothOpen { get => EndCondition == FeJointEndCondition.BothOpen; set { if (value) EndCondition = FeJointEndCondition.BothOpen; } }
    public bool IsFirstClosedLastOpen { get => EndCondition == FeJointEndCondition.FirstClosedLastOpen; set { if (value) EndCondition = FeJointEndCondition.FirstClosedLastOpen; } }
    public bool IsFirstOpenLastClosed { get => EndCondition == FeJointEndCondition.FirstOpenLastClosed; set { if (value) EndCondition = FeJointEndCondition.FirstOpenLastClosed; } }

    public int InstallStage
    {
        get => _installStage;
        set { _installStage = Math.Max(1, value); OnPropertyChanged(); }
    }

    public string Status { get => _status; set { _status = value ?? ""; OnPropertyChanged(); } }

    public void NotifyPropertySetsChanged()
    {
        OnPropertyChanged(nameof(JointPropertySets));
        if (SelectedPropertySet == null || !JointPropertySets.Contains(SelectedPropertySet))
            SelectedPropertySet = JointPropertySets.FirstOrDefault();
    }

    public void ApplyPropertySetEdits(IEnumerable<FeJointPropertySet> sets)
    {
        var selectedId = SelectedPropertySet?.Id;
        _geometry.ReplaceJointPropertySets(sets);
        SelectedPropertySet = _geometry.FindJointPropertySet(selectedId)
            ?? _geometry.JointPropertySets.FirstOrDefault();
        NotifyPropertySetsChanged();
    }

    public Point[] PreviewGroundAxis { get; private set; } = Array.Empty<Point>();
    public IReadOnlyList<Point[]> PreviewJointsAxis { get; private set; } = Array.Empty<Point[]>();
    public IReadOnlyList<Point[]> PreviewRegionsAxis { get; private set; } = Array.Empty<Point[]>();
    public IReadOnlyList<FeJointPolyline> PreviewPolylines => _previewPolylines;
    public bool CanApply => Mode == FeJointPlacementMode.ManualDraw
        || (Mode == FeJointPlacementMode.JointNetwork && HasSelectedNetworkRegion)
        || _previewPolylines.Count > 0;

    public JointWizardDraftProperties BuildDraftProperties()
    {
        var set = SelectedPropertySet;
        return new JointWizardDraftProperties
        {
            Kind = Kind,
            Name = Name,
            DipAngleDeg = UseDipOverride || Mode is FeJointPlacementMode.ThroughPoint or FeJointPlacementMode.ParallelSet
                ? DipAngleDeg
                : null,
            KnKpa = set?.NormalStiffnessKpaPerM,
            KsKpa = set?.ShearStiffnessKpaPerM,
            JointCohesionKpa = set?.PeakCohesionKpa,
            JointFrictionDeg = set?.PeakFrictionDeg,
            JointTensionCutoffKpa = set?.TensileStrengthKpa,
            PropertySetId = set?.Id,
            EndCondition = EndCondition,
            InstallStage = InstallStage
        };
    }

    public IReadOnlyList<GeologicalFeature> BuildFeatures()
    {
        var props = BuildDraftProperties();
        var list = new List<GeologicalFeature>();
        var i = 0;
        foreach (var poly in _previewPolylines)
        {
            i++;
            var feature = new GeologicalFeature
            {
                Id = Guid.NewGuid().ToString("N"),
                Kind = Kind,
                Name = _previewPolylines.Count == 1 ? Name : $"{Name} {i}",
                DipAngleDeg = props.DipAngleDeg,
                KnKpa = props.KnKpa,
                KsKpa = props.KsKpa,
                JointCohesionKpa = props.JointCohesionKpa,
                JointFrictionDeg = props.JointFrictionDeg,
                JointTensionCutoffKpa = props.JointTensionCutoffKpa,
                PropertySetId = props.PropertySetId,
                EndCondition = props.EndCondition,
                InstallStage = props.InstallStage
            };
            foreach (var v in poly.Vertices)
                feature.Vertices.Add(v);
            if (Mode == FeJointPlacementMode.JointNetwork && SelectedNetwork != null)
            {
                feature.JointNetworkId = SelectedNetwork.Id;
                feature.JointNetworkName = SelectedNetwork.Name;
            }
            list.Add(feature);
        }

        return list;
    }

    public bool IsEditing => _editingFeature != null;

    private void SeedFromGeometry()
    {
        var verts = _geometry.GeometryVertexRows.Select(v => (v.AxisX, v.AxisY)).ToList();
        var ground = FeJointPlacementService.BuildGroundEnvelope(verts);
        PreviewGroundAxis = ground.Select(p => new Point(p.X, p.Y)).ToArray();
        Suggestions.Clear();
        foreach (var s in FeJointPlacementService.Suggest(ground, _geometry.MaterialLayerCutElevations.ToList()))
            Suggestions.Add(s);

        if (FeJointPlacementService.TryFindCrest(ground, out var crest) &&
            FeJointPlacementService.TryFindToe(ground, crest, out var toe))
        {
            _dipAngleDeg = FeJointPlacementService.ChordDipDeg(crest, toe);
            _throughX = 0.5 * (crest.X + toe.X);
            _throughY = 0.5 * (crest.Y + toe.Y);
            _tensionCrackDepth = FeJointPlacementService.DefaultTensionDepth(ground);
        }
        else
        {
            _throughX = 0.5 * (_geometry.AxisXMin + _geometry.AxisXMax);
            _throughY = 0.5 * (_geometry.AxisYMin + _geometry.AxisYMax);
        }

        var n = _geometry.GeologicalFeatures.Count(f => f.Kind == Kind) + 1;
        _name = $"{Kind} {n}";
        if (ground.Count < 2)
        {
            _mode = FeJointPlacementMode.ThroughPoint;
            _status = "No slope outline yet — place a through-point joint or draw manually.";
        }

        OnPropertyChanged(nameof(DipAngleDeg));
        OnPropertyChanged(nameof(ThroughX));
        OnPropertyChanged(nameof(ThroughY));
        OnPropertyChanged(nameof(TensionCrackDepth));
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(IsCrestToe));
        OnPropertyChanged(nameof(IsThroughPoint));
        OnPropertyChanged(nameof(IsManualDraw));
        OnPropertyChanged(nameof(ShowDip));
        OnPropertyChanged(nameof(ShowThroughPoint));
        OnPropertyChanged(nameof(ShowTensionDepth));
        OnPropertyChanged(nameof(ShowParallel));
        OnPropertyChanged(nameof(PreviewGroundAxis));
    }

    private void ApplySuggestion(FeJointSuggestion? suggestion)
    {
        if (suggestion == null) return;
        _layerIndex = suggestion.LayerIndex;
        UseDipOverride = false;
        Mode = suggestion.Mode;
        Status = suggestion.Hint;
    }

    private void LoadSelectedExisting()
    {
        if (SelectedExisting == null) return;
        var f = SelectedExisting;
        Kind = f.Kind;
        Name = f.Name;
        SelectedPropertySet = _geometry.FindJointPropertySet(f.PropertySetId) ?? SelectedPropertySet;
        EndCondition = f.EndCondition;
        InstallStage = f.InstallStage > 0 ? f.InstallStage : 1;
        if (f.DipAngleDeg is double dip)
        {
            UseDipOverride = true;
            DipAngleDeg = dip;
        }

        _previewPolylines = new[] { new FeJointPolyline(f.Name, f.Vertices.ToList()) };
        PreviewJointsAxis = new[] { f.Vertices.Select(v => new Point(v.X, v.Y)).ToArray() };
        Status = $"Editing {f.Name}. Change placement to relocate, or Apply to update properties.";
        OnPropertyChanged(nameof(PreviewJointsAxis));
        OnPropertyChanged(nameof(PreviewPolylines));
        OnPropertyChanged(nameof(IsEditing));
        (ApplyCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void DeleteExisting()
    {
        if (SelectedExisting == null) return;
        _geometry.RemoveGeologicalFeature(SelectedExisting);
        SelectedExisting = null;
        Status = "Joint removed.";
        RefreshPreview();
    }

    public void RefreshPreview()
    {
        var verts = _geometry.GeometryVertexRows.Select(v => (v.AxisX, v.AxisY)).ToList();
        var ground = FeJointPlacementService.BuildGroundEnvelope(verts);
        PreviewGroundAxis = ground.Select(p => new Point(p.X, p.Y)).ToArray();
        OnPropertyChanged(nameof(PreviewGroundAxis));

        if (Mode == FeJointPlacementMode.ManualDraw)
        {
            _previewPolylines = Array.Empty<FeJointPolyline>();
            PreviewJointsAxis = Array.Empty<Point[]>();
            PreviewRegionsAxis = Array.Empty<Point[]>();
            Status = "Apply closes the wizard and starts canvas drawing with these properties.";
            OnPropertyChanged(nameof(PreviewJointsAxis));
            OnPropertyChanged(nameof(PreviewPolylines));
            OnPropertyChanged(nameof(PreviewRegionsAxis));
            (ApplyCommand as RelayCommand)?.RaiseCanExecuteChanged();
            return;
        }

        if (Mode == FeJointPlacementMode.JointNetwork)
        {
            RefreshNetworkPreview();
            return;
        }

        var request = new FeJointPlacementRequest(
            Mode,
            ground,
            verts.Count >= 3 ? verts : null,
            _geometry.MaterialLayerCutElevations.ToList(),
            _geometry.AxisXMin,
            _geometry.AxisXMax,
            _geometry.AxisYMin,
            _geometry.AxisYMax,
            DipAngleDeg,
            UseDipOverride,
            ThroughX,
            ThroughY,
            TensionCrackDepth,
            ParallelCount,
            ParallelSpacing,
            _layerIndex);

        _previewPolylines = FeJointPlacementService.Place(request);
        PreviewJointsAxis = _previewPolylines
            .Select(p => p.Vertices.Select(v => new Point(v.X, v.Y)).ToArray())
            .ToList();
        Status = _previewPolylines.Count == 0
            ? "No joint fits the current geometry. Adjust dip, intercept, or pick another location."
            : $"{_previewPolylines.Count} joint(s) ready — {string.Join(", ", _previewPolylines.Select(p => p.Label))}.";
        OnPropertyChanged(nameof(PreviewJointsAxis));
        OnPropertyChanged(nameof(PreviewPolylines));
        (ApplyCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void InvokeApply()
    {
        if (Mode == FeJointPlacementMode.ManualDraw)
        {
            ManualDrawRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_previewPolylines.Count == 0)
            return;
        ApplyRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
