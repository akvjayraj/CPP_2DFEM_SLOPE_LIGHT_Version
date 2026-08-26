using System.Windows;
using GeotechSlopeApp.Models.FieldStations;

namespace GeotechSlopeApp.Services.FieldStations;

public readonly record struct SoilBlockBounds(double MinX, double MinY, double MaxX, double MaxY);

/// <summary>Axis-aligned closed rectangle that contains all field-station logs, plus a margin.</summary>
public static class FieldStationSoilBlock
{
    public const double DefaultMargin = 5.0;

    public static double ToModelY(double importedY, double yOffset) => importedY + yOffset;

    public static bool TryComputeBounds(
        IEnumerable<FieldStation> stations,
        double margin,
        out SoilBlockBounds bounds,
        double yOffset = 0)
    {
        bounds = default;
        if (stations == null)
            return false;

        var xs = new List<double>();
        var ys = new List<double>();
        foreach (var station in stations)
        {
            xs.Add(station.X);
            if (station.CollarY != 0)
                ys.Add(ToModelY(station.CollarY, yOffset));
            foreach (var interval in station.Intervals)
            {
                ys.Add(ToModelY(interval.TopY, yOffset));
                ys.Add(ToModelY(interval.BottomY, yOffset));
            }
        }

        if (xs.Count == 0 || ys.Count == 0)
            return false;

        var pad = Math.Max(0, margin);
        var minX = xs.Min() - pad;
        var maxX = xs.Max() + pad;
        var minY = ys.Min() - pad;
        var maxY = ys.Max() + pad;
        var minSpan = Math.Max(pad, 1.0);
        if (maxX - minX < 1e-9)
        {
            minX -= minSpan;
            maxX += minSpan;
        }

        if (maxY - minY < 1e-9)
        {
            minY -= minSpan;
            maxY += minSpan;
        }

        bounds = new SoilBlockBounds(minX, minY, maxX, maxY);
        return true;
    }

    public static IReadOnlyList<Point> CollectContactSnapPoints(
        IEnumerable<FieldStation> stations,
        double yOffset = 0)
    {
        var points = new List<Point>();
        if (stations == null)
            return points;

        foreach (var station in stations)
        {
            foreach (var interval in station.Intervals)
            {
                points.Add(new Point(station.X, ToModelY(interval.TopY, yOffset)));
                points.Add(new Point(station.X, ToModelY(interval.BottomY, yOffset)));
            }
        }

        return points;
    }

    /// <summary>Extends a polyline to the soil-block left and right X edges along the end-segment slopes.</summary>
    public static List<(double X, double Y)> ExtendPolylineToBlockX(
        IReadOnlyList<(double X, double Y)> cut,
        double minX,
        double maxX,
        double eps = 1e-9)
    {
        var pts = cut.OrderBy(p => p.X).ToList();
        if (pts.Count < 2)
            return pts;

        (double X, double Y) Extend((double X, double Y) end, (double X, double Y) neighbor, double targetX)
        {
            var dx = end.X - neighbor.X;
            var dy = end.Y - neighbor.Y;
            if (Math.Abs(dx) < eps)
                return (targetX, end.Y);
            var slope = dy / dx;
            return (targetX, end.Y + slope * (targetX - end.X));
        }

        if (pts[0].X > minX + eps)
            pts.Insert(0, Extend(pts[0], pts[1], minX));
        if (pts[^1].X < maxX - eps)
            pts.Add(Extend(pts[^1], pts[^2], maxX));
        return pts;
    }
}
