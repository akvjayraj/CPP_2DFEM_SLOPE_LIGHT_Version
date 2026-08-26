using System;
using System.Collections.Generic;
using System.Windows;
using GeotechSlopeApp.Models.Support;

namespace GeotechSlopeApp.Services.Support;

public enum FeSupportSpacingMeasure
{
    AlongFace,
    Vertical,
    Horizontal
}

public sealed record FeSupportPatternRequest(
    FeSupportType Type,
    double StartX,
    double EndX,
    double SpacingM,
    int? FixedCount,
    double LengthM,
    double InclinationDeg,
    FeSupportSpacingMeasure Measure,
    double? EndLengthM = null,
    double? EndInclinationDeg = null,
    int RowCount = 1,
    double RowStaggerFraction = 0.5);

public sealed record FeSupportPatternSegment(
    double HeadX, double HeadY, double TailX, double TailY, int RowIndex = 0);

public sealed class FeSupportPatternResult
{
    public IReadOnlyList<FeSupportPatternSegment> Segments { get; init; } =
        Array.Empty<FeSupportPatternSegment>();

    public int Count => Segments.Count;
    public double ExtentM { get; init; }
    public double AchievedSpacingM { get; init; }
    public int RowCount { get; init; } = 1;
    public FeSupportSpacingMeasure Measure { get; init; }
    public string Summary { get; init; } = "";
    public bool IsValid => Segments.Count > 0;

    public static FeSupportPatternResult Empty(string summary) => new() { Summary = summary };
}

/// <summary>
/// Generates rows of equally spaced linear support elements along the slope face.
/// Heads land on the ground surface; tails follow length and inclination into the mass.
/// </summary>
public static class FeSupportPatternGenerator
{
    public const double MinSpacingM = 0.05;
    public const int MaxCount = 500;
    public const int MaxRows = 4;

    public static FeSupportPatternResult Generate(
        FeSupportPatternRequest request,
        IReadOnlyList<Point>? ground)
    {
        if (request == null)
            return FeSupportPatternResult.Empty("No pattern request.");
        if (FeSupportTypeHelpers.IsFaceSupport(request.Type))
            return FeSupportPatternResult.Empty(
                "Face support covers an extent, so it is not placed at a spacing.");

        var x0 = Math.Min(request.StartX, request.EndX);
        var x1 = Math.Max(request.StartX, request.EndX);
        if (x1 - x0 < 1e-6)
            return FeSupportPatternResult.Empty("Set a row extent (Start X and End X) first.");

        var surface = Clip(ground, x0, x1);
        if (surface.Count < 2)
            return FeSupportPatternResult.Empty("No ground surface between the row limits.");

        var spacing = Math.Max(MinSpacingM, request.SpacingM);
        var extent = Extent(surface, request.Measure);
        if (extent < 1e-6)
            return FeSupportPatternResult.Empty("The row extent is too small for a pattern.");

        int count;
        if (request.FixedCount is { } fixedCount)
        {
            count = Math.Clamp(fixedCount, 1, MaxCount);
            spacing = count > 1 ? extent / (count - 1) : 0.0;
            if (count > 1 && spacing < MinSpacingM)
            {
                count = Math.Max(1, (int)Math.Floor(extent / MinSpacingM) + 1);
                spacing = count > 1 ? extent / (count - 1) : 0.0;
            }
        }
        else
        {
            if (extent < spacing - 1e-9)
                return FeSupportPatternResult.Empty(
                    $"The extent ({extent:0.##} m) is shorter than one spacing ({spacing:0.##} m).");
            count = (int)Math.Floor(extent / spacing + 1e-9) + 1;
            if (count > MaxCount)
                return FeSupportPatternResult.Empty(
                    $"{count} elements at {spacing:0.##} m exceeds the {MaxCount} element limit.");
        }

        var sign = surface[^1].Y >= surface[0].Y ? 1.0 : -1.0;
        var startLength = Math.Max(0.0, request.LengthM);
        var endLength = Math.Max(0.0, request.EndLengthM ?? request.LengthM);
        var startIncl = Math.Clamp(request.InclinationDeg, -89.9, 89.9);
        var endIncl = Math.Clamp(request.EndInclinationDeg ?? request.InclinationDeg, -89.9, 89.9);
        var rows = Math.Clamp(request.RowCount, 1, MaxRows);
        var stagger = Math.Clamp(request.RowStaggerFraction, 0.0, 1.0);

        FeSupportPatternSegment SegmentAt(double pos, int row)
        {
            var t = extent > 1e-12 ? Math.Clamp(pos / extent, 0.0, 1.0) : 0.0;
            var length = startLength + (endLength - startLength) * t;
            var rad = (startIncl + (endIncl - startIncl) * t) * Math.PI / 180.0;
            var head = HeadAt(surface, request.Measure, pos);
            return new FeSupportPatternSegment(
                head.X, head.Y,
                head.X + sign * Math.Cos(rad) * length,
                head.Y - Math.Sin(rad) * length,
                row);
        }

        var segments = new List<FeSupportPatternSegment>(count * rows);
        for (var i = 0; i < count; i++)
            segments.Add(SegmentAt(i * spacing, 0));

        if (rows > 1 && spacing >= MinSpacingM && stagger > 1e-9)
        {
            for (var row = 1; row < rows; row++)
            {
                var offset0 = row * stagger * spacing;
                for (var pos = offset0; pos <= extent + 1e-9; pos += spacing)
                {
                    segments.Add(SegmentAt(pos, row));
                    if (segments.Count > MaxCount)
                        return FeSupportPatternResult.Empty(
                            $"{rows} rows at {spacing:0.##} m exceeds the {MaxCount} element limit.");
                }
            }
        }

        var name = FeSupportTypeHelpers.DisplayName(request.Type).ToLowerInvariant();
        var plural = segments.Count == 1 ? name : name + "s";
        return new FeSupportPatternResult
        {
            Segments = segments,
            ExtentM = extent,
            AchievedSpacingM = spacing,
            Measure = request.Measure,
            RowCount = rows,
            Summary = rows > 1
                ? $"{rows} rows, {segments.Count} {plural} at {spacing:0.##} m c/c"
                : $"{segments.Count} {plural} at {spacing:0.##} m c/c over {extent:0.##} m"
        };
    }

    private static List<Point> Clip(IReadOnlyList<Point>? ground, double x0, double x1)
    {
        var clipped = new List<Point>();
        if (ground == null || ground.Count < 2)
            return clipped;
        clipped.Add(new Point(x0, GroundYAt(ground, x0)));
        foreach (var p in ground)
        {
            if (p.X > x0 + 1e-9 && p.X < x1 - 1e-9)
                clipped.Add(p);
        }
        clipped.Add(new Point(x1, GroundYAt(ground, x1)));
        return clipped;
    }

    private static double Extent(IReadOnlyList<Point> surface, FeSupportSpacingMeasure measure) =>
        measure switch
        {
            FeSupportSpacingMeasure.Vertical => Math.Abs(surface[^1].Y - surface[0].Y),
            FeSupportSpacingMeasure.Horizontal => Math.Abs(surface[^1].X - surface[0].X),
            _ => ArcLength(surface)
        };

    private static double ArcLength(IReadOnlyList<Point> surface)
    {
        var total = 0.0;
        for (var i = 0; i < surface.Count - 1; i++)
        {
            var dx = surface[i + 1].X - surface[i].X;
            var dy = surface[i + 1].Y - surface[i].Y;
            total += Math.Sqrt(dx * dx + dy * dy);
        }
        return total;
    }

    private static Point HeadAt(IReadOnlyList<Point> surface, FeSupportSpacingMeasure measure, double offset)
    {
        if (measure == FeSupportSpacingMeasure.Horizontal)
        {
            var x = surface[0].X + offset;
            return new Point(x, GroundYAt(surface, x));
        }

        if (measure == FeSupportSpacingMeasure.Vertical)
        {
            var down = surface[^1].Y <= surface[0].Y ? -1.0 : 1.0;
            var target = surface[0].Y + down * offset;
            return AtElevation(surface, target);
        }

        var remaining = offset;
        for (var i = 0; i < surface.Count - 1; i++)
        {
            var a = surface[i];
            var b = surface[i + 1];
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var seg = Math.Sqrt(dx * dx + dy * dy);
            if (seg < 1e-12) continue;
            if (remaining <= seg + 1e-9)
            {
                var t = Math.Clamp(remaining / seg, 0.0, 1.0);
                return new Point(a.X + t * dx, a.Y + t * dy);
            }
            remaining -= seg;
        }
        return surface[^1];
    }

    private static Point AtElevation(IReadOnlyList<Point> surface, double y)
    {
        for (var i = 0; i < surface.Count - 1; i++)
        {
            var a = surface[i];
            var b = surface[i + 1];
            var lo = Math.Min(a.Y, b.Y);
            var hi = Math.Max(a.Y, b.Y);
            if (y < lo - 1e-9 || y > hi + 1e-9) continue;
            var dy = b.Y - a.Y;
            if (Math.Abs(dy) < 1e-12) return a;
            var t = Math.Clamp((y - a.Y) / dy, 0.0, 1.0);
            return new Point(a.X + t * (b.X - a.X), y);
        }
        return surface[^1];
    }

    private static double GroundYAt(IReadOnlyList<Point> ground, double x)
    {
        for (var i = 0; i < ground.Count - 1; i++)
        {
            var a = ground[i];
            var b = ground[i + 1];
            if (x < Math.Min(a.X, b.X) - 1e-9 || x > Math.Max(a.X, b.X) + 1e-9)
                continue;
            var dx = b.X - a.X;
            if (Math.Abs(dx) < 1e-12) return b.Y;
            return a.Y + (b.Y - a.Y) * (x - a.X) / dx;
        }
        return x <= ground[0].X ? ground[0].Y : ground[^1].Y;
    }
}
