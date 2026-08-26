using GeotechSlopeApp.Models.Analysis;

namespace GeotechSlopeApp.Services.Analysis;

/// <summary>
/// Honest engine catalog. Missing solvers stay NotImplemented — never a silent no-op.
/// </summary>
public static class AnalysisCapabilityCatalog
{
    public const string FeatureNotAvailable = "Feature not available in current FEM engine.";

    private static readonly IAnalysisCapability[] Items =
    {
        new AnalysisCapability
        {
            Intent = AnalysisIntent.InitialStress,
            DisplayName = "Initial stress",
            Status = AnalysisCapabilityStatus.Available
        },
        new AnalysisCapability
        {
            Intent = AnalysisIntent.Static,
            DisplayName = "Static",
            Status = AnalysisCapabilityStatus.Available
        },
        new AnalysisCapability
        {
            Intent = AnalysisIntent.Srf,
            DisplayName = "Strength reduction (SRF)",
            Status = AnalysisCapabilityStatus.Available
        },
        new AnalysisCapability
        {
            Intent = AnalysisIntent.Seepage,
            DisplayName = "Steady seepage",
            Status = AnalysisCapabilityStatus.Available
        },
        new AnalysisCapability
        {
            Intent = AnalysisIntent.Consolidation,
            DisplayName = "Consolidation / transient",
            Status = AnalysisCapabilityStatus.Available
        },
        new AnalysisCapability
        {
            Intent = AnalysisIntent.Dynamic,
            DisplayName = "Dynamic",
            Status = AnalysisCapabilityStatus.Partial,
            UnavailableReason = "Time-history dynamics are not available. Pseudo-static kₕ / kᵥ under Loads is the live path."
        },
        new AnalysisCapability
        {
            Intent = AnalysisIntent.Thermal,
            DisplayName = "Thermal / THM",
            Status = AnalysisCapabilityStatus.NotImplemented,
            UnavailableReason = FeatureNotAvailable + " Thermal and THM coupling are not in this engine."
        },
        new AnalysisCapability
        {
            Intent = AnalysisIntent.Coupled,
            DisplayName = "Coupled",
            Status = AnalysisCapabilityStatus.Partial,
            UnavailableReason = "Fully coupled Biot is not available. Steady seepage → SRF and prescribed-u → SRF are the live coupling paths."
        }
    };

    public static IReadOnlyList<IAnalysisCapability> All => Items;

    public static IAnalysisCapability Get(AnalysisIntent intent) =>
        Items.First(c => c.Intent == intent);

    public static bool CanRunNativeSolver(AnalysisIntent intent) =>
        Get(intent).Status != AnalysisCapabilityStatus.NotImplemented;

    public static string Describe(AnalysisIntent intent)
    {
        var cap = Get(intent);
        return cap.Status == AnalysisCapabilityStatus.Available
            ? cap.DisplayName
            : $"{cap.DisplayName}: {cap.UnavailableReason}";
    }
}
