using System;

namespace GeotechSlopeApp.Models;

/// <summary>
/// Display labels for Project Settings → General → Unit System.
/// Values themselves are not converted — only the UI unit strings.
/// </summary>
public static class ProjectUnitSystem
{
    public const string Si = "SI";
    public const string Imperial = "Imperial";

    public static bool IsImperial(string? units) =>
        string.Equals(units, Imperial, StringComparison.OrdinalIgnoreCase);

    public static string LengthLabel(string? units) => IsImperial(units) ? "ft" : "m";

    public static string UnitWeightLabel(string? units) => IsImperial(units) ? "pcf" : "kN/m³";

    public static string StressLabel(string? units) => IsImperial(units) ? "psf" : "kPa";
}
