using System.Windows;

namespace GeotechSlopeApp.Models;

/// <summary>
/// One edge of a closed surface perimeter in axis space. Straight edges are a line segment;
/// arc edges store three points in boundary order (from → through → to), matching canvas overlay resolution.
/// </summary>
public readonly struct SurfaceAxisPerimeterSegment
{
    public bool IsArc { get; }
    public Point A { get; }
    public Point B { get; }
    public Point C { get; }

    public static SurfaceAxisPerimeterSegment Line(Point start, Point end) =>
        new(false, start, end, default);

    /// <summary>Three axis points along the arc in traversal order.</summary>
    public static SurfaceAxisPerimeterSegment Arc(Point from, Point through, Point to) =>
        new(true, from, through, to);

    private SurfaceAxisPerimeterSegment(bool isArc, Point a, Point b, Point c)
    {
        IsArc = isArc;
        A = a;
        B = b;
        C = c;
    }
}
