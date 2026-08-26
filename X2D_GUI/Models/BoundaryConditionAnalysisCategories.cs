using System;

namespace GeotechSlopeApp.Models;

/// <summary>
/// Maps BC Type strings to Project Settings → Analysis heading families
/// (Structural vs Flow-Seepage/Groundwater) for the BC DataGrid.
/// </summary>
public static class BoundaryConditionAnalysisCategories
{
    public const string Structural = "Structural";
    public const string Seepage = "Flow-Seepage/Groundwater";

    private static readonly string[] StructuralBcTypes =
    {
        "Fixed", "Roller X", "Roller Y", "Prescribed Displacement", "Force"
    };

    private static readonly string[] SeepageBcTypes =
    {
        "Total Head (H)",
        "Zero Pressure (P=0)",
        "Nodal Flow Rate (Q)",
        "Normal Infiltration (q)",
        "Vertical Infiltration (q)",
        "Unknown (P=0 or Q=0)",
        "None / Remove BC",
        "Pressure Head",
        "Pore Pressure",
        "Constant"
    };

    public static bool IsSeepageBcType(string? bcType)
    {
        if (string.IsNullOrWhiteSpace(bcType)) return false;
        foreach (var t in SeepageBcTypes)
        {
            if (string.Equals(t, bcType, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    public static bool IsStructuralBcType(string? bcType)
    {
        if (string.IsNullOrWhiteSpace(bcType)) return false;
        foreach (var t in StructuralBcTypes)
        {
            if (string.Equals(t, bcType, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Resolve analysis heading for a BC type. Unknown / empty → Structural.
    /// </summary>
    public static string Resolve(string? bcType)
    {
        if (IsSeepageBcType(bcType))
            return Seepage;
        return Structural;
    }
}
