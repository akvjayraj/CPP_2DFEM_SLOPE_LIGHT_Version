using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GeotechSlopeApp.Models;

/// <summary>
/// Options for the steady seepage analysis types (Flow-Seepage/Groundwater → Steady State →
/// 2D Plane/axisymmetric [p72], Unconfined / free-surface flow [p73], General 2D [p74], General 3D).
/// Persisted in the .xfem project file.
/// </summary>
public class SeepageAnalysisSettings : INotifyPropertyChanged
{
    private double _upstreamHead = 10.0;
    private double _downstreamHead = 0.0;
    private bool _axisymmetric;
    private double _freeSurfaceTolerance = 0.01;
    private int _freeSurfaceMaxIterations = 20;
    private bool _includeWaterHeadPlaneAxisym;
    private bool _includeWaterHeadFreeSurface;
    private bool _includeWaterHeadGeneral2D;
    private bool _includeWaterHeadGeneral3D;

    /// <summary>Fixed total head (m) applied on the upstream boundary.</summary>
    public double UpstreamHead
    {
        get => _upstreamHead;
        set { if (_upstreamHead != value) { _upstreamHead = value; OnPropertyChanged(); } }
    }

    /// <summary>Fixed total head (m) applied on the downstream boundary.</summary>
    public double DownstreamHead
    {
        get => _downstreamHead;
        set { if (_downstreamHead != value) { _downstreamHead = value; OnPropertyChanged(); } }
    }

    /// <summary>p72 only: treat the section as axisymmetric (x = radial coordinate) instead of plane.</summary>
    public bool Axisymmetric
    {
        get => _axisymmetric;
        set { if (_axisymmetric != value) { _axisymmetric = value; OnPropertyChanged(); } }
    }

    /// <summary>p73 only: free-surface iteration convergence tolerance.</summary>
    public double FreeSurfaceTolerance
    {
        get => _freeSurfaceTolerance;
        set { if (_freeSurfaceTolerance != value) { _freeSurfaceTolerance = value; OnPropertyChanged(); } }
    }

    /// <summary>p73 only: maximum free-surface iterations.</summary>
    public int FreeSurfaceMaxIterations
    {
        get => _freeSurfaceMaxIterations;
        set { if (_freeSurfaceMaxIterations != value) { _freeSurfaceMaxIterations = value; OnPropertyChanged(); } }
    }

    /// <summary>Steady State → 2D Plane/axisymmetric → Water Head (interactive heads on Groundwater tab).</summary>
    public bool IncludeWaterHeadPlaneAxisym
    {
        get => _includeWaterHeadPlaneAxisym;
        set { if (_includeWaterHeadPlaneAxisym != value) { _includeWaterHeadPlaneAxisym = value; OnPropertyChanged(); } }
    }

    /// <summary>Steady State → Unconfined / free-surface flow → Water Head.</summary>
    public bool IncludeWaterHeadFreeSurface
    {
        get => _includeWaterHeadFreeSurface;
        set { if (_includeWaterHeadFreeSurface != value) { _includeWaterHeadFreeSurface = value; OnPropertyChanged(); } }
    }

    /// <summary>Steady State → General 2D → Water Head.</summary>
    public bool IncludeWaterHeadGeneral2D
    {
        get => _includeWaterHeadGeneral2D;
        set { if (_includeWaterHeadGeneral2D != value) { _includeWaterHeadGeneral2D = value; OnPropertyChanged(); } }
    }

    /// <summary>Steady State → General 3D seepage → Water Head.</summary>
    public bool IncludeWaterHeadGeneral3D
    {
        get => _includeWaterHeadGeneral3D;
        set { if (_includeWaterHeadGeneral3D != value) { _includeWaterHeadGeneral3D = value; OnPropertyChanged(); } }
    }

    /// <summary>Water Head flag for the given seepage solver type name, or false if unknown.</summary>
    public bool GetIncludeWaterHeadForType(string? detailedAnalysisType) => detailedAnalysisType switch
    {
        "2D Plane/axisymmetric" => IncludeWaterHeadPlaneAxisym,
        "Unconfined / free-surface flow" => IncludeWaterHeadFreeSurface,
        "General 2D" => IncludeWaterHeadGeneral2D,
        "General 3D seepage" => IncludeWaterHeadGeneral3D,
        _ => false
    };

    /// <summary>Apply a Water Head flag to every steady-state solver (legacy Static Water Head migration).</summary>
    public void SetAllIncludeWaterHead(bool value)
    {
        IncludeWaterHeadPlaneAxisym = value;
        IncludeWaterHeadFreeSurface = value;
        IncludeWaterHeadGeneral2D = value;
        IncludeWaterHeadGeneral3D = value;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
