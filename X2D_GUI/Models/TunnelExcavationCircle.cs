using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;

namespace GeotechSlopeApp.Models;

public class TunnelExcavationCircle
{
    public Point Center { get; set; }
    public double Radius { get; set; }
    public bool IsRestored { get; set; }
    public int? StageActivation { get; set; }
    public ObservableCollection<PointCollection> ClippedSegments { get; set; } = new();
}
