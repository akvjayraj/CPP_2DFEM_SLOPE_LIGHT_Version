using System;
using System.Windows.Media;

namespace GeotechSlopeApp.Models;

/// <summary>
/// Maps BC Type strings to RS2-style symbol kinds and family colors.
/// </summary>
public static class BcSymbolCatalog
{
    public static BcSymbolKind ResolveKind(string? bcType)
    {
        if (string.IsNullOrWhiteSpace(bcType))
            return BcSymbolKind.Fixed;

        if (string.Equals(bcType, "Fixed", StringComparison.OrdinalIgnoreCase))
            return BcSymbolKind.Fixed;
        if (string.Equals(bcType, "Roller X", StringComparison.OrdinalIgnoreCase))
            return BcSymbolKind.RollerX;
        if (string.Equals(bcType, "Roller Y", StringComparison.OrdinalIgnoreCase))
            return BcSymbolKind.RollerY;
        if (string.Equals(bcType, "Prescribed Displacement", StringComparison.OrdinalIgnoreCase))
            return BcSymbolKind.Prescribed;
        if (string.Equals(bcType, "Force", StringComparison.OrdinalIgnoreCase))
            return BcSymbolKind.Force;

        if (string.Equals(bcType, "Total Head (H)", StringComparison.OrdinalIgnoreCase))
            return BcSymbolKind.TotalHead;
        if (string.Equals(bcType, "Zero Pressure (P=0)", StringComparison.OrdinalIgnoreCase))
            return BcSymbolKind.ZeroPressure;
        if (string.Equals(bcType, "Nodal Flow Rate (Q)", StringComparison.OrdinalIgnoreCase))
            return BcSymbolKind.FlowQ;
        if (string.Equals(bcType, "Normal Infiltration (q)", StringComparison.OrdinalIgnoreCase)
            || string.Equals(bcType, "Vertical Infiltration (q)", StringComparison.OrdinalIgnoreCase))
            return BcSymbolKind.Infiltration;
        if (string.Equals(bcType, "Unknown (P=0 or Q=0)", StringComparison.OrdinalIgnoreCase))
            return BcSymbolKind.Unknown;
        if (string.Equals(bcType, "None / Remove BC", StringComparison.OrdinalIgnoreCase))
            return BcSymbolKind.None;
        if (string.Equals(bcType, "Pressure Head", StringComparison.OrdinalIgnoreCase))
            return BcSymbolKind.PressureHead;
        if (string.Equals(bcType, "Pore Pressure", StringComparison.OrdinalIgnoreCase))
            return BcSymbolKind.PorePressure;
        if (string.Equals(bcType, "Constant", StringComparison.OrdinalIgnoreCase))
            return BcSymbolKind.Constant;

        return BoundaryConditionAnalysisCategories.IsSeepageBcType(bcType)
            ? BcSymbolKind.TotalHead
            : BcSymbolKind.Fixed;
    }

    public static bool IsSeepageKind(BcSymbolKind kind) =>
        kind is BcSymbolKind.TotalHead
            or BcSymbolKind.ZeroPressure
            or BcSymbolKind.FlowQ
            or BcSymbolKind.Infiltration
            or BcSymbolKind.Unknown
            or BcSymbolKind.None
            or BcSymbolKind.PressureHead
            or BcSymbolKind.PorePressure
            or BcSymbolKind.Constant;

    public static string ShortLabel(BcSymbolKind kind) => kind switch
    {
        BcSymbolKind.Fixed => "Fixed",
        BcSymbolKind.RollerX => "Roller X",
        BcSymbolKind.RollerY => "Roller Y",
        BcSymbolKind.Prescribed => "Prescribed",
        BcSymbolKind.Force => "Force",
        BcSymbolKind.TotalHead => "Total Head (H)",
        BcSymbolKind.ZeroPressure => "Zero Pressure (P=0)",
        BcSymbolKind.FlowQ => "Nodal Flow (Q)",
        BcSymbolKind.Infiltration => "Infiltration (q)",
        BcSymbolKind.Unknown => "Unknown",
        BcSymbolKind.None => "None",
        BcSymbolKind.PressureHead => "Pressure Head",
        BcSymbolKind.PorePressure => "Pore Pressure",
        BcSymbolKind.Constant => "Constant",
        _ => kind.ToString(),
    };

    public static string AnalysisFamily(BcSymbolKind kind) =>
        IsSeepageKind(kind)
            ? BoundaryConditionAnalysisCategories.Seepage
            : BoundaryConditionAnalysisCategories.Structural;

    /// <summary>Primary fill / badge color for the glyph.</summary>
    public static Color GetFillColor(BcSymbolKind kind) => kind switch
    {
        BcSymbolKind.Fixed => Color.FromRgb(0xDC, 0x26, 0x26),
        BcSymbolKind.RollerX or BcSymbolKind.RollerY => Color.FromRgb(0x25, 0x63, 0xEB),
        BcSymbolKind.Prescribed => Color.FromRgb(0x05, 0x96, 0x69),
        BcSymbolKind.Force => Color.FromRgb(0xEA, 0x58, 0x0C),
        BcSymbolKind.TotalHead => Color.FromRgb(0x02, 0x84, 0xC7),
        BcSymbolKind.PressureHead => Color.FromRgb(0x03, 0x69, 0xA1),
        BcSymbolKind.PorePressure => Color.FromRgb(0x0E, 0x74, 0x90),
        BcSymbolKind.Constant => Color.FromRgb(0x15, 0x5E, 0x75),
        BcSymbolKind.ZeroPressure => Color.FromRgb(0x08, 0x91, 0xB2),
        BcSymbolKind.FlowQ => Color.FromRgb(0x7C, 0x3A, 0xED),
        BcSymbolKind.Infiltration => Color.FromRgb(0x4F, 0x46, 0xE5),
        BcSymbolKind.Unknown => Color.FromRgb(0x64, 0x74, 0x8B),
        BcSymbolKind.None => Color.FromRgb(0x94, 0xA3, 0xB8),
        _ => Color.FromRgb(0x64, 0x74, 0x8B),
    };

    /// <summary>Darker stroke / outline for contrast (halo-friendly).</summary>
    public static Color GetStrokeColor(BcSymbolKind kind)
    {
        var fill = GetFillColor(kind);
        return Color.FromRgb(
            (byte)(fill.R * 0.65),
            (byte)(fill.G * 0.65),
            (byte)(fill.B * 0.65));
    }

    public static SolidColorBrush GetFillBrush(BcSymbolKind kind)
    {
        var brush = new SolidColorBrush(GetFillColor(kind));
        brush.Freeze();
        return brush;
    }

    public static SolidColorBrush GetStrokeBrush(BcSymbolKind kind)
    {
        var brush = new SolidColorBrush(GetStrokeColor(kind));
        brush.Freeze();
        return brush;
    }

    /// <summary>True when the DOF column should offer an X/Y chooser (Prescribed / Force).</summary>
    public static bool UsesEditableDof(string? bcType)
    {
        var kind = ResolveKind(bcType);
        return kind is BcSymbolKind.Prescribed or BcSymbolKind.Force;
    }

    /// <summary>True when DOF is meaningful (structural restraint / load), not seepage.</summary>
    public static bool UsesDegreeOfFreedom(string? bcType)
        => !BoundaryConditionAnalysisCategories.IsSeepageBcType(bcType)
           && ResolveKind(bcType) != BcSymbolKind.None;

    public static string FormatDofDisplay(string? bcType, int degreeOfFreedom)
    {
        var kind = ResolveKind(bcType);
        return kind switch
        {
            BcSymbolKind.Fixed => "Both",
            BcSymbolKind.RollerX => "0 (X)",
            BcSymbolKind.RollerY => "1 (Y)",
            BcSymbolKind.Prescribed or BcSymbolKind.Force =>
                degreeOfFreedom == 1 ? "1 (Y)" : "0 (X)",
            _ => "—",
        };
    }

    /// <summary>Suggested DOF when the BC type implies a fixed axis.</summary>
    public static int? ImpliedDegreeOfFreedom(string? bcType)
    {
        return ResolveKind(bcType) switch
        {
            BcSymbolKind.RollerX => 0,
            BcSymbolKind.RollerY => 1,
            BcSymbolKind.Fixed => 0,
            _ => null,
        };
    }

    public static int ResolveApplyDegreeOfFreedom(string? bcType, int editorDof)
        => ImpliedDegreeOfFreedom(bcType) ?? editorDof;

    public static bool ShowsRestraintFlags(string? bcType)
    {
        var kind = ResolveKind(bcType);
        return kind is BcSymbolKind.Fixed or BcSymbolKind.RollerX or BcSymbolKind.RollerY;
    }

    public static bool ShowsDof(string? bcType) => UsesEditableDof(bcType);

    public static bool ShowsValue(string? bcType)
    {
        var kind = ResolveKind(bcType);
        return kind is not BcSymbolKind.Unknown and not BcSymbolKind.None;
    }

    public static bool ValueIsReadOnly(string? bcType)
        => ResolveKind(bcType) == BcSymbolKind.ZeroPressure;

    public static string DefaultValue(string? bcType)
        => "0";

    public static string DefaultUx(string? bcType) => ResolveKind(bcType) switch
    {
        BcSymbolKind.Fixed or BcSymbolKind.RollerX => "1",
        BcSymbolKind.RollerY => "0",
        _ => "0",
    };

    public static string DefaultUy(string? bcType) => ResolveKind(bcType) switch
    {
        BcSymbolKind.Fixed or BcSymbolKind.RollerY => "1",
        BcSymbolKind.RollerX => "0",
        _ => "0",
    };

    public static string ValueFieldLabel(string? bcType) => ResolveKind(bcType) switch
    {
        BcSymbolKind.TotalHead or BcSymbolKind.PressureHead => "Head",
        BcSymbolKind.PorePressure => "u",
        BcSymbolKind.FlowQ => "Q",
        BcSymbolKind.Infiltration => "q",
        _ => "Value",
    };
}
