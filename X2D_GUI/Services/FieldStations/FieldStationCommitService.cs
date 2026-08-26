using GeotechSlopeApp.Models.FieldStations;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Services.FieldStations;

public sealed class FieldStationCommitResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public IReadOnlyList<double> CutYs { get; init; } = Array.Empty<double>();
    public int AssignedLayerCount { get; init; }
}

/// <summary>
/// Commits field-station contacts into the existing horizontal layer-cut pipeline.
/// Does not run FoS solvers. v1 writes horizontal cuts only (linear connect reserved for v2).
/// </summary>
public static class FieldStationCommitService
{
    public const double ElevationTolerance = 1e-6;

    public static bool TryNormalize(FieldStation station, out string error)
    {
        error = "";
        if (station.Intervals.Count == 0)
        {
            error = $"Station {station.Id}: no unit intervals.";
            return false;
        }

        foreach (var interval in station.Intervals)
        {
            if (string.IsNullOrWhiteSpace(interval.UnitCode))
            {
                error = $"Station {station.Id}: a unit interval is missing a unit code.";
                return false;
            }

            if (Math.Abs(interval.TopY - interval.BottomY) <= ElevationTolerance)
            {
                error = $"Station {station.Id}: unit {interval.UnitCode} has zero thickness.";
                return false;
            }

            if (interval.TopY < interval.BottomY)
                (interval.TopY, interval.BottomY) = (interval.BottomY, interval.TopY);
        }

        var ordered = station.Intervals.OrderByDescending(i => i.TopY).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            for (var j = i + 1; j < ordered.Count; j++)
            {
                var a = ordered[i];
                var b = ordered[j];
                var overlapTop = Math.Min(a.TopY, b.TopY);
                var overlapBottom = Math.Max(a.BottomY, b.BottomY);
                if (overlapTop - overlapBottom > ElevationTolerance)
                {
                    error = $"Station {station.Id}: units {a.UnitCode} and {b.UnitCode} overlap.";
                    return false;
                }
            }
        }

        station.Intervals.Clear();
        foreach (var interval in ordered)
            station.Intervals.Add(interval);
        return true;
    }

    public static FieldStationCommitResult CommitHorizontal(
        FieldStationSet set,
        GeometryDefinitionViewModel geometry,
        MaterialPropertiesViewModel? materials,
        double yOffset = 0)
    {
        if (set.Stations.Count == 0)
            return Fail("Import at least one field station before committing.");

        foreach (var station in set.Stations)
        {
            if (!TryNormalize(station, out var error))
                return Fail(error);
        }

        var unmapped = set.UnitMaterialMap.Where(r => !r.IsMapped).Select(r => r.UnitCode).ToList();
        if (unmapped.Count > 0)
            return Fail("Map every unit code to a material before committing: " + string.Join(", ", unmapped));

        var cutYs = CollectInteriorContactYs(set.Stations, yOffset);
        if (cutYs.Count == 0)
            return Fail("No interior unit contacts to write as layer cuts.");

        if (geometry.GeometryVertexRows.Count < 3)
        {
            var margin = geometry.ProjectSettings?.FieldStationSoilBlockMargin
                         ?? FieldStationSoilBlock.DefaultMargin;
            if (!FieldStationSoilBlock.TryComputeBounds(set.Stations, margin, out var block, yOffset))
                return Fail("Could not compute a closed soil block from the stations.");
            geometry.CreateClosedSoilBlock(block.MinX, block.MinY, block.MaxX, block.MaxY, recordUndo: false);
        }

        var assignments = BuildBottomToTopAssignments(set, geometry, cutYs, yOffset);
        if (assignments.Count == 0)
            return Fail("Could not match unit intervals to layer bands.");

        geometry.CommitFieldStationHorizontalCuts(cutYs, assignments, "Commit field stations to layer cuts");
        ApplyUnitMapToUnassignedLayers(set, geometry, yOffset);
        materials?.RebuildLayerAssignments();
        set.LastCommitUtc = DateTimeOffset.UtcNow;

        return new FieldStationCommitResult
        {
            Success = true,
            Message = $"Committed {cutYs.Count} interface(s) and {assignments.Count} layer assignment(s).",
            CutYs = cutYs,
            AssignedLayerCount = assignments.Count
        };
    }

    public static IReadOnlyList<double> CollectInteriorContactYs(
        IEnumerable<FieldStation> stations,
        double yOffset = 0)
    {
        var cuts = new SortedSet<double>();
        foreach (var station in stations)
        {
            if (station.Intervals.Count == 0) continue;
            var ys = station.Intervals
                .SelectMany(i => new[]
                {
                    FieldStationSoilBlock.ToModelY(i.TopY, yOffset),
                    FieldStationSoilBlock.ToModelY(i.BottomY, yOffset)
                })
                .ToList();
            var minY = ys.Min();
            var maxY = ys.Max();
            foreach (var y in ys)
            {
                if (y > minY + ElevationTolerance && y < maxY - ElevationTolerance)
                    cuts.Add(y);
            }
        }

        return cuts.ToList();
    }

    /// <summary>
    /// Writes Field Stations unit→material ids onto any layer that still has no assignment.
    /// Does not create cuts; call after Commit or when the user maps a unit on Analysis.
    /// </summary>
    public static int ApplyUnitMapToUnassignedLayers(
        FieldStationSet set,
        GeometryDefinitionViewModel geometry,
        double yOffset = 0)
    {
        if (set.Stations.Count == 0 || geometry.MaterialLayerCutElevations.Count == 0)
            return 0;

        var assignments = BuildBottomToTopAssignments(set, geometry, geometry.MaterialLayerCutElevations, yOffset);
        var written = 0;
        foreach (var (displayLayer, materialId) in assignments)
        {
            if (geometry.GetLayerMaterialAssignmentMaterialId(displayLayer) is > 0)
                continue;
            geometry.SetLayerMaterialAssignmentMaterialId(displayLayer, materialId);
            written++;
        }

        return written;
    }

    private static List<(int DisplayLayer, int MaterialId)> BuildBottomToTopAssignments(
        FieldStationSet set,
        GeometryDefinitionViewModel geometry,
        IReadOnlyList<double> cutYs,
        double yOffset = 0)
    {
        var (_, _, yMin, yMax) = geometry.GetBoundingBoxBounds();
        var bounds = new List<double> { yMin };
        foreach (var y in cutYs.OrderBy(v => v))
        {
            if (y <= yMin + ElevationTolerance || y >= yMax - ElevationTolerance)
                continue;
            if (bounds.Count > 0 && Math.Abs(y - bounds[^1]) <= ElevationTolerance)
                continue;
            bounds.Add(y);
        }
        bounds.Add(yMax);

        var assignments = new List<(int DisplayLayer, int MaterialId)>();
        for (var i = 0; i < bounds.Count - 1; i++)
        {
            var mid = 0.5 * (bounds[i] + bounds[i + 1]);
            var unit = ResolveUnitAtElevation(set.Stations, mid, yOffset);
            if (unit == null) continue;
            var materialId = set.MaterialIdForUnit(unit);
            if (materialId == null) continue;
            assignments.Add((i + 1, materialId.Value));
        }

        return assignments;
    }

    private static string? ResolveUnitAtElevation(
        IEnumerable<FieldStation> stations,
        double elevation,
        double yOffset = 0)
    {
        FieldUnitInterval? bottom = null;
        FieldUnitInterval? top = null;
        foreach (var station in stations)
        {
            foreach (var interval in station.Intervals)
            {
                var topY = FieldStationSoilBlock.ToModelY(interval.TopY, yOffset);
                var bottomY = FieldStationSoilBlock.ToModelY(interval.BottomY, yOffset);
                if (elevation <= topY + ElevationTolerance && elevation >= bottomY - ElevationTolerance)
                    return interval.UnitCode;
                if (bottom == null || interval.BottomY < bottom.BottomY)
                    bottom = interval;
                if (top == null || interval.TopY > top.TopY)
                    top = interval;
            }
        }

        if (bottom != null && elevation < FieldStationSoilBlock.ToModelY(bottom.BottomY, yOffset))
            return bottom.UnitCode;
        if (top != null && elevation > FieldStationSoilBlock.ToModelY(top.TopY, yOffset))
            return top.UnitCode;
        return null;
    }

    private static FieldStationCommitResult Fail(string message) =>
        new() { Success = false, Message = message };
}
