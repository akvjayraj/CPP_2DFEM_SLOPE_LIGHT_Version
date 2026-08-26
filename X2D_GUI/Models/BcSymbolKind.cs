namespace GeotechSlopeApp.Models;

/// <summary>RS2-style BC glyph kinds for DataGrid Symbol column and canvas markers.</summary>
public enum BcSymbolKind
{
    Fixed,
    RollerX,
    RollerY,
    Prescribed,
    Force,
    TotalHead,
    ZeroPressure,
    FlowQ,
    Infiltration,
    Unknown,
    None,
    PressureHead,
    PorePressure,
    Constant,
}
