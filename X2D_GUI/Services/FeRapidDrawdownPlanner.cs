using System.Collections.Generic;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.Services;

public sealed record FeDrawdownStagePlan(string Name, GroundwaterModel Groundwater, string StrengthNote);

/// <summary>
/// Builds GroundwaterModel clones for each drawdown stage. FEM-native: each stage is a
/// prescribed water table (or B-bar hold for the undrained stage).
/// </summary>
public static class FeRapidDrawdownPlanner
{
    public static IReadOnlyList<FeDrawdownStagePlan> BuildStages(
        FeDrawdownSettings settings,
        GroundwaterModel current)
    {
        if (!settings.IsActive)
            return System.Array.Empty<FeDrawdownStagePlan>();

        var stages = new List<FeDrawdownStagePlan>
        {
            new("Stage 1 — pre-drawdown (drained)",
                settings.BuildStageModel(current, postDrawdown: false),
                "Effective-stress Mohr–Coulomb at the high reservoir surface.")
        };

        if (settings.Method == FeDrawdownMethod.InstantaneousDrained)
        {
            stages.Add(new("Stage 2 — post-drawdown (drained)",
                settings.BuildStageModel(current, postDrawdown: true),
                "Drained strengths with the lowered water table."));
            return stages;
        }

        var undrained = settings.BuildStageModel(current, postDrawdown: true);
        if (current.Bbar > 0 || current.Method == GroundwaterMethod.Bbar)
        {
            undrained.Method = GroundwaterMethod.Bbar;
            undrained.Bbar = current.Bbar > 0 ? current.Bbar : 1.0;
        }
        stages.Add(new(
            settings.Method == FeDrawdownMethod.CorpsTwoStage
                ? "Stage 2 — USACE undrained (R envelope / B-bar hold)"
                : "Stage 2 — Duncan undrained (Kc interpolation / B-bar hold)",
            undrained,
            "Keep undrained pore pressure while the reservoir is lowered. Assign R or Kc/Kf envelopes on materials."));

        if (settings.Method == FeDrawdownMethod.DuncanThreeStage && settings.TakeLowerOfUndrainedAndDrained)
        {
            stages.Add(new("Stage 3 — lower of undrained and drained",
                settings.BuildStageModel(current, postDrawdown: true),
                "Re-run with drained post-drawdown strengths; governing SRF is the lower of stages 2 and 3."));
        }

        return stages;
    }
}
