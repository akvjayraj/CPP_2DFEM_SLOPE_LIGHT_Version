using System.Collections.Generic;

namespace GeotechSlopeApp.Models;

/// <summary>Groundwater analysis inputs for .dat export and solver.</summary>
public class GroundwaterModel
{
    public bool IsEnabled { get; set; }
    public double GammaW { get; set; } = 9.81;
    public GroundwaterMethod Method { get; set; } = GroundwaterMethod.Hydrostatic;
    public double Ru { get; set; }
    /// <summary>Skempton B̄ coefficient when <see cref="Method"/> is <see cref="GroundwaterMethod.Bbar"/>.</summary>
    public double Bbar { get; set; }
    public GroundwaterDrawMode DrawMode { get; set; } = GroundwaterDrawMode.Horizontal;
    /// <summary>Canvas-only display flag; does not affect solver inputs.</summary>
    public bool ShowSaturatedMaterialZone { get; set; } = true;
    /// <summary>Canvas-only display flag; does not affect solver inputs.</summary>
    public bool ShowUnsaturatedMaterialZone { get; set; }
    /// <summary>Canvas-only: show exterior ponded free-water depth field.</summary>
    public bool ShowPondedFreeWaterRegion { get; set; } = true;
    /// <summary>Canvas-only fill style for the ponded free-water region.</summary>
    public PondedWaterDisplayPattern PondedWaterDisplayPattern { get; set; } = PondedWaterDisplayPattern.DepthGradient;
    /// <summary>Canvas-only: show auto-assigned DistributedEdgeLoads as face-pressure arrows.</summary>
    public bool ShowPondedFacePressureArrows { get; set; } = true;
    /// <summary>Canvas-only: show hydrostatic head isobars in the ponded region.</summary>
    public bool ShowPondedHeadIsobars { get; set; } = true;
    /// <summary>RS2-style water level source for ponded face pressures.</summary>
    public PondedWaterLevelMethod PondedWaterLevelMethod { get; set; } = PondedWaterLevelMethod.PhreaticLine;
    /// <summary>Constant free-water surface Y when <see cref="PondedWaterLevelMethod.TotalHead"/>.</summary>
    public double PondedTotalHead { get; set; }
    public List<(double X, double Y)> SurfPoints { get; set; } = new();
    /// <summary>Named water-pressure grids (preferred). Flat lists below are legacy / aggregated.</summary>
    public List<WaterPressureGridData> WaterPressureGrids { get; set; } = new();
    public List<(double X, double Y, double Value)> TotalHeadGridPoints { get; set; } = new();
    public List<(double X, double Y, double Value)> PressureHeadGridPoints { get; set; } = new();
    public List<(double X, double Y, double Value)> PorePressureGridPoints { get; set; } = new();
    /// <summary>Per-material saturated unit weights in material-list order (matches .dat etype). Empty = dry γ only.</summary>
    public List<double> GammaSat { get; set; } = new();
    public GroundwaterStaticAnalysisSettings? StaticSettings { get; set; }
}
