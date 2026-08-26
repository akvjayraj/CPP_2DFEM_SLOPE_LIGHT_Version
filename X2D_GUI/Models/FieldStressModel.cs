using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GeotechSlopeApp.Models;

/// <summary>
/// Field-stress modes offered by the p610_t3 solver, mirroring RS2's "Constant" and "Gravity" options.
///
/// Constant: single position-independent stress tensor (σ1, σ3, σZ, angle) applied everywhere — used
/// for deep excavations where the depth gradient is small compared to the mean stress.
///
/// Gravity: per-material γ scaled by centroid depth, with user-selectable Kx, Kz and an optional
/// explicit free-surface elevation override. Reduces to the historical y_top/K0 behaviour when the
/// user leaves the surface in "auto" mode.
/// </summary>
public enum FieldStressMode
{
    Direct,
    Constant,
    Gravity,
    /// <summary>
    /// Explicit traction-loading block from the .dat (n_traction_groups + per-group
    /// mode/sxx/syy/sxy + per-edge node IDs). Selected automatically when a .dat is
    /// imported and a traction block is present; the parsed values populate
    /// <see cref="FieldStressModel.TractionGroups"/> and round-trip back to the .dat.
    /// </summary>
    Traction,
}

/// <summary>
/// Concrete field-stress parameters for a single scope (global default or per-material override).
/// Both modes store their knobs here so switching modes preserves the other mode's values in the UI.
/// </summary>
public partial class FieldStressSpec : ObservableObject
{
    [ObservableProperty] private FieldStressMode _mode = FieldStressMode.Gravity;

    // Constant-mode parameters (used when Mode == Constant).
    // Sign convention: Smith-Griffiths tension-positive. Enter compressive stresses as negative values.
    [ObservableProperty] private double _sigma1  = -1000.0; // σ1 (major in-plane principal), kPa
    [ObservableProperty] private double _sigma3  = -500.0;  // σ3 (minor in-plane principal), kPa
    [ObservableProperty] private double _sigmaZ  = -750.0;  // σZ (out-of-plane), kPa
    [ObservableProperty] private double _angleDeg = 0.0;    // θ CCW from +x axis to σ1 direction, degrees

    // Direct traction tensor parameters (used when Mode == Direct).
    // These are the values written/read by p610 elastic traction groups: mode=0 sxx syy txy.
    [ObservableProperty] private double _directSxx = -100.0;
    [ObservableProperty] private double _directSyy = -100.0;
    [ObservableProperty] private double _directTxy = 0.0;

    // Gravity-mode parameters (used when Mode == Gravity).
    [ObservableProperty] private double _kx = 0.5;              // horizontal-to-vertical stress ratio in-plane
    [ObservableProperty] private double _kz = 0.5;              // out-of-plane stress ratio
    [ObservableProperty] private bool   _autoSurface = true;    // true = use max-y of mesh as free surface
    [ObservableProperty] private double _ySurface = 0.0;        // user-specified free-surface elevation (axis units), used only when AutoSurface = false
    [ObservableProperty] private bool   _useEffectiveStressRatio = false; // when true, solver interprets Kx/Kz as effective-stress ratios against porewater

    public FieldStressSpec Clone() => new()
    {
        Mode = Mode,
        Sigma1 = Sigma1,
        Sigma3 = Sigma3,
        SigmaZ = SigmaZ,
        AngleDeg = AngleDeg,
        DirectSxx = DirectSxx,
        DirectSyy = DirectSyy,
        DirectTxy = DirectTxy,
        Kx = Kx,
        Kz = Kz,
        AutoSurface = AutoSurface,
        YSurface = YSurface,
        UseEffectiveStressRatio = UseEffectiveStressRatio,
    };
}

/// <summary>
/// A per-material override of the global field stress (RS2's "Advanced / Custom Field Stress for
/// individual materials" option). <see cref="MaterialIndex"/> is 1-based to match the solver's
/// 1-based material numbering.
/// </summary>
public partial class FieldStressOverride : ObservableObject
{
    [ObservableProperty] private int _materialIndex = 1;
    [ObservableProperty] private FieldStressSpec _spec = new();
    [ObservableProperty] private string _materialName = "";

    public FieldStressOverride()
    {
        Spec.PropertyChanged += OnSpecPropertyChanged;
    }

    partial void OnSpecChanging(FieldStressSpec value)
        => Spec.PropertyChanged -= OnSpecPropertyChanged;

    partial void OnSpecChanged(FieldStressSpec value)
    {
        value.PropertyChanged += OnSpecPropertyChanged;
        RefreshModeHelpers();
    }

    private void OnSpecPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FieldStressSpec.Mode))
            RefreshModeHelpers();
    }

    private void RefreshModeHelpers()
    {
        OnPropertyChanged(nameof(IsConstantMode));
        OnPropertyChanged(nameof(IsGravityMode));
        OnPropertyChanged(nameof(IsDirectMode));
        OnPropertyChanged(nameof(IsTractionMode));
        OnPropertyChanged(nameof(IsConstantModeSelected));
        OnPropertyChanged(nameof(IsGravityModeSelected));
        OnPropertyChanged(nameof(IsDirectModeSelected));
        OnPropertyChanged(nameof(IsTractionModeSelected));
    }

    public bool IsConstantMode => Spec.Mode == FieldStressMode.Constant;
    public bool IsGravityMode  => Spec.Mode == FieldStressMode.Gravity;
    public bool IsDirectMode   => Spec.Mode == FieldStressMode.Direct;
    public bool IsTractionMode => Spec.Mode == FieldStressMode.Traction;

    public bool IsConstantModeSelected
    {
        get => Spec.Mode == FieldStressMode.Constant;
        set { if (value && Spec.Mode != FieldStressMode.Constant) Spec.Mode = FieldStressMode.Constant; }
    }

    public bool IsGravityModeSelected
    {
        get => Spec.Mode == FieldStressMode.Gravity;
        set { if (value && Spec.Mode != FieldStressMode.Gravity) Spec.Mode = FieldStressMode.Gravity; }
    }

    public bool IsDirectModeSelected
    {
        get => Spec.Mode == FieldStressMode.Direct;
        set { if (value && Spec.Mode != FieldStressMode.Direct) Spec.Mode = FieldStressMode.Direct; }
    }

    public bool IsTractionModeSelected
    {
        get => Spec.Mode == FieldStressMode.Traction;
        set { if (value && Spec.Mode != FieldStressMode.Traction) Spec.Mode = FieldStressMode.Traction; }
    }

    public FieldStressOverride Clone() => new()
    {
        MaterialIndex = MaterialIndex,
        MaterialName = MaterialName,
        Spec = Spec.Clone(),
    };
}

/// <summary>
/// One node-pair (or node-triple for T6/Q8 quadratic edges) defining a single boundary edge
/// loaded by a traction group. Mirrors one row of the .dat traction block:
/// <c>nodeA  nodeB  [nodeC]</c> where <c>nodeC</c> is the optional mid-edge node for quadratic
/// elements. Stored 1-based to match solver convention.
/// </summary>
public partial class TractionEdge : ObservableObject
{
    [ObservableProperty] private int _nodeA;
    [ObservableProperty] private int _nodeB;
    [ObservableProperty] private int _nodeC; // 0 = no mid-edge node (T3/Q4); >0 = quadratic edge

    public TractionEdge Clone() => new() { NodeA = NodeA, NodeB = NodeB, NodeC = NodeC };
}

/// <summary>
/// One traction group from the .dat <c>n_traction_groups</c> block. Mirrors:
/// <code>
/// mode  p0  p1  p2     ! mode=0 direct: sxx=p0, syy=p1, sxy=p2
/// n_edges
/// n_edges rows: nodeA  nodeB  [nodeC]
/// </code>
/// </summary>
public partial class TractionGroup : ObservableObject
{
    [ObservableProperty] private int _mode;       // 0 = direct sxx/syy/sxy; 1 = far-field sigma_v/K0
    [ObservableProperty] private double _sxx;
    [ObservableProperty] private double _syy;
    [ObservableProperty] private double _txy;

    public ObservableCollection<TractionEdge> Edges { get; } = new();

    public TractionGroup Clone()
    {
        var clone = new TractionGroup
        {
            Mode = Mode,
            Sxx  = Sxx,
            Syy  = Syy,
            Txy  = Txy,
        };
        foreach (var e in Edges) clone.Edges.Add(e.Clone());
        return clone;
    }
}

/// <summary>
/// Project-level field-stress configuration: one global default spec plus an optional list of
/// per-material overrides. Serialisable on its own via <see cref="Persistence.XfemProjectPersistence"/>.
/// </summary>
public partial class FieldStressModel : ObservableObject
{
    [ObservableProperty] private FieldStressSpec _global = new();

    public ObservableCollection<FieldStressOverride> Overrides { get; } = new();

    /// <summary>
    /// Explicit traction-group block parsed from the .dat (or hand-edited via the GUI). Empty by
    /// default — the elastic .dat builder falls back to its convex-hull auto-edge heuristic when
    /// this list is empty, so brand-new projects keep their previous behaviour.
    /// </summary>
    public ObservableCollection<TractionGroup> TractionGroups { get; } = new();

    public FieldStressModel Clone()
    {
        var clone = new FieldStressModel { Global = Global.Clone() };
        foreach (var o in Overrides)
            clone.Overrides.Add(o.Clone());
        foreach (var g in TractionGroups)
            clone.TractionGroups.Add(g.Clone());
        return clone;
    }
}
