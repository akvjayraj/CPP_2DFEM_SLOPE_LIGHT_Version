using System.Collections.Generic;
using System.Linq;

namespace GeotechSlopeApp.Models;

/// <summary>
/// Read-only summary row surfaced by Materials &amp; Regions ▸ "Excavated Elements per Stage".
/// One row per excavation stage (Stage 1 = Initial Stress is not represented).
/// </summary>
public sealed class StageExcavationSummaryRow
{
    /// <summary>Maximum number of element IDs shown in the truncated string. The rest are summarised as "+ N more".</summary>
    public const int TruncationLimit = 50;

    public int StageNumber { get; init; }
    public string StageName { get; init; } = "";
    public int ElementCount { get; init; }

    /// <summary>First <see cref="TruncationLimit"/> IDs comma-joined; suffixed with "+ N more" when more exist.</summary>
    public string ElementIdsTruncated { get; init; } = "";

    /// <summary>All IDs comma-joined (used as a tooltip / clipboard payload).</summary>
    public string ElementIdsFull { get; init; } = "";

    public static StageExcavationSummaryRow FromIds(int stageNumber, string stageName, IReadOnlyList<int> ids)
    {
        ids ??= System.Array.Empty<int>();
        var sortedFull = ids.Distinct().OrderBy(i => i).ToList();
        var truncated = sortedFull.Take(TruncationLimit).ToList();
        var truncatedStr = string.Join(", ", truncated);
        if (sortedFull.Count > truncated.Count)
            truncatedStr += $"   + {sortedFull.Count - truncated.Count} more";

        return new StageExcavationSummaryRow
        {
            StageNumber         = stageNumber,
            StageName           = stageName ?? "",
            ElementCount        = sortedFull.Count,
            ElementIdsTruncated = truncatedStr,
            ElementIdsFull      = string.Join(", ", sortedFull),
        };
    }
}
