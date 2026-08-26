using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GeotechSlopeApp.Models;

/// <summary>Flow-Seepage/Groundwater Static analysis component toggles (Project Settings subtab).</summary>
public class GroundwaterStaticAnalysisSettings : INotifyPropertyChanged
{
    private bool _includeWaterSurfaces;
    private bool _includePondedFreeWater;
    private bool _includeRuCoefficients;
    private bool _includeWaterPressureGridTotalHead;
    private bool _includeWaterPressureGridPressureHead;
    private bool _includeWaterPressureGridPorePressure;

    public bool IncludeWaterSurfaces
    {
        get => _includeWaterSurfaces;
        set { if (_includeWaterSurfaces == value) return; _includeWaterSurfaces = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// When true (and Water Surfaces on), hydrostatic free water above the ground is applied
    /// as ponded face pressures (RS2 Ponded Water Load / Auto Assign).
    /// Shown in the UI as a sub-checkbox under Water Surfaces.
    /// </summary>
    public bool IncludePondedFreeWater
    {
        get => _includePondedFreeWater;
        set { if (_includePondedFreeWater == value) return; _includePondedFreeWater = value; OnPropertyChanged(); }
    }

    public bool IncludeRuCoefficients
    {
        get => _includeRuCoefficients;
        set { if (_includeRuCoefficients == value) return; _includeRuCoefficients = value; OnPropertyChanged(); }
    }

    public bool IncludeWaterPressureGridTotalHead
    {
        get => _includeWaterPressureGridTotalHead;
        set { if (_includeWaterPressureGridTotalHead == value) return; _includeWaterPressureGridTotalHead = value; OnPropertyChanged(); }
    }

    public bool IncludeWaterPressureGridPressureHead
    {
        get => _includeWaterPressureGridPressureHead;
        set { if (_includeWaterPressureGridPressureHead == value) return; _includeWaterPressureGridPressureHead = value; OnPropertyChanged(); }
    }

    public bool IncludeWaterPressureGridPorePressure
    {
        get => _includeWaterPressureGridPorePressure;
        set { if (_includeWaterPressureGridPorePressure == value) return; _includeWaterPressureGridPorePressure = value; OnPropertyChanged(); }
    }

    public void ApplyDefaults()
    {
        IncludeWaterSurfaces = false;
        IncludePondedFreeWater = false;
        IncludeRuCoefficients = false;
        IncludeWaterPressureGridTotalHead = false;
        IncludeWaterPressureGridPressureHead = false;
        IncludeWaterPressureGridPorePressure = false;
    }

    public void CopyFrom(GroundwaterStaticAnalysisSettings other)
    {
        IncludeWaterSurfaces = other.IncludeWaterSurfaces;
        IncludePondedFreeWater = other.IncludePondedFreeWater;
        IncludeRuCoefficients = other.IncludeRuCoefficients;
        IncludeWaterPressureGridTotalHead = other.IncludeWaterPressureGridTotalHead;
        IncludeWaterPressureGridPressureHead = other.IncludeWaterPressureGridPressureHead;
        IncludeWaterPressureGridPorePressure = other.IncludeWaterPressureGridPorePressure;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
