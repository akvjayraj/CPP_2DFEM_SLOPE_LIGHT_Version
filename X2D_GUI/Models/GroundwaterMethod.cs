namespace GeotechSlopeApp.Models;

/// <summary>Pore-pressure definition for groundwater export and solver.</summary>
public enum GroundwaterMethod
{
    Hydrostatic,
    Ru,
    /// <summary>Water-pressure grid (total head / pressure head / pore pressure scatter).</summary>
    Grid,
    /// <summary>Skempton B̄: u = B̄ · σv (same stress-ratio form as Ru).</summary>
    Bbar
}
