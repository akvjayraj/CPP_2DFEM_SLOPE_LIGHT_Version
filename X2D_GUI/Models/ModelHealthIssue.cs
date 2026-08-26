using System;

namespace GeotechSlopeApp.Models;

/// <summary>
/// Severity bucket for a single Model Health rule. Mirrors the three-tier
/// palette already used in the Verification Suite Browser:
/// Critical = red (model will not solve / will produce garbage),
/// Warning  = amber (model will solve but the result is suspect),
/// Info     = slate (cosmetic note, no action required).
/// </summary>
public enum ModelHealthSeverity
{
    Info,
    Warning,
    Critical,
}

/// <summary>
/// One flagged condition emitted by <c>ModelHealthViewModel</c>. Each issue
/// carries enough context for the user to act on it without reading source:
/// a short title, a one-sentence body explaining why it matters, and an
/// optional fix-it hint that names the concrete tab/dialog where the user
/// can correct it.
/// </summary>
public sealed class ModelHealthIssue
{
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public string FixHint { get; init; } = "";
    public ModelHealthSeverity Severity { get; init; } = ModelHealthSeverity.Info;

    public string SeverityLabel => Severity switch
    {
        ModelHealthSeverity.Critical => "CRITICAL",
        ModelHealthSeverity.Warning  => "WARNING",
        ModelHealthSeverity.Info     => "INFO",
        _ => "",
    };
}
