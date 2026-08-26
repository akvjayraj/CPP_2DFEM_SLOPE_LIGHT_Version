namespace GeotechSlopeApp.Models;

/// <summary>Static water-pressure grid scalar type (Project Settings → Static checkboxes).</summary>
public enum WaterPressureGridType
{
    TotalHead,
    PressureHead,
    PorePressure
}

public static class WaterPressureGridTypeExtensions
{
    public static string GetDisplayName(this WaterPressureGridType type) => type switch
    {
        WaterPressureGridType.TotalHead => "Total Head",
        WaterPressureGridType.PressureHead => "Pressure Head",
        WaterPressureGridType.PorePressure => "Pore Pressure",
        _ => type.ToString()
    };

    public static string GetValueColumnHeader(this WaterPressureGridType type) => type switch
    {
        WaterPressureGridType.TotalHead => "Total Head (m)",
        WaterPressureGridType.PressureHead => "Pressure Head (m)",
        WaterPressureGridType.PorePressure => "Pore Pressure (kPa)",
        _ => "Value"
    };

    public static string ToPersistenceString(this WaterPressureGridType type) => type switch
    {
        WaterPressureGridType.TotalHead => "TotalHead",
        WaterPressureGridType.PressureHead => "PressureHead",
        WaterPressureGridType.PorePressure => "PorePressure",
        _ => type.ToString()
    };

    public static bool TryParse(string? value, out WaterPressureGridType type)
    {
        if (string.Equals(value, "TotalHead", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Total Head", StringComparison.OrdinalIgnoreCase))
        {
            type = WaterPressureGridType.TotalHead;
            return true;
        }

        if (string.Equals(value, "PressureHead", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Pressure Head", StringComparison.OrdinalIgnoreCase))
        {
            type = WaterPressureGridType.PressureHead;
            return true;
        }

        if (string.Equals(value, "PorePressure", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Pore Pressure", StringComparison.OrdinalIgnoreCase))
        {
            type = WaterPressureGridType.PorePressure;
            return true;
        }

        type = default;
        return false;
    }
}
