using System;
using System.Collections.Generic;
using System.Linq;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Ensures the Groundwater block written into SRF / structural .dat is solver-active
/// when Groundwater is ON and usable data exists (phreatic, Ru, or water-pressure grid),
/// even if Project Settings Static component checkboxes are unchecked.
/// UI checkboxes are not modified — only the export clone's <see cref="GroundwaterModel.StaticSettings"/>.
/// </summary>
public static class SrfGroundwaterExport
{
    /// <summary>
    /// Apply export-only StaticSettings overrides so <c>groundwater::Spec::active()</c> can be true.
    /// </summary>
    public static void EnsureActiveExportSettings(GroundwaterModel model)
    {
        if (model is not { IsEnabled: true })
            return;

        bool hasPoreGrid = HasGridPoints(model, WaterPressureGridType.PorePressure);
        bool hasPressureHeadGrid = HasGridPoints(model, WaterPressureGridType.PressureHead);
        bool hasTotalHeadGrid = HasGridPoints(model, WaterPressureGridType.TotalHead);
        bool hasSurf = model.SurfPoints.Count > 0;
        bool hasRu = model.Method == GroundwaterMethod.Ru && model.Ru > 0;
        bool hasBbar = model.Method == GroundwaterMethod.Bbar && model.Bbar > 0;

        // Nothing to export yet — leave settings as-is (preflight will warn).
        if (!hasPoreGrid && !hasPressureHeadGrid && !hasTotalHeadGrid && !hasSurf && !hasRu && !hasBbar)
            return;

        var export = new GroundwaterStaticAnalysisSettings();
        if (model.StaticSettings != null)
            export.CopyFrom(model.StaticSettings);

        if (hasPoreGrid)
            export.IncludeWaterPressureGridPorePressure = true;
        if (hasPressureHeadGrid)
            export.IncludeWaterPressureGridPressureHead = true;
        if (hasTotalHeadGrid)
            export.IncludeWaterPressureGridTotalHead = true;
        if (hasRu || hasBbar)
            export.IncludeRuCoefficients = true;
        if (hasSurf || model.Method == GroundwaterMethod.Hydrostatic)
            export.IncludeWaterSurfaces = true;

        // Prefer grid method when a pore-pressure / head grid is present (seepage coupling).
        if (hasPoreGrid || hasPressureHeadGrid || hasTotalHeadGrid)
        {
            if ((model.Method != GroundwaterMethod.Ru || !hasRu)
                && (model.Method != GroundwaterMethod.Bbar || !hasBbar))
                model.Method = GroundwaterMethod.Grid;
        }

        model.StaticSettings = export;
    }

    /// <summary>Human-readable SRF pore-pressure export status for Overview.</summary>
    public static string DescribeExportStatus(GroundwaterModel? model)
    {
        if (model is not { IsEnabled: true })
            return "SRF pore pressure: inactive (Groundwater Off)";

        bool hasPoreGrid = HasGridPoints(model, WaterPressureGridType.PorePressure);
        bool hasPressureHeadGrid = HasGridPoints(model, WaterPressureGridType.PressureHead);
        bool hasTotalHeadGrid = HasGridPoints(model, WaterPressureGridType.TotalHead);
        bool hasSurf = model.SurfPoints.Count > 0;
        bool hasRu = model.Method == GroundwaterMethod.Ru && model.Ru > 0;
        bool hasBbar = model.Method == GroundwaterMethod.Bbar && model.Bbar > 0;

        if (hasPoreGrid)
        {
            int n = CountGridPoints(model, WaterPressureGridType.PorePressure);
            double maxU = MaxPorePressureValue(model);
            return maxU > 0
                ? $"SRF pore pressure: grid {n} pts, u_max≈{maxU:G4}"
                : $"SRF pore pressure: grid {n} pts";
        }
        if (hasPressureHeadGrid)
        {
            int n = CountGridPoints(model, WaterPressureGridType.PressureHead);
            return $"SRF pore pressure: pressure-head grid {n} pts";
        }
        if (hasTotalHeadGrid)
        {
            int n = CountGridPoints(model, WaterPressureGridType.TotalHead);
            return $"SRF pore pressure: total-head grid {n} pts";
        }
        if (hasRu)
            return $"SRF pore pressure: Ru = {model.Ru:G4}";
        if (hasBbar)
            return $"SRF pore pressure: B-bar = {model.Bbar:G4}";
        if (hasSurf)
            return $"SRF pore pressure: hydrostatic {model.SurfPoints.Count} pts";

        return "SRF pore pressure: inactive — define WT, Ru, B-bar, or run Steady seepage";
    }

    /// <summary>True when export would make <c>groundwater::Spec::active()</c> true.</summary>
    public static bool WouldBeActive(GroundwaterModel? model)
    {
        if (model is not { IsEnabled: true })
            return false;
        if (model.Method == GroundwaterMethod.Ru && model.Ru > 0)
            return true;
        if (model.Method == GroundwaterMethod.Bbar && model.Bbar > 0)
            return true;
        if (HasGridPoints(model, WaterPressureGridType.PorePressure)
            || HasGridPoints(model, WaterPressureGridType.PressureHead)
            || HasGridPoints(model, WaterPressureGridType.TotalHead))
            return true;
        return model.SurfPoints.Count > 0;
    }

    /// <summary>Max pore-pressure value on exportable grids (already in stress units).</summary>
    public static double MaxPorePressureValue(GroundwaterModel? model)
    {
        if (model is not { IsEnabled: true })
            return 0.0;

        double max = 0.0;
        void Consider(IEnumerable<(double X, double Y, double Value)>? pts)
        {
            if (pts == null) return;
            foreach (var p in pts)
                if (p.Value > max) max = p.Value;
        }

        if (model.WaterPressureGrids is { Count: > 0 })
        {
            foreach (var g in model.WaterPressureGrids)
            {
                if (g.Points == null || g.Points.Count == 0) continue;
                if (g.GridType == WaterPressureGridType.PorePressure)
                    Consider(g.Points);
                else if (g.GridType == WaterPressureGridType.PressureHead)
                {
                    foreach (var p in g.Points)
                    {
                        var u = Math.Max(0.0, model.GammaW * p.Value);
                        if (u > max) max = u;
                    }
                }
            }
        }

        Consider(model.PorePressureGridPoints);
        if (model.PressureHeadGridPoints is { Count: > 0 })
        {
            foreach (var p in model.PressureHeadGridPoints)
            {
                var u = Math.Max(0.0, model.GammaW * p.Value);
                if (u > max) max = u;
            }
        }

        return max;
    }

    /// <summary>
    /// Hint when seepage-derived pore pressures are large enough to prevent SRF bracket convergence.
    /// </summary>
    public static string? BuildHighPorePressureHint(
        GroundwaterModel? model,
        double meshMaxY,
        double? cohesion = null,
        double? unitWeight = null)
    {
        if (model is not { IsEnabled: true })
            return null;

        double maxU = MaxPorePressureValue(model);
        if (maxU <= 0.0)
            return null;

        double overburden = 0.0;
        if (unitWeight is > 0 && meshMaxY > 0)
            overburden = unitWeight.Value * meshMaxY;

        bool severe = false;
        if (cohesion is > 0 && maxU > 5.0 * cohesion.Value)
            severe = true;
        if (overburden > 0 && maxU > 0.75 * overburden)
            severe = true;

        if (!severe)
            return null;

        return
            $"Seepage pore pressure reaches u ≈ {maxU:G4} (mesh top y ≈ {meshMaxY:G4}). " +
            "That often prevents SRF trials from converging. " +
            "Lower Upstream/Downstream Water Head (H₁/H₂) so total head is near the phreatic level " +
            "(typically ≤ mesh top), or use Static + Water Surfaces instead of Coupled Steady seepage.";
    }

    private static bool HasGridPoints(GroundwaterModel model, WaterPressureGridType type)
    {
        if (model.WaterPressureGrids is { Count: > 0 }
            && model.WaterPressureGrids.Any(g => g.GridType == type && g.Points is { Count: > 0 }))
            return true;

        return type switch
        {
            WaterPressureGridType.TotalHead => model.TotalHeadGridPoints is { Count: > 0 },
            WaterPressureGridType.PressureHead => model.PressureHeadGridPoints is { Count: > 0 },
            _ => model.PorePressureGridPoints is { Count: > 0 }
        };
    }

    private static int CountGridPoints(GroundwaterModel model, WaterPressureGridType type)
    {
        if (model.WaterPressureGrids is { Count: > 0 })
        {
            var n = model.WaterPressureGrids
                .Where(g => g.GridType == type && g.Points != null)
                .Sum(g => g.Points.Count);
            if (n > 0) return n;
        }

        return type switch
        {
            WaterPressureGridType.TotalHead => model.TotalHeadGridPoints?.Count ?? 0,
            WaterPressureGridType.PressureHead => model.PressureHeadGridPoints?.Count ?? 0,
            _ => model.PorePressureGridPoints?.Count ?? 0
        };
    }
}
