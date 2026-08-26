namespace GeotechSlopeApp.Models;

/// <summary>Axis-space point entered in Material Properties → Layering Controls (shared by horizontal and non-horizontal modes).</summary>
public class LayerPendingPointDisplay
{
    public int PointNumber { get; set; }
    public double AxisX { get; set; }
    public double AxisY { get; set; }

    public LayerPendingPointDisplay(int pointNumber, double axisX, double axisY)
    {
        PointNumber = pointNumber;
        AxisX = axisX;
        AxisY = axisY;
    }
}
