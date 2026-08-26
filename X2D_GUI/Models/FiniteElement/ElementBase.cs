namespace GeotechSlopeApp.Models.FiniteElement;

/// <summary>
/// Base class for FEM elements. NodeIds are 0-based indices into the nodes list.
/// </summary>
public abstract class ElementBase
{
    public int Id { get; set; }
    public int[] NodeIds { get; set; } = Array.Empty<int>();

    /// <summary>
    /// False when the solver has excavated this element (staged tunnel cut) — its
    /// stiffness contribution has been removed and its stored stress wiped.
    /// Populated from the <c>"active"</c> field on each element in the p610_t3 JSON
    /// output (1 = solid, 0 = void). Renderers must skip inactive elements (contour
    /// fill, stress vectors) and draw them as a dashed void outline on the mesh.
    /// Defaults to true so legacy (non-excavation) meshes render exactly as before.
    /// </summary>
    public bool IsActive { get; set; } = true;
}
