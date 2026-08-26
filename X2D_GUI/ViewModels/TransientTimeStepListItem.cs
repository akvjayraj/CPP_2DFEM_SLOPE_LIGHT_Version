namespace GeotechSlopeApp.ViewModels;

/// <summary>Combo / scrubber row for one transient consolidation time frame.</summary>
public sealed class TransientTimeStepListItem
{
    public int FrameIndex { get; init; }
    public int Step { get; init; }
    public double Time { get; init; }
    public string DisplayName { get; init; } = "";
    public string Summary { get; init; } = "";
    /// <summary>Keeps ResultsVisualizationView incremental-panel bindings from null-refing.</summary>
    public P610T3StagedRowDetails? StageResult { get; init; } = new();
}
