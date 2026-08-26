using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace GeotechSlopeApp.Models;

/// <summary>
/// One construction / excavation stage (RS2 / Plaxis style).
/// Stores which surfaces are excavated, which supports are installed,
/// and which loads are active for this stage.
/// </summary>
public class ConstructionStage : INotifyPropertyChanged
{
    private int _stageNumber;
    private string _stageName = "";
    private string _stageType = "Initial Stress";
    private string _description = "";
    private bool _isActive = true;

    // ── Identity ─────────────────────────────────────────────────────────────
    public int StageNumber
    {
        get => _stageNumber;
        set { _stageNumber = value; OnPropertyChanged(); }
    }

    public string StageName
    {
        get => _stageName;
        set { _stageName = value?.Trim() ?? ""; OnPropertyChanged(); }
    }

    public string StageType
    {
        get => _stageType;
        set { _stageType = value; OnPropertyChanged(); }
    }

    public string Description
    {
        get => _description;
        set { _description = value; OnPropertyChanged(); }
    }

    public bool IsActive
    {
        get => _isActive;
        set { _isActive = value; OnPropertyChanged(); }
    }

    // ── Excavation ───────────────────────────────────────────────────────────
    /// <summary>
    /// Surface numbers (from GeometrySurface.SurfaceNumber) that are excavated
    /// specifically in THIS stage (not inherited from earlier stages).
    /// </summary>
    public HashSet<int> ExcavatedSurfaceNumbers { get; } = new();

    /// <summary>
    /// Excavated zone records with polygon data for canvas rendering and solver.
    /// One entry per excavation action committed in this stage.
    /// </summary>
    public ObservableCollection<StageExcavationZone> ExcavationZones { get; } = new();

    /// <summary>
    /// Authoritative list of mesh element IDs excavated in this stage when the .dat itself
    /// supplies them (Layers block on import) or when CommitExcavation derives them from a
    /// polygon. Polygon-derived IDs UNION with these on emit so byte-for-byte round-trip is
    /// preserved when the user loads + saves without editing. Empty = polygon-only path.
    /// </summary>
    public List<int> ExcavatedElementIds { get; } = new();

    /// <summary>
    /// Transient cache: element IDs derived from <see cref="ExcavatedSurfaceNumbers"/> (boolean subtract).
    /// Recomputed on every mesh generation; not persisted.
    /// </summary>
    public List<int> SurfaceDerivedElementIds { get; } = new();

    // ── Supports ─────────────────────────────────────────────────────────────
    /// <summary>Surface/element IDs where support (bolts, liners, struts) is installed in this stage.</summary>
    public HashSet<int> InstalledSupportSurfaceIds { get; } = new();

    // ── Loads ────────────────────────────────────────────────────────────────
    /// <summary>Load IDs active in this stage.</summary>
    public HashSet<int> ActiveLoadIds { get; } = new();

    // ── FE results (filled after solve) ──────────────────────────────────────
    /// <summary>Nodal displacements from the solver for this stage [nodeIndex → (ux, uy)].</summary>
    public Dictionary<int, (double Ux, double Uy)> NodalDisplacements { get; } = new();

    /// <summary>Element stresses from the solver for this stage [elementId → (σx, σy, τxy)].</summary>
    public Dictionary<int, (double Sx, double Sy, double Txy)> ElementStresses { get; } = new();

    /// <summary>True when this stage has been solved.</summary>
    public bool IsSolved { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// One excavation action within a stage: records the surface and its canvas polygon
/// so the canvas can render it as a solid white void.
/// </summary>
public class StageExcavationZone : INotifyPropertyChanged
{
    /// <summary>Surface number that was excavated.</summary>
    public int SurfaceNumber { get; set; }

    /// <summary>Stage number in which this excavation was applied.</summary>
    public int AppliedInStageNumber { get; set; }

    /// <summary>Canvas-space polygon points used for white-fill rendering.</summary>
    public PointCollection CanvasPolygon { get; set; } = new();

    /// <summary>Axis-space polygon points used for the FE solver boundary.</summary>
    public List<(double X, double Y)> AxisPolygon { get; set; } = new();

    /// <summary>Centroid in canvas space (used to place the stage label).</summary>
    public double CentroidX { get; set; }
    public double CentroidY { get; set; }

    /// <summary>Human-readable display name (e.g. "Exc S2 – Stage 1").</summary>
    public string DisplayLabel { get; set; } = "";

    /// <summary>
    /// Mesh element IDs derived from <see cref="AxisPolygon"/> for the current mesh.
    /// Transient render/sync cache; recomputed on every mesh generation.
    /// </summary>
    public List<int> DerivedElementIds { get; set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
