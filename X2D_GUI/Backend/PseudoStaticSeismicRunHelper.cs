using System;
using System.Globalization;
using System.Linq;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>Resolves per-stage pseudo-static k_h / k_v for script patching and solver runs.</summary>
public static class PseudoStaticSeismicRunHelper
{
    /// <summary>Footer active stage when it is Seismic / Dynamic — solver run default.</summary>
    public static (double Kh, double Kv) ResolveCoefficientsForActiveStage(
        FEAnalysisViewModel? fe,
        StagedConstructionViewModel? staged)
        => ResolveCoefficientsForStage(fe, staged?.ActiveStageNumber ?? 0);

    /// <summary>
    /// Coefficients shown in Script Editor — matches Load Conditions Seismic / Inertia Load
    /// (<see cref="LoadConditionsViewModel.SeismicEditTarget"/>).
    /// </summary>
    public static (double Kh, double Kv) ResolveCoefficientsForScriptEditor(
        FEAnalysisViewModel? fe,
        LoadConditionsViewModel? loads)
    {
        if (loads?.SeismicEditTarget is { } target)
            return (target.PseudoStaticKh, target.PseudoStaticKv);

        return (0.0, 0.0);
    }

    /// <summary>Per-stage k_h/k_v when the stage row is Seismic / Dynamic; otherwise (0, 0).</summary>
    public static (double Kh, double Kv) ResolveCoefficientsForStage(FEAnalysisViewModel? fe, int stageNumber)
    {
        if (fe is not { StagedExcavationEnabled: true })
            return (0.0, 0.0);

        if (stageNumber <= 0)
            return (0.0, 0.0);

        var def = fe.StageDefinitions.FirstOrDefault(s => s.StageNumber == stageNumber);
        if (def is { IsSeismicDynamicStage: true })
            return (def.PseudoStaticKh, def.PseudoStaticKv);

        return (0.0, 0.0);
    }

    /// <summary>
    /// Script → Project Settings: when .dat has no <c>! k_h</c> / <c>! k_v</c>, clear pseudo-static
    /// coefficients on Seismic / Dynamic stage rows only. Does not disable staged analysis
    /// (construction/excavation/SRF stages are independent of script pseudo-static lines).
    /// </summary>
    public static void ClearSeismicStageFromScriptImport(FEAnalysisViewModel fe, out bool valuesChanged)
    {
        valuesChanged = false;
        foreach (var stage in fe.StageDefinitions.Where(s => s.IsSeismicDynamicStage))
        {
            if (Math.Abs(stage.PseudoStaticKh) <= 1e-12 && Math.Abs(stage.PseudoStaticKv) <= 1e-12)
                continue;
            stage.PseudoStaticKh = 0.0;
            stage.PseudoStaticKv = 0.0;
            valuesChanged = true;
        }
    }

    /// <summary>
    /// Script → Project Settings: when .dat declares <c>! k_h</c> / <c>! k_v</c>, turn on staged analysis
    /// and ensure exactly one Seismic / Dynamic row receives the coefficients.
    /// </summary>
    public static StageDefinition EnsureSeismicStageFromScriptImport(
        FEAnalysisViewModel fe,
        double k_h,
        double k_v,
        out bool valuesChanged)
    {
        valuesChanged = false;

        if (!fe.StagedExcavationEnabled)
        {
            fe.StagedExcavationEnabled = true;
            valuesChanged = true;
        }

        var seismic = fe.StageDefinitions.FirstOrDefault(s => s.IsSeismicDynamicStage);
        if (seismic == null)
        {
            var next = fe.StageDefinitions.Count == 0
                ? 1
                : fe.StageDefinitions.Max(s => s.StageNumber) + 1;
            seismic = new StageDefinition
            {
                StageNumber = next,
                StageName = "Pseudo-static seismic",
                StageType = "Seismic / Dynamic",
                IsActive = true,
            };
            fe.StageDefinitions.Add(seismic);
            fe.SelectedStageDefinition = seismic;
            valuesChanged = true;
        }

        if (Math.Abs(seismic.PseudoStaticKh - k_h) > 1e-12 || Math.Abs(seismic.PseudoStaticKv - k_v) > 1e-12)
        {
            seismic.PseudoStaticKh = k_h;
            seismic.PseudoStaticKv = k_v;
            valuesChanged = true;
        }

        return seismic;
    }

    /// <summary>Load Conditions edit target, else footer active Seismic / Dynamic stage, else script .dat holder.</summary>
    public static StageDefinition? ResolveImportTargetStage(
        FEAnalysisViewModel? fe,
        StagedConstructionViewModel? staged,
        LoadConditionsViewModel? loads = null)
    {
        if (fe is { StagedExcavationEnabled: true })
        {
            if (loads is { IsSeismicLoadUiEnabled: true, SeismicEditTarget: { IsSeismicDynamicStage: true } editTarget }
                && fe.StageDefinitions.Contains(editTarget))
                return editTarget;

            int stageNum = staged?.ActiveStageNumber ?? 0;
            if (stageNum > 0)
            {
                var def = fe.StageDefinitions.FirstOrDefault(s => s.StageNumber == stageNum);
                if (def is { IsSeismicDynamicStage: true })
                    return def;
            }
        }

        return null;
    }

    public static string FormatCoefficient(double v) =>
        v.ToString("G6", CultureInfo.InvariantCulture);
}
