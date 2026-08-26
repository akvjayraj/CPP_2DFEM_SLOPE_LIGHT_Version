using GeotechSlopeApp.Models.Analysis;

namespace GeotechSlopeApp.Services.Analysis;

public enum AnalysisCapabilityStatus
{
    Available,
    Partial,
    NotImplemented
}

public interface IAnalysisCapability
{
    AnalysisIntent Intent { get; }
    string DisplayName { get; }
    AnalysisCapabilityStatus Status { get; }
    string UnavailableReason { get; }
}

public sealed class AnalysisCapability : IAnalysisCapability
{
    public AnalysisIntent Intent { get; init; }
    public string DisplayName { get; init; } = "";
    public AnalysisCapabilityStatus Status { get; init; }
    public string UnavailableReason { get; init; } = "";
}
