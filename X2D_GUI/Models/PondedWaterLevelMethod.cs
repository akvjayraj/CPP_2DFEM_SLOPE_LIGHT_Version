namespace GeotechSlopeApp.Models;

/// <summary>
/// How the free-water surface elevation is defined for ponded face pressures
/// (RS2 Add Ponded Water Load water-level methods — subset for v1).
/// </summary>
public enum PondedWaterLevelMethod
{
    /// <summary>Use the Groundwater phreatic / piezometric surf polyline.</summary>
    PhreaticLine = 0,

    /// <summary>Constant total head = axis Y of the free-water surface (RS2 Total Head).</summary>
    TotalHead = 1
}
