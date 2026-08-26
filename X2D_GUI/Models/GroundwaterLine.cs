using System.Windows;

namespace GeotechSlopeApp.Models;

public class GroundwaterLine
{
    public GroundwaterLine(Point start, Point end, double elevation)
    {
        Start = start;
        End = end;
        Elevation = elevation;
    }

    public Point Start { get; }
    public Point End { get; }
    public double Elevation { get; }
    public string Label => $"Water Table ({Elevation:F2})";
}
