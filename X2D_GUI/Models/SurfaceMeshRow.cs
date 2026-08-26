using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GeotechSlopeApp.Models;

/// <summary>Coarse-to-fine local refinement level for a single surface region.</summary>
public enum MeshRefinementLevel { Default, Coarse, Medium, Fine, VeryFine }

/// <summary>
/// One row in the Surface Mesh subtab's surface list.
/// Each row represents one closed surface region and controls its mesh size and refinement.
/// </summary>
public class SurfaceMeshRow : INotifyPropertyChanged
{
    private string _meshSizeOverride = "";
    private bool _isExcluded;
    private MeshRefinementLevel _refinement = MeshRefinementLevel.Default;

    public int SurfaceNumber { get; set; }
    public string SurfaceName { get; set; } = "";
    public string AssignedMaterialName { get; set; } = "—";

    /// <summary>True when this surface is excavated in the active construction stage.</summary>
    public bool IsExcavated { get; set; }

    /// <summary>True when the surface has a material assigned in the geometry Surface grid.</summary>
    public bool HasAssignedMaterial { get; set; }

    /// <summary>
    /// Staged excavation with no material: treated as an open void (hole) in the continuum mesh.
    /// Excavated surfaces that still have a material (e.g. rock in a cavern) are meshed as solid regions.
    /// </summary>
    public bool ExcavatedAsOpenVoid => IsExcavated && !HasAssignedMaterial;

    /// <summary>User can manually exclude a surface from meshing (even when not excavated).</summary>
    public bool IsExcluded
    {
        get => _isExcluded;
        set { _isExcluded = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(WillBeMeshed)); }
    }

    /// <summary>
    /// Override element target size for this surface. Empty string = use global default.
    /// Supports numeric values (e.g. "0.3") or empty for default.
    /// </summary>
    public string MeshSizeOverride
    {
        get => _meshSizeOverride;
        set { _meshSizeOverride = value; OnPropertyChanged(); }
    }

    /// <summary>Local refinement preset for this surface.</summary>
    public MeshRefinementLevel Refinement
    {
        get => _refinement;
        set { _refinement = value; OnPropertyChanged(); }
    }

    public bool WillBeMeshed => !IsExcluded && (!IsExcavated || HasAssignedMaterial);

    public string StatusText =>
        IsExcluded ? "Excluded" :
        ExcavatedAsOpenVoid ? "Void (no mesh)" :
        IsExcavated ? "Staged - meshed (material)" :
                      "Active";

    public string StatusColor =>
        IsExcluded ? "#FF6B7280" :
        ExcavatedAsOpenVoid ? "#FFDC2626" :
        IsExcavated ? "#FFD97706" :
                      "#FF16A34A";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
