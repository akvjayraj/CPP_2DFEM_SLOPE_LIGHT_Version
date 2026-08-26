using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GeotechSlopeApp.Models;

/// <summary>
/// Options for Flow-Seepage/Groundwater → Transient State solvers:
/// Transient (plane/axisymmetric) [p84] and General 2D consolidation [p88].
/// Persisted in the .xfem project file.
/// </summary>
public class TransientAnalysisSettings : INotifyPropertyChanged
{
    private bool _axisymmetric;
    private double _timeStep = 0.01;
    private int _numberOfSteps = 150;
    private double _theta = 0.5;
    private int _printInterval = 10;
    private int _monitorNode = 31;
    private int _contourStep = 100;
    private int _contourIntervals = 10;
    private double _initialPressure = 100.0;
    private double _fixedPressure;
    private bool _fixTopFace = true;
    private bool _fixBottomFace;
    private bool _fixMinXFace;
    private bool _fixMaxXFace = true;

    /// <summary>p84 only: axisymmetric (r–z) instead of plane strain.</summary>
    public bool Axisymmetric
    {
        get => _axisymmetric;
        set { if (_axisymmetric != value) { _axisymmetric = value; OnPropertyChanged(); } }
    }

    /// <summary>Time-step size Δt (dtim).</summary>
    public double TimeStep
    {
        get => _timeStep;
        set { if (_timeStep != value) { _timeStep = value; OnPropertyChanged(); } }
    }

    /// <summary>Number of time steps (nstep).</summary>
    public int NumberOfSteps
    {
        get => _numberOfSteps;
        set { if (_numberOfSteps != value) { _numberOfSteps = value; OnPropertyChanged(); } }
    }

    /// <summary>θ-method weight (0 = explicit, 0.5 = Crank–Nicolson, 1 = fully implicit).</summary>
    public double Theta
    {
        get => _theta;
        set { if (_theta != value) { _theta = value; OnPropertyChanged(); } }
    }

    /// <summary>Print monitor node every N steps (npri).</summary>
    public int PrintInterval
    {
        get => _printInterval;
        set { if (_printInterval != value) { _printInterval = value; OnPropertyChanged(); } }
    }

    /// <summary>Node for time-history output (nres).</summary>
    public int MonitorNode
    {
        get => _monitorNode;
        set { if (_monitorNode != value) { _monitorNode = value; OnPropertyChanged(); } }
    }

    /// <summary>Time-step index at which a contour/spatial dump is written (ntime).</summary>
    public int ContourStep
    {
        get => _contourStep;
        set { if (_contourStep != value) { _contourStep = value; OnPropertyChanged(); } }
    }

    /// <summary>Contour intervals (nci).</summary>
    public int ContourIntervals
    {
        get => _contourIntervals;
        set { if (_contourIntervals != value) { _contourIntervals = value; OnPropertyChanged(); } }
    }

    /// <summary>Initial excess pore pressure on free (non-fixed) nodes.</summary>
    public double InitialPressure
    {
        get => _initialPressure;
        set { if (_initialPressure != value) { _initialPressure = value; OnPropertyChanged(); } }
    }

    /// <summary>Prescribed pressure on selected drain faces.</summary>
    public double FixedPressure
    {
        get => _fixedPressure;
        set { if (_fixedPressure != value) { _fixedPressure = value; OnPropertyChanged(); } }
    }

    /// <summary>Fix nodes on the max-y (top) face.</summary>
    public bool FixTopFace
    {
        get => _fixTopFace;
        set { if (_fixTopFace != value) { _fixTopFace = value; OnPropertyChanged(); } }
    }

    /// <summary>Fix nodes on the min-y (bottom) face.</summary>
    public bool FixBottomFace
    {
        get => _fixBottomFace;
        set { if (_fixBottomFace != value) { _fixBottomFace = value; OnPropertyChanged(); } }
    }

    /// <summary>Fix nodes on the min-x face.</summary>
    public bool FixMinXFace
    {
        get => _fixMinXFace;
        set { if (_fixMinXFace != value) { _fixMinXFace = value; OnPropertyChanged(); } }
    }

    /// <summary>Fix nodes on the max-x face.</summary>
    public bool FixMaxXFace
    {
        get => _fixMaxXFace;
        set { if (_fixMaxXFace != value) { _fixMaxXFace = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
