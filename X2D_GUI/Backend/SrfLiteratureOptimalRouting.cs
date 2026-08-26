using System;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Selects the literature-validated SRF solver route per mesh type: branded
/// <c>slope_stability_{Mesh}_SRF</c> metadata and the Core A/B process to run.
/// Axisymmetric mechanical SRF appends <c>_axisym</c> to the branded executable.
/// </summary>
public static class SrfLiteratureOptimalRouting
{
    public enum SolverCore
    {
        CoreA,
        CoreB,
    }

    public enum DeckFormat
    {
        P610,
        P64Explicit,
    }

    /// <param name="Executable">Branded name written to AnalysisOptions (<c>slope_stability_{Mesh}_SRF[_axisym]</c>).</param>
    /// <param name="ProcessExecutable">Native binary to launch (same as branded for T3/T6/Q4/Q8).</param>
    public sealed record Route(
        SolverCore Core,
        DeckFormat Deck,
        string Executable,
        string ProcessExecutable)
    {
        public Route(SolverCore core, DeckFormat deck, string executable)
            : this(core, deck, executable, executable)
        {
        }
    }

    /// <summary>
    /// Branded executable + deck format chosen to minimize FoS deviation from Griffiths &amp; Lane benchmarks.
    /// T3/T6 → Core A + p610 deck; Q4/Q8 → Core B + explicit p64-layout deck.
    /// </summary>
    public static Route Resolve(
        string? meshType,
        FailureCriterion failureCriterion = FailureCriterion.MohrCoulomb,
        bool axisymmetric = false)
    {
        var mesh = P610AnalysisOptionsHelper.NormalizeMeshTypeToken(meshType);
        var branded = P610AnalysisOptionsHelper.FormatSlopeSrfExecutableName(mesh, failureCriterion, axisymmetric);
        return mesh switch
        {
            "Q4" => new Route(SolverCore.CoreB, DeckFormat.P64Explicit, branded),
            "Q8" => new Route(SolverCore.CoreB, DeckFormat.P64Explicit, branded),
            _ => new Route(SolverCore.CoreA, DeckFormat.P610, branded),
        };
    }

    public static Route Resolve(
        MeshViewModel? meshVm,
        FailureCriterion failureCriterion = FailureCriterion.MohrCoulomb,
        bool axisymmetric = false)
        => Resolve(P610AnalysisOptionsHelper.ResolveMeshType(meshVm), failureCriterion, axisymmetric);

    public static string ResolveBrandedExecutable(
        string? meshType,
        FailureCriterion failureCriterion = FailureCriterion.MohrCoulomb,
        bool axisymmetric = false)
        => Resolve(meshType, failureCriterion, axisymmetric).Executable;

    public static string ResolveProcessExecutable(
        string? meshType,
        FailureCriterion failureCriterion = FailureCriterion.MohrCoulomb,
        bool axisymmetric = false)
        => Resolve(meshType, failureCriterion, axisymmetric).ProcessExecutable;

    public static string ResolveProcessExecutable(
        P610AnalysisOptions options,
        MeshViewModel? meshVm,
        bool axisymmetric = false)
    {
        var mesh = meshVm is { IsDiscretized: true, MeshInformationRows.Count: > 0 }
            ? P610AnalysisOptionsHelper.ResolveMeshType(meshVm)
            : P610AnalysisOptionsHelper.NormalizeMeshTypeToken(options.MeshType);
        var axisym = axisymmetric
            || string.Equals(options.AnalysisType, "axisymmetric", StringComparison.OrdinalIgnoreCase)
            || string.Equals(options.AnalysisType, "axisym", StringComparison.OrdinalIgnoreCase);
        return Resolve(mesh, options.FailureCriterion, axisym).ProcessExecutable;
    }

    public static bool UsesP610Deck(string? meshType)
        => Resolve(meshType).Deck == DeckFormat.P610;

    public static bool UsesP64ExplicitDeck(string? meshType)
        => Resolve(meshType).Deck == DeckFormat.P64Explicit;
}
