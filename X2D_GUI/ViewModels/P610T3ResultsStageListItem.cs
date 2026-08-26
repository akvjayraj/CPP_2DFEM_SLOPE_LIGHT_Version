using System.Collections.Generic;
using GeotechSlopeApp.Models.FiniteElement;

namespace GeotechSlopeApp.ViewModels;

/// <summary>Combo row for p610_t3 multi-stage JSON. Matches ResultsVisualizationView: DisplayName, Summary, StageResult.</summary>
public sealed class P610T3ResultsStageListItem
{
    public int StageListIndex { get; init; }
    public string DisplayName { get; init; } = "";
    public string Summary { get; init; } = "";
    /// <summary>Present so "Incremental from previous" panel bindings do not null-ref; p610_t3 does not compute those increments in the app.</summary>
    public P610T3StagedRowDetails? StageResult { get; init; } = new();
}

public sealed class P610T3StagedRowDetails
{
    public object IncrementalDisplacements { get; init; } = "— (not computed for p610_t3)";
    public IReadOnlyList<ElementStress> IncrementalStresses { get; init; } = new List<ElementStress>();
}
