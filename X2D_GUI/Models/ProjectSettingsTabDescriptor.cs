namespace GeotechSlopeApp.Models;

public enum ProjectSettingsTabKind
{
    General,
    AnalysisType,
    Appearance,
    GridAxes,
    FieldStations
}

public sealed class ProjectSettingsTabDescriptor
{
    public ProjectSettingsTabKind Kind { get; init; }
    public string Header { get; init; } = "";
}
