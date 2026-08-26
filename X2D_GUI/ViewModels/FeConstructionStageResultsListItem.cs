namespace GeotechSlopeApp.ViewModels;

/// <summary>
/// Combo row for staged FE analysis results (construction stages from Run All).
/// Mirrors <see cref="P610T3ResultsStageListItem"/> for ResultsVisualizationView bindings.
/// </summary>
public sealed class FeConstructionStageResultsListItem
{
    public int StageListIndex { get; init; }
    public int StageNumber { get; init; }
    public string DisplayName { get; init; } = "";
    public string Summary { get; init; } = "";
    public object? StageResult { get; init; } = new P610T3StagedRowDetails();
}
