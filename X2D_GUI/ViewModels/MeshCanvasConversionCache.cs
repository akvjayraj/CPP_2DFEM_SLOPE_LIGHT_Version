using System.Collections.Generic;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.ViewModels;

/// <summary>Immutable mesh canvas conversion result shared across Mesh/BC wrappers.</summary>
internal sealed class MeshCanvasConversionSnapshot
{
    public required List<MeshDiscretizationNodeDisplay> ConvertedDiscretizationNodes { get; init; }
    public required List<MeshDiscretizationNodeDisplay> HighlightedMeshDiscretizationNodes { get; init; }
    public required List<MeshNodeDisplayItem> MeshNodeDisplayItems { get; init; }
    public required List<MeshElementDisplay> AllMeshElements { get; init; }
}

/// <summary>
/// Shared base + per-stage filtered mesh canvas snapshots keyed by mesh instance and viewport revision.
/// </summary>
internal static class MeshCanvasConversionCache
{
    private static readonly object Gate = new();
    private static readonly Dictionary<(int MeshKey, long GeomRevision, int MeshRevision), MeshCanvasConversionSnapshot> BaseCache = new();
    private static readonly Dictionary<(int MeshKey, long GeomRevision, int MeshRevision, int StageKey, int HiddenHash), MeshCanvasConversionSnapshot> FilteredCache = new();

    public static bool TryGetBase(int meshKey, long geomRevision, int meshRevision, out MeshCanvasConversionSnapshot? snapshot)
    {
        lock (Gate)
            return BaseCache.TryGetValue((meshKey, geomRevision, meshRevision), out snapshot);
    }

    public static void SetBase(int meshKey, long geomRevision, int meshRevision, MeshCanvasConversionSnapshot snapshot)
    {
        lock (Gate)
        {
            BaseCache[(meshKey, geomRevision, meshRevision)] = snapshot;
            RemoveFilteredForMesh(meshKey);
        }
    }

    public static bool TryGetFiltered(
        int meshKey, long geomRevision, int meshRevision, int stageKey, int hiddenHash,
        out MeshCanvasConversionSnapshot? snapshot)
    {
        lock (Gate)
            return FilteredCache.TryGetValue((meshKey, geomRevision, meshRevision, stageKey, hiddenHash), out snapshot);
    }

    public static void SetFiltered(
        int meshKey, long geomRevision, int meshRevision, int stageKey, int hiddenHash,
        MeshCanvasConversionSnapshot snapshot)
    {
        lock (Gate)
            FilteredCache[(meshKey, geomRevision, meshRevision, stageKey, hiddenHash)] = snapshot;
    }

    public static void InvalidateMesh(int meshKey)
    {
        lock (Gate)
        {
            var baseKeys = new List<(int, long, int)>();
            foreach (var key in BaseCache.Keys)
                if (key.MeshKey == meshKey)
                    baseKeys.Add(key);
            foreach (var key in baseKeys)
                BaseCache.Remove(key);
            RemoveFilteredForMesh(meshKey);
        }
    }

    private static void RemoveFilteredForMesh(int meshKey)
    {
        var filteredKeys = new List<(int, long, int, int, int)>();
        foreach (var key in FilteredCache.Keys)
            if (key.MeshKey == meshKey)
                filteredKeys.Add(key);
        foreach (var key in filteredKeys)
            FilteredCache.Remove(key);
    }

    public static MeshCanvasConversionSnapshot CloneSnapshot(MeshCanvasConversionSnapshot source)
        => new()
        {
            ConvertedDiscretizationNodes = new List<MeshDiscretizationNodeDisplay>(source.ConvertedDiscretizationNodes),
            HighlightedMeshDiscretizationNodes = new List<MeshDiscretizationNodeDisplay>(source.HighlightedMeshDiscretizationNodes),
            MeshNodeDisplayItems = new List<MeshNodeDisplayItem>(source.MeshNodeDisplayItems),
            AllMeshElements = new List<MeshElementDisplay>(source.AllMeshElements)
        };

    public static MeshCanvasConversionSnapshot FilterSnapshot(MeshCanvasConversionSnapshot baseSnapshot, IReadOnlySet<int>? hiddenIds)
    {
        if (hiddenIds == null || hiddenIds.Count == 0)
            return CloneSnapshot(baseSnapshot);

        var filteredElements = new List<MeshElementDisplay>();
        foreach (var el in baseSnapshot.AllMeshElements)
        {
            if (!hiddenIds.Contains(el.DisplayId))
                filteredElements.Add(el);
        }

        return new MeshCanvasConversionSnapshot
        {
            ConvertedDiscretizationNodes = baseSnapshot.ConvertedDiscretizationNodes,
            HighlightedMeshDiscretizationNodes = baseSnapshot.HighlightedMeshDiscretizationNodes,
            MeshNodeDisplayItems = baseSnapshot.MeshNodeDisplayItems,
            AllMeshElements = filteredElements
        };
    }
}
