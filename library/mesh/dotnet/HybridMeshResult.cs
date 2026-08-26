using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Mesh produced for one region by the hybrid driver. Connectivity indices are 0-based into the
/// shared <see cref="HybridMeshResult.Nodes"/> pool, so structured and unstructured regions are
/// already conforming (they reference the same node ids on shared interfaces).
/// </summary>
public sealed class HybridRegionMesh
{
    public int SurfaceNumber { get; init; }

    /// <summary>The strategy actually used to produce the elements below.</summary>
    public RegionMeshStrategy Strategy { get; init; }

    /// <summary>Triangle connectivity (0-based into the shared node pool). Empty for quad regions.</summary>
    public IReadOnlyList<(int a, int b, int c)> Triangles { get; init; } = Array.Empty<(int, int, int)>();

    /// <summary>Quad connectivity (0-based, CCW). Empty for triangle regions.</summary>
    public IReadOnlyList<(int a, int b, int c, int d)> Quads { get; init; } = Array.Empty<(int, int, int, int)>();

    /// <summary>Diagnostic note about why this strategy was selected.</summary>
    public string Reason { get; init; } = string.Empty;

    public int ElementCount => Triangles.Count + Quads.Count;
}

/// <summary>
/// Unified output of <see cref="HybridMeshDriver"/>: a single shared node pool plus per-region
/// element sets tagged with the strategy that produced them. This is the canonical hand-off shape
/// the view-model adapter converts into <c>MeshInformationRow</c> / display collections, and the
/// shape Phase 2 (conformal stitching) and Phase 3 (adaptive refinement) operate on.
/// </summary>
public sealed class HybridMeshResult
{
    public IReadOnlyList<Point> Nodes { get; init; } = Array.Empty<Point>();
    public IReadOnlyList<HybridRegionMesh> Regions { get; init; } = Array.Empty<HybridRegionMesh>();

    /// <summary>Minimum triangle angle across the whole mesh (degrees); 0 if no triangles.</summary>
    public double MinAngleDeg { get; init; }

    /// <summary>Maximum element aspect ratio across the whole mesh.</summary>
    public double MaxAspectRatio { get; init; }

    /// <summary>Quality-gate evaluation for the whole mesh (Phase 2). Never null.</summary>
    public HybridMeshQuality Quality { get; init; } = HybridMeshQuality.Empty;

    /// <summary>True when structured blocks were conformally stitched into surrounding CDT triangles.</summary>
    public bool StitchApplied { get; init; }

    /// <summary>Short diagnostic note about the path taken (fast-path / stitched / CDT fallback).</summary>
    public string Notes { get; init; } = string.Empty;

    public int NodeCount => Nodes.Count;
    public int ElementCount => Regions.Sum(r => r.ElementCount);
    public int TriangleCount => Regions.Sum(r => r.Triangles.Count);
    public int QuadCount => Regions.Sum(r => r.Quads.Count);

    public int StructuredRegionCount =>
        Regions.Count(r => r.Strategy == RegionMeshStrategy.StructuredTransfinite);

    public bool IsEmpty => Nodes.Count == 0 || ElementCount == 0;

    public static HybridMeshResult Empty { get; } = new();

    /// <summary>One-line human summary for the mesh report / status bar.</summary>
    public string Summary() =>
        $"{NodeCount} nodes, {ElementCount} elements " +
        $"({QuadCount} quad / {TriangleCount} tri across {Regions.Count} region(s), " +
        $"{StructuredRegionCount} structured{(StitchApplied ? ", stitched" : "")}); " +
        Quality.Summary();
}
