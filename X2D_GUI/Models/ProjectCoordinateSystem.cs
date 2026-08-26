using CommunityToolkit.Mvvm.ComponentModel;

namespace GeotechSlopeApp.Models;

/// <summary>Optional EPSG / ground-control frame for CAD/GIS interchange.</summary>
public partial class ProjectCoordinateSystem : ObservableObject
{
    [ObservableProperty]
    private int? _epsgCode;

    [ObservableProperty]
    private string _name = "Local engineering (metres)";

    [ObservableProperty]
    private double _originEasting;

    [ObservableProperty]
    private double _originNorthing;

    [ObservableProperty]
    private double _rotationDeg;

    [ObservableProperty]
    private double _scale = 1.0;

    /// <summary>Maps local model (X,Y) to map easting/northing.</summary>
    public (double Easting, double Northing) ToMap(double x, double y)
    {
        var s = Scale == 0 ? 1.0 : Scale;
        var rad = RotationDeg * System.Math.PI / 180.0;
        var c = System.Math.Cos(rad);
        var sn = System.Math.Sin(rad);
        var xr = (x * c - y * sn) * s;
        var yr = (x * sn + y * c) * s;
        return (OriginEasting + xr, OriginNorthing + yr);
    }

    /// <summary>Maps map easting/northing to local model (X,Y).</summary>
    public (double X, double Y) FromMap(double easting, double northing)
    {
        var s = Scale == 0 ? 1.0 : Scale;
        var dx = (easting - OriginEasting) / s;
        var dy = (northing - OriginNorthing) / s;
        var rad = -RotationDeg * System.Math.PI / 180.0;
        var c = System.Math.Cos(rad);
        var sn = System.Math.Sin(rad);
        return (dx * c - dy * sn, dx * sn + dy * c);
    }
}
