using System;
using System.Collections.Generic;
using System.Linq;

namespace GeotechSlopeApp.Backend;

/// <summary>Structured mesh build output including boundary-fill statistics.</summary>
public sealed class StructuredMeshBuildResult
{
    public P64EmbankmentMeshBuilder.MeshResult Mesh { get; init; } = null!;
    public int BoundaryFillElementCount { get; init; }
    public bool UsedBoundaryFittedStrategy { get; init; }
    public string StrategyName { get; init; } = string.Empty;
}
