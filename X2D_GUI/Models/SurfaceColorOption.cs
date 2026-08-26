namespace GeotechSlopeApp.Models;

/// <summary>Dropdown entry for assigning a surface overlay color (palette index or auto by surface number).</summary>
public sealed class SurfaceColorOption
{
    /// <summary>-1 means use surface number modulo palette length.</summary>
    public int Index { get; init; }
    public string DisplayName { get; init; } = "";
}
