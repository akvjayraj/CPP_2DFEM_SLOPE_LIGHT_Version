using System.Collections.Generic;
using System.Linq;

namespace GeotechSlopeApp.Models;

/// <summary>One row in the Edge-Element or Edge-Node connectivity DataGrid.</summary>
public class EdgeConnectivityRow
{
    public string EdgeName     { get; init; } = "";
    public int    ElementCount => ElementIds.Count;
    public int    NodeCount    => NodeIds.Count;

    public List<int> ElementIds { get; init; } = new();
    public List<int> NodeIds    { get; init; } = new();

    public string ElementIdsString => ElementIds.Count > 0
        ? string.Join(", ", ElementIds)
        : "(none)";

    public string NodeIdsString => NodeIds.Count > 0
        ? string.Join(", ", NodeIds)
        : "(none)";
}
