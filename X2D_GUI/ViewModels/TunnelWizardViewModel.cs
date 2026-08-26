using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.ViewModels;

/// <summary>
/// Stress preset baked into the wizard's "Stress" step. Maps to a configured
/// <see cref="FieldStressMode"/> + numeric values applied through <see cref="FieldStressViewModel.Global"/>.
/// </summary>
public enum TunnelStressPreset
{
    /// <summary>σ_v = σ_h = σ_z (lithostatic, K=1). Most common Kirsch-style benchmark.</summary>
    Hydrostatic,
    /// <summary>σ_v from depth × γ; σ_h = K0 × σ_v.</summary>
    K0Lateral,
    /// <summary>Pure self-weight gravity (FieldStressMode.Gravity).</summary>
    Gravity,
    /// <summary>User-supplied direct (σ_xx, σ_yy, τ_xy) tensor.</summary>
    Direct,
}

/// <summary>
/// Mesh density preset for the wizard's "Mesh" step. Sets far-field / near-tunnel edge lengths
/// and ring refinement counts so a non-expert user can pick "Fine" without typing numbers.
/// </summary>
public enum TunnelMeshPreset
{
    Coarse,
    Medium,
    Fine,
}

/// <summary>
/// State machine for the Tunnel Wizard dialog. Holds all parametric inputs across six steps,
/// derives a live polygon preview from the current shape parameters, and delegates the actual
/// model mutation to <see cref="TunnelWizardApplier"/>.
/// <para>
/// The view-model is <em>independent</em> of any specific dialog window — wire it to the dialog's
/// DataContext, listen on <see cref="ApplyRequested"/> for the close-after-apply signal, and call
/// <see cref="Reset"/> if the dialog is opened a second time.
/// </para>
/// </summary>
public sealed class TunnelWizardViewModel : INotifyPropertyChanged
{
    public const int StepShape       = 0;
    public const int StepDimensions  = 1;
    public const int StepDomain      = 2;
    public const int StepStress      = 3;
    public const int StepMesh        = 4;
    public const int StepReview      = 5;
    public const int StepCount       = 6;

    public TunnelWizardViewModel()
    {
        ShapeParams = new TunnelShapeParams();
        NextStepCommand   = new RelayCommand(GoNext,   () => CurrentStepIndex < StepCount - 1);
        PrevStepCommand   = new RelayCommand(GoPrev,   () => CurrentStepIndex > 0);
        ApplyCommand      = new RelayCommand(InvokeApply, () => CanApply);
        CancelCommand     = new RelayCommand(InvokeCancel);
        ApplyShapePresetCommand = new RelayCommand<string?>(ApplyShapePreset);
        ApplyMeshPresetCommand  = new RelayCommand<string?>(ApplyMeshPreset);
        ApplyStressPresetCommand = new RelayCommand<string?>(ApplyStressPreset);

        ApplyDefaults();
        RefreshPreview();
    }

    // ── Step ────────────────────────────────────────────────────────────────

    private int _currentStepIndex;
    public int CurrentStepIndex
    {
        get => _currentStepIndex;
        private set
        {
            if (_currentStepIndex == value) return;
            _currentStepIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsStepShape));
            OnPropertyChanged(nameof(IsStepDimensions));
            OnPropertyChanged(nameof(IsStepDomain));
            OnPropertyChanged(nameof(IsStepStress));
            OnPropertyChanged(nameof(IsStepMesh));
            OnPropertyChanged(nameof(IsStepReview));
            OnPropertyChanged(nameof(StepLabel));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsStepShape      => CurrentStepIndex == StepShape;
    public bool IsStepDimensions => CurrentStepIndex == StepDimensions;
    public bool IsStepDomain     => CurrentStepIndex == StepDomain;
    public bool IsStepStress     => CurrentStepIndex == StepStress;
    public bool IsStepMesh       => CurrentStepIndex == StepMesh;
    public bool IsStepReview     => CurrentStepIndex == StepReview;

    public string StepLabel => CurrentStepIndex switch
    {
        StepShape       => "Step 1 of 6 — Shape",
        StepDimensions  => "Step 2 of 6 — Dimensions",
        StepDomain      => "Step 3 of 6 — Outer domain",
        StepStress      => "Step 4 of 6 — Field stress",
        StepMesh        => "Step 5 of 6 — Mesh density",
        StepReview      => "Step 6 of 6 — Review and apply",
        _ => $"Step {CurrentStepIndex + 1} of {StepCount}",
    };

    // ── Shape ───────────────────────────────────────────────────────────────

    public TunnelShapeParams ShapeParams { get; }

    public TunnelShapeKind ShapeKind
    {
        get => ShapeParams.Kind;
        set
        {
            if (ShapeParams.Kind == value) return;
            ShapeParams.Kind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShapeIsCircle));
            OnPropertyChanged(nameof(ShapeIsRectangular));
            OnPropertyChanged(nameof(ShapeIsHorseshoe));
            OnPropertyChanged(nameof(ShapeIsNatm));
            RefreshPreview();
        }
    }

    public bool ShapeIsCircle      => ShapeKind == TunnelShapeKind.Circle;
    public bool ShapeIsRectangular => ShapeKind == TunnelShapeKind.Rectangular;
    public bool ShapeIsHorseshoe   => ShapeKind == TunnelShapeKind.Horseshoe;
    public bool ShapeIsNatm        => ShapeKind == TunnelShapeKind.NatmArch;

    public double CenterX
    {
        get => ShapeParams.CenterX;
        set { if (NearlyEqual(ShapeParams.CenterX, value)) return; ShapeParams.CenterX = value; OnPropertyChanged(); RefreshPreview(); }
    }
    public double CenterY
    {
        get => ShapeParams.CenterY;
        set { if (NearlyEqual(ShapeParams.CenterY, value)) return; ShapeParams.CenterY = value; OnPropertyChanged(); RefreshPreview(); }
    }
    public double Radius
    {
        get => ShapeParams.Radius;
        set { if (NearlyEqual(ShapeParams.Radius, value)) return; ShapeParams.Radius = value; OnPropertyChanged(); RefreshPreview(); }
    }
    public double Width
    {
        get => ShapeParams.Width;
        set { if (NearlyEqual(ShapeParams.Width, value)) return; ShapeParams.Width = value; OnPropertyChanged(); RefreshPreview(); }
    }
    public double Height
    {
        get => ShapeParams.Height;
        set { if (NearlyEqual(ShapeParams.Height, value)) return; ShapeParams.Height = value; OnPropertyChanged(); RefreshPreview(); }
    }
    public double HorseshoeSpan
    {
        get => ShapeParams.HorseshoeSpan;
        set { if (NearlyEqual(ShapeParams.HorseshoeSpan, value)) return; ShapeParams.HorseshoeSpan = value; OnPropertyChanged(); RefreshPreview(); }
    }
    public double HorseshoeHeight
    {
        get => ShapeParams.HorseshoeHeight;
        set { if (NearlyEqual(ShapeParams.HorseshoeHeight, value)) return; ShapeParams.HorseshoeHeight = value; OnPropertyChanged(); RefreshPreview(); }
    }
    public double NatmCrownRadius
    {
        get => ShapeParams.NatmCrownRadius;
        set { if (NearlyEqual(ShapeParams.NatmCrownRadius, value)) return; ShapeParams.NatmCrownRadius = value; OnPropertyChanged(); RefreshPreview(); }
    }
    public double NatmInvertRadius
    {
        get => ShapeParams.NatmInvertRadius;
        set { if (NearlyEqual(ShapeParams.NatmInvertRadius, value)) return; ShapeParams.NatmInvertRadius = value; OnPropertyChanged(); RefreshPreview(); }
    }
    public double NatmSideRadius
    {
        get => ShapeParams.NatmSideRadius;
        set { if (NearlyEqual(ShapeParams.NatmSideRadius, value)) return; ShapeParams.NatmSideRadius = value; OnPropertyChanged(); RefreshPreview(); }
    }
    public double NatmSpan
    {
        get => ShapeParams.NatmSpan;
        set { if (NearlyEqual(ShapeParams.NatmSpan, value)) return; ShapeParams.NatmSpan = value; OnPropertyChanged(); RefreshPreview(); }
    }
    public double NatmHeight
    {
        get => ShapeParams.NatmHeight;
        set { if (NearlyEqual(ShapeParams.NatmHeight, value)) return; ShapeParams.NatmHeight = value; OnPropertyChanged(); RefreshPreview(); }
    }

    // ── Domain ──────────────────────────────────────────────────────────────

    private double _domainXMin = -50, _domainXMax = +50, _domainYMin = -50, _domainYMax = +50;
    public double DomainXMin { get => _domainXMin; set { _domainXMin = value; OnPropertyChanged(); RefreshPreview(); } }
    public double DomainXMax { get => _domainXMax; set { _domainXMax = value; OnPropertyChanged(); RefreshPreview(); } }
    public double DomainYMin { get => _domainYMin; set { _domainYMin = value; OnPropertyChanged(); RefreshPreview(); } }
    public double DomainYMax { get => _domainYMax; set { _domainYMax = value; OnPropertyChanged(); RefreshPreview(); } }

    /// <summary>Recompute outer rectangle as 5× max diameter around the tunnel polygon.</summary>
    public void AutoSizeDomain(double margin = 5.0)
    {
        var poly = TunnelShapeBuilder.BuildPolygon(ShapeParams);
        var (xMin, xMax, yMin, yMax) = TunnelShapeBuilder.SuggestOuterDomain(poly, margin);
        DomainXMin = xMin; DomainXMax = xMax; DomainYMin = yMin; DomainYMax = yMax;
    }

    // ── Stress ──────────────────────────────────────────────────────────────

    private TunnelStressPreset _stressPreset = TunnelStressPreset.Hydrostatic;
    public TunnelStressPreset StressPreset
    {
        get => _stressPreset;
        set
        {
            if (_stressPreset == value) return;
            _stressPreset = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StressIsHydrostatic));
            OnPropertyChanged(nameof(StressIsK0));
            OnPropertyChanged(nameof(StressIsGravity));
            OnPropertyChanged(nameof(StressIsDirect));
        }
    }
    public bool StressIsHydrostatic => StressPreset == TunnelStressPreset.Hydrostatic;
    public bool StressIsK0          => StressPreset == TunnelStressPreset.K0Lateral;
    public bool StressIsGravity     => StressPreset == TunnelStressPreset.Gravity;
    public bool StressIsDirect      => StressPreset == TunnelStressPreset.Direct;

    private double _stressMagnitude = 1000.0;
    /// <summary>Compressive σ magnitude (positive number; sign-flipped at apply time per FieldStressSpec convention).</summary>
    public double StressMagnitude { get => _stressMagnitude; set { _stressMagnitude = value; OnPropertyChanged(); } }

    private double _stressK = 1.0;
    public double StressK { get => _stressK; set { _stressK = value; OnPropertyChanged(); } }

    private double _stressDepth = 50.0;
    public double StressDepth { get => _stressDepth; set { _stressDepth = value; OnPropertyChanged(); } }

    private double _stressGamma = 25.0;
    /// <summary>Unit weight γ (kN/m³) for K0/Gravity presets.</summary>
    public double StressGamma { get => _stressGamma; set { _stressGamma = value; OnPropertyChanged(); } }

    private double _stressAngleDeg = 0.0;
    public double StressAngleDeg { get => _stressAngleDeg; set { _stressAngleDeg = value; OnPropertyChanged(); } }

    private double _directSxx = -1000.0, _directSyy = -1000.0, _directTxy = 0.0;
    public double DirectSxx { get => _directSxx; set { _directSxx = value; OnPropertyChanged(); } }
    public double DirectSyy { get => _directSyy; set { _directSyy = value; OnPropertyChanged(); } }
    public double DirectTxy { get => _directTxy; set { _directTxy = value; OnPropertyChanged(); } }

    // ── Mesh ────────────────────────────────────────────────────────────────

    private TunnelMeshPreset _meshPreset = TunnelMeshPreset.Medium;
    public TunnelMeshPreset MeshPreset
    {
        get => _meshPreset;
        set
        {
            if (_meshPreset == value) return;
            _meshPreset = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MeshIsCoarse));
            OnPropertyChanged(nameof(MeshIsMedium));
            OnPropertyChanged(nameof(MeshIsFine));
            ApplyMeshPresetValues();
        }
    }
    public bool MeshIsCoarse => MeshPreset == TunnelMeshPreset.Coarse;
    public bool MeshIsMedium => MeshPreset == TunnelMeshPreset.Medium;
    public bool MeshIsFine   => MeshPreset == TunnelMeshPreset.Fine;

    private double _farFieldEdgeLength   = 5.0;
    private double _nearTunnelEdgeLength = 0.5;
    private int    _ringCount            = 4;
    private double _ringGrowthFactor     = 1.2;
    private double _firstRingThickness   = 0.3;
    public double FarFieldEdgeLength    { get => _farFieldEdgeLength;   set { _farFieldEdgeLength   = value; OnPropertyChanged(); } }
    public double NearTunnelEdgeLength  { get => _nearTunnelEdgeLength; set { _nearTunnelEdgeLength = value; OnPropertyChanged(); } }
    public int    RingCount             { get => _ringCount;            set { _ringCount            = value; OnPropertyChanged(); } }
    public double RingGrowthFactor      { get => _ringGrowthFactor;     set { _ringGrowthFactor     = value; OnPropertyChanged(); } }
    public double FirstRingThickness    { get => _firstRingThickness;   set { _firstRingThickness   = value; OnPropertyChanged(); } }

    // ── Live preview ────────────────────────────────────────────────────────

    private Point[] _previewTunnelAxis = Array.Empty<Point>();
    /// <summary>Current tunnel polygon in axis space (CCW, closed).</summary>
    public Point[] PreviewTunnelAxis
    {
        get => _previewTunnelAxis;
        private set { _previewTunnelAxis = value ?? Array.Empty<Point>(); OnPropertyChanged(); }
    }

    private Point[] _previewDomainAxis = Array.Empty<Point>();
    /// <summary>Outer rectangle in axis space (4 corners, closed).</summary>
    public Point[] PreviewDomainAxis
    {
        get => _previewDomainAxis;
        private set { _previewDomainAxis = value ?? Array.Empty<Point>(); OnPropertyChanged(); }
    }

    private void RefreshPreview()
    {
        try
        {
            var poly = TunnelShapeBuilder.BuildPolygon(ShapeParams);
            PreviewTunnelAxis = poly.ToArray();
        }
        catch (Exception)
        {
            PreviewTunnelAxis = Array.Empty<Point>();
        }
        PreviewDomainAxis = new[]
        {
            new Point(_domainXMin, _domainYMin),
            new Point(_domainXMax, _domainYMin),
            new Point(_domainXMax, _domainYMax),
            new Point(_domainXMin, _domainYMax),
            new Point(_domainXMin, _domainYMin),
        };
        OnPropertyChanged(nameof(ReviewSummary));
    }

    /// <summary>One-shot textual summary the Review step shows verbatim.</summary>
    public string ReviewSummary
    {
        get
        {
            var lines = new List<string>
            {
                $"Shape: {ShapeKind}",
            };
            switch (ShapeKind)
            {
                case TunnelShapeKind.Circle:
                    lines.Add($"  radius = {Radius:G6} m, centred at ({CenterX:G6}, {CenterY:G6})");
                    break;
                case TunnelShapeKind.Rectangular:
                    lines.Add($"  width × height = {Width:G6} × {Height:G6} m");
                    break;
                case TunnelShapeKind.Horseshoe:
                    lines.Add($"  span × height = {HorseshoeSpan:G6} × {HorseshoeHeight:G6} m");
                    break;
                case TunnelShapeKind.NatmArch:
                    lines.Add($"  span × height = {NatmSpan:G6} × {NatmHeight:G6} m,  R_crown={NatmCrownRadius:G6}, R_invert={NatmInvertRadius:G6}");
                    break;
            }
            lines.Add($"Outer domain: x∈[{DomainXMin:G6}, {DomainXMax:G6}],  y∈[{DomainYMin:G6}, {DomainYMax:G6}]");
            lines.Add($"Stress: {StressPreset}" + StressPreset switch
            {
                TunnelStressPreset.Hydrostatic => $",  σ = {StressMagnitude:G6} kPa (compressive)",
                TunnelStressPreset.K0Lateral   => $",  γ = {StressGamma:G6} kN/m³,  K0 = {StressK:G6},  depth = {StressDepth:G6} m",
                TunnelStressPreset.Gravity     => $",  γ = {StressGamma:G6} kN/m³  (FieldStressMode.Gravity)",
                TunnelStressPreset.Direct      => $",  σxx = {DirectSxx:G6},  σyy = {DirectSyy:G6},  τxy = {DirectTxy:G6} kPa",
                _ => string.Empty,
            });
            lines.Add($"Mesh: {MeshPreset} (h_far={FarFieldEdgeLength:G6}, h_near={NearTunnelEdgeLength:G6}, rings={RingCount}, growth={RingGrowthFactor:G6})");
            return string.Join(Environment.NewLine, lines);
        }
    }

    // ── Commands ────────────────────────────────────────────────────────────

    public ICommand NextStepCommand { get; }
    public ICommand PrevStepCommand { get; }
    public ICommand ApplyCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ApplyShapePresetCommand { get; }
    public ICommand ApplyMeshPresetCommand { get; }
    public ICommand ApplyStressPresetCommand { get; }

    private bool CanApply => CurrentStepIndex == StepReview && PreviewTunnelAxis.Length >= 4;

    /// <summary>Raised when the Apply button is clicked. Subscribers (the dialog code-behind) close after handling.</summary>
    public event EventHandler? ApplyRequested;
    /// <summary>Raised when the Cancel button is clicked.</summary>
    public event EventHandler? CancelRequested;

    private void GoNext() { if (CurrentStepIndex < StepCount - 1) CurrentStepIndex++; }
    private void GoPrev() { if (CurrentStepIndex > 0) CurrentStepIndex--; }
    private void InvokeApply() => ApplyRequested?.Invoke(this, EventArgs.Empty);
    private void InvokeCancel() => CancelRequested?.Invoke(this, EventArgs.Empty);

    private void ApplyShapePreset(string? key)
    {
        if (string.IsNullOrEmpty(key)) return;
        switch (key)
        {
            case "Circle":      ShapeKind = TunnelShapeKind.Circle;      break;
            case "Rectangular": ShapeKind = TunnelShapeKind.Rectangular; break;
            case "Horseshoe":   ShapeKind = TunnelShapeKind.Horseshoe;   break;
            case "NatmArch":    ShapeKind = TunnelShapeKind.NatmArch;    break;
        }
    }

    private void ApplyMeshPreset(string? key)
    {
        if (string.IsNullOrEmpty(key)) return;
        switch (key)
        {
            case "Coarse": MeshPreset = TunnelMeshPreset.Coarse; break;
            case "Medium": MeshPreset = TunnelMeshPreset.Medium; break;
            case "Fine":   MeshPreset = TunnelMeshPreset.Fine;   break;
        }
    }

    private void ApplyStressPreset(string? key)
    {
        if (string.IsNullOrEmpty(key)) return;
        switch (key)
        {
            case "Hydrostatic": StressPreset = TunnelStressPreset.Hydrostatic; break;
            case "K0Lateral":   StressPreset = TunnelStressPreset.K0Lateral;   break;
            case "Gravity":     StressPreset = TunnelStressPreset.Gravity;     break;
            case "Direct":      StressPreset = TunnelStressPreset.Direct;      break;
        }
    }

    private void ApplyMeshPresetValues()
    {
        // Scale near-tunnel size off the characteristic tunnel dimension so presets work for 1m and 30m tunnels alike.
        double charSize = Math.Max(EstimateCharacteristicSize(), 1.0);
        switch (MeshPreset)
        {
            case TunnelMeshPreset.Coarse:
                FarFieldEdgeLength   = charSize * 1.0;
                NearTunnelEdgeLength = charSize * 0.20;
                RingCount            = 3;
                RingGrowthFactor     = 1.4;
                FirstRingThickness   = charSize * 0.10;
                break;
            case TunnelMeshPreset.Medium:
                FarFieldEdgeLength   = charSize * 0.6;
                NearTunnelEdgeLength = charSize * 0.10;
                RingCount            = 4;
                RingGrowthFactor     = 1.2;
                FirstRingThickness   = charSize * 0.06;
                break;
            case TunnelMeshPreset.Fine:
                FarFieldEdgeLength   = charSize * 0.4;
                NearTunnelEdgeLength = charSize * 0.05;
                RingCount            = 6;
                RingGrowthFactor     = 1.15;
                FirstRingThickness   = charSize * 0.03;
                break;
        }
    }

    private double EstimateCharacteristicSize()
    {
        return ShapeKind switch
        {
            TunnelShapeKind.Circle      => Math.Max(2.0 * Radius, 1.0),
            TunnelShapeKind.Rectangular => Math.Max(Math.Max(Width, Height), 1.0),
            TunnelShapeKind.Horseshoe   => Math.Max(Math.Max(HorseshoeSpan, HorseshoeHeight), 1.0),
            TunnelShapeKind.NatmArch    => Math.Max(Math.Max(NatmSpan, NatmHeight), 1.0),
            _ => 10.0,
        };
    }

    private void ApplyDefaults()
    {
        ShapeKind = TunnelShapeKind.Circle;
        ShapeParams.Radius = 5.0;
        ShapeParams.Width = 8.0;
        ShapeParams.Height = 6.0;
        ShapeParams.HorseshoeSpan = 10.0;
        ShapeParams.HorseshoeHeight = 8.0;
        ShapeParams.NatmCrownRadius = 5.0;
        ShapeParams.NatmInvertRadius = 12.0;
        ShapeParams.NatmSideRadius = 8.0;
        ShapeParams.NatmSpan = 10.0;
        ShapeParams.NatmHeight = 7.0;
        AutoSizeDomain();
        ApplyMeshPresetValues();
    }

    /// <summary>Resets the wizard to its first step and default values (used when reopening the dialog).</summary>
    public void Reset()
    {
        CurrentStepIndex = StepShape;
        ApplyDefaults();
        RefreshPreview();
    }

    // ── INotifyPropertyChanged ──────────────────────────────────────────────

    private static bool NearlyEqual(double a, double b) => Math.Abs(a - b) < 1e-9;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
