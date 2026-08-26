using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.ViewModels;

/// <summary>
/// ViewModel wrapper around <see cref="FieldStressModel"/> with commands for the Load Conditions
/// tab's Field Stress panel and the Advanced per-material overrides dialog. The underlying
/// <see cref="FieldStressModel"/> is persisted to .xfem; this class owns all user-interaction
/// wiring (PropertyChanged bubbling, commands, materials list for the override picker).
/// </summary>
public class FieldStressViewModel : INotifyPropertyChanged
{
    private readonly FieldStressModel _model;
    private MaterialPropertiesViewModel? _materialsVM;
    private FieldStressOverride? _selectedOverride;
    private TractionEdge? _selectedTractionEdge;

    public FieldStressViewModel() : this(new FieldStressModel()) { }

    public FieldStressViewModel(FieldStressModel model)
    {
        _model = model ?? new FieldStressModel();
        _model.Global.PropertyChanged += OnGlobalSpecChanged;
        _model.Overrides.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Overrides));
            OnPropertyChanged(nameof(HasOverrides));
            OnPropertyChanged(nameof(OverrideCount));
            FieldStressChanged?.Invoke(this, System.EventArgs.Empty);
        };
        foreach (var ov in _model.Overrides)
            WireOverride(ov);

        _model.TractionGroups.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
                foreach (var item in e.NewItems)
                    if (item is TractionGroup g) WireTractionGroup(g);

            OnPropertyChanged(nameof(TractionGroups));
            OnPropertyChanged(nameof(HasTractionGroups));
            OnPropertyChanged(nameof(TractionGroupCount));
            FieldStressChanged?.Invoke(this, System.EventArgs.Empty);
        };
        foreach (var grp in _model.TractionGroups)
            WireTractionGroup(grp);

        AddOverrideCommand = new RelayCommand(OnAddOverride, () => _materialsVM?.Materials?.Count > 0);
        RemoveOverrideCommand = new RelayCommand<FieldStressOverride?>(OnRemoveOverride, o => o != null);

        AddTractionGroupCommand = new RelayCommand(OnAddTractionGroup);
        RemoveTractionGroupCommand = new RelayCommand<TractionGroup?>(OnRemoveTractionGroup, g => g != null);
        AddTractionEdgeCommand = new RelayCommand<TractionGroup?>(OnAddTractionEdge, g => g != null);
        RemoveTractionEdgeCommand = new RelayCommand<TractionEdge?>(OnRemoveTractionEdge, e => e != null);
    }

    /// <summary>The model instance owned by this VM — persist/apply through this reference.</summary>
    public FieldStressModel Model => _model;

    /// <summary>Global default spec applied to every element not covered by an override.</summary>
    public FieldStressSpec Global => _model.Global;

    public ObservableCollection<FieldStressOverride> Overrides => _model.Overrides;
    public bool HasOverrides => _model.Overrides.Count > 0;
    public int OverrideCount => _model.Overrides.Count;

    /// <summary>
    /// Explicit traction-group block (mode + sxx/syy/sxy + edges) parsed from the loaded .dat
    /// or hand-edited via the GUI. Empty for fresh projects, in which case the elastic .dat
    /// builder falls back to its convex-hull auto-edge heuristic.
    /// </summary>
    public ObservableCollection<TractionGroup> TractionGroups => _model.TractionGroups;
    public bool HasTractionGroups => _model.TractionGroups.Count > 0;
    public int TractionGroupCount => _model.TractionGroups.Count;

    public FieldStressOverride? SelectedOverride
    {
        get => _selectedOverride;
        set
        {
            _selectedOverride = value;
            OnPropertyChanged();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Materials list for the override material picker; set by MainWindowViewModel after construction.</summary>
    public ObservableCollection<MaterialModel>? MaterialsSource => _materialsVM?.Materials;

    public void SetMaterialsSource(MaterialPropertiesViewModel? materialsVM)
    {
        _materialsVM = materialsVM;
        OnPropertyChanged(nameof(MaterialsSource));
        CommandManager.InvalidateRequerySuggested();
    }

    public ICommand AddOverrideCommand { get; }
    public ICommand RemoveOverrideCommand { get; }

    public ICommand AddTractionGroupCommand { get; }
    public ICommand RemoveTractionGroupCommand { get; }
    public ICommand AddTractionEdgeCommand { get; }
    public ICommand RemoveTractionEdgeCommand { get; }

    /// <summary>Raised whenever any field-stress parameter changes (global or override). ScriptViewModel listens to re-emit the .dat.</summary>
    public event System.EventHandler? FieldStressChanged;

    /// <summary>
    /// The traction-edge row currently selected in the Traction DataGrid (Load Conditions panel
    /// or the per-material overrides dialog). LoadConditionsViewModel watches this so the
    /// load canvas can highlight the corresponding A→B (and optional C mid-edge) polyline.
    /// </summary>
    public TractionEdge? SelectedTractionEdge
    {
        get => _selectedTractionEdge;
        set
        {
            if (ReferenceEquals(_selectedTractionEdge, value)) return;
            _selectedTractionEdge = value;
            OnPropertyChanged();
            SelectedTractionEdgeChanged?.Invoke(this, System.EventArgs.Empty);
        }
    }

    /// <summary>Raised whenever <see cref="SelectedTractionEdge"/> changes. The Load Conditions
    /// canvas overlay subscribes so it can refresh the highlighted A→B polyline.</summary>
    public event System.EventHandler? SelectedTractionEdgeChanged;

    // ── Mode visibility helpers (bound from the XAML panel) ─────────────────
    public bool IsDirectMode => Global.Mode == FieldStressMode.Direct;
    public bool IsConstantMode => Global.Mode == FieldStressMode.Constant;
    public bool IsGravityMode  => Global.Mode == FieldStressMode.Gravity;
    public bool IsTractionMode => Global.Mode == FieldStressMode.Traction;

    public bool IsDirectModeSelected
    {
        get => Global.Mode == FieldStressMode.Direct;
        set { if (value && Global.Mode != FieldStressMode.Direct) Global.Mode = FieldStressMode.Direct; }
    }

    public bool IsConstantModeSelected
    {
        get => Global.Mode == FieldStressMode.Constant;
        set { if (value && Global.Mode != FieldStressMode.Constant) Global.Mode = FieldStressMode.Constant; }
    }

    public bool IsGravityModeSelected
    {
        get => Global.Mode == FieldStressMode.Gravity;
        set { if (value && Global.Mode != FieldStressMode.Gravity) Global.Mode = FieldStressMode.Gravity; }
    }

    public bool IsTractionModeSelected
    {
        get => Global.Mode == FieldStressMode.Traction;
        set { if (value && Global.Mode != FieldStressMode.Traction) Global.Mode = FieldStressMode.Traction; }
    }

    private void OnGlobalSpecChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FieldStressSpec.Mode))
        {
            OnPropertyChanged(nameof(IsDirectMode));
            OnPropertyChanged(nameof(IsConstantMode));
            OnPropertyChanged(nameof(IsGravityMode));
            OnPropertyChanged(nameof(IsTractionMode));
            OnPropertyChanged(nameof(IsDirectModeSelected));
            OnPropertyChanged(nameof(IsConstantModeSelected));
            OnPropertyChanged(nameof(IsGravityModeSelected));
            OnPropertyChanged(nameof(IsTractionModeSelected));
        }
        FieldStressChanged?.Invoke(this, System.EventArgs.Empty);
    }

    private void WireOverride(FieldStressOverride ov)
    {
        ov.PropertyChanged += (_, _) => FieldStressChanged?.Invoke(this, System.EventArgs.Empty);
        ov.Spec.PropertyChanged += (_, _) => FieldStressChanged?.Invoke(this, System.EventArgs.Empty);
    }

    private void WireTractionGroup(TractionGroup grp)
    {
        grp.PropertyChanged += (_, _) => FieldStressChanged?.Invoke(this, System.EventArgs.Empty);
        grp.Edges.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
                foreach (var item in e.NewItems)
                    if (item is TractionEdge te) WireTractionEdge(te);
            FieldStressChanged?.Invoke(this, System.EventArgs.Empty);
        };
        foreach (var edge in grp.Edges) WireTractionEdge(edge);
    }

    private void WireTractionEdge(TractionEdge edge)
    {
        edge.PropertyChanged += (_, _) => FieldStressChanged?.Invoke(this, System.EventArgs.Empty);
    }

    private void OnAddTractionGroup()
    {
        // Seed a new group with the current Direct-tensor stresses so a user who is just
        // exploring the panel sees sensible defaults instead of all zeros.
        var grp = new TractionGroup
        {
            Mode = 0,
            Sxx  = Global.DirectSxx,
            Syy  = Global.DirectSyy,
            Txy  = Global.DirectTxy,
        };
        _model.TractionGroups.Add(grp);
    }

    private void OnRemoveTractionGroup(TractionGroup? grp)
    {
        if (grp == null) return;
        if (_selectedTractionEdge != null && grp.Edges.Contains(_selectedTractionEdge))
            SelectedTractionEdge = null;
        _model.TractionGroups.Remove(grp);
    }

    private void OnAddTractionEdge(TractionGroup? grp)
    {
        if (grp == null) return;
        grp.Edges.Add(new TractionEdge { NodeA = 0, NodeB = 0, NodeC = 0 });
    }

    private void OnRemoveTractionEdge(TractionEdge? edge)
    {
        if (edge == null) return;
        if (ReferenceEquals(_selectedTractionEdge, edge))
            SelectedTractionEdge = null;
        foreach (var grp in _model.TractionGroups)
        {
            if (grp.Edges.Remove(edge)) return;
        }
    }

    private void OnAddOverride()
    {
        if (_materialsVM?.Materials == null || _materialsVM.Materials.Count == 0) return;
        int firstFree = 1;
        while (_model.Overrides.Any(o => o.MaterialIndex == firstFree) && firstFree <= _materialsVM.Materials.Count)
            firstFree++;
        if (firstFree > _materialsVM.Materials.Count) firstFree = 1;
        var mat = _materialsVM.Materials.Count >= firstFree ? _materialsVM.Materials[firstFree - 1] : _materialsVM.Materials[0];

        // Per-material overrides cannot use Traction mode — traction groups are a
        // domain-global property of the .dat traction block, not a per-material spec.
        // Demote a Traction-mode global to Direct tensor when seeding the override so
        // the override editor (which only renders Gravity/Constant/Direct radios)
        // always has a matching panel to display.
        var seedSpec = Global.Clone();
        if (seedSpec.Mode == FieldStressMode.Traction)
            seedSpec.Mode = FieldStressMode.Direct;

        var ov = new FieldStressOverride
        {
            MaterialIndex = firstFree,
            MaterialName = mat.Name,
            Spec = seedSpec,
        };
        WireOverride(ov);
        _model.Overrides.Add(ov);
        SelectedOverride = ov;
    }

    private void OnRemoveOverride(FieldStressOverride? ov)
    {
        if (ov == null) return;
        _model.Overrides.Remove(ov);
        if (ReferenceEquals(SelectedOverride, ov)) SelectedOverride = null;
    }

    /// <summary>Replace the underlying model state from a loaded project file; wires change events.</summary>
    public void LoadFromModel(FieldStressModel? loaded)
    {
        SelectedTractionEdge = null;
        _model.Global.Mode = loaded?.Global?.Mode ?? FieldStressMode.Gravity;
        if (loaded?.Global != null)
        {
            _model.Global.Sigma1 = loaded.Global.Sigma1;
            _model.Global.Sigma3 = loaded.Global.Sigma3;
            _model.Global.SigmaZ = loaded.Global.SigmaZ;
            _model.Global.AngleDeg = loaded.Global.AngleDeg;
            _model.Global.DirectSxx = loaded.Global.DirectSxx;
            _model.Global.DirectSyy = loaded.Global.DirectSyy;
            _model.Global.DirectTxy = loaded.Global.DirectTxy;
            _model.Global.Kx = loaded.Global.Kx;
            _model.Global.Kz = loaded.Global.Kz;
            _model.Global.AutoSurface = loaded.Global.AutoSurface;
            _model.Global.YSurface = loaded.Global.YSurface;
            _model.Global.UseEffectiveStressRatio = loaded.Global.UseEffectiveStressRatio;
        }
        _model.Overrides.Clear();
        if (loaded?.Overrides != null)
            foreach (var ov in loaded.Overrides)
            {
                var copy = ov.Clone();
                WireOverride(copy);
                _model.Overrides.Add(copy);
            }

        _model.TractionGroups.Clear();
        if (loaded?.TractionGroups != null)
            foreach (var grp in loaded.TractionGroups)
                _model.TractionGroups.Add(grp.Clone());

        OnPropertyChanged(nameof(Global));
        OnPropertyChanged(nameof(Overrides));
        OnPropertyChanged(nameof(HasOverrides));
        OnPropertyChanged(nameof(OverrideCount));
        OnPropertyChanged(nameof(TractionGroups));
        OnPropertyChanged(nameof(HasTractionGroups));
        OnPropertyChanged(nameof(TractionGroupCount));
        OnPropertyChanged(nameof(IsDirectMode));
        OnPropertyChanged(nameof(IsConstantMode));
        OnPropertyChanged(nameof(IsGravityMode));
        OnPropertyChanged(nameof(IsTractionMode));
        OnPropertyChanged(nameof(IsDirectModeSelected));
        OnPropertyChanged(nameof(IsConstantModeSelected));
        OnPropertyChanged(nameof(IsGravityModeSelected));
        OnPropertyChanged(nameof(IsTractionModeSelected));
        FieldStressChanged?.Invoke(this, System.EventArgs.Empty);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
