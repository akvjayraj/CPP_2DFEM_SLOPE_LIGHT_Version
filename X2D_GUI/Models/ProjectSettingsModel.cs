using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GeotechSlopeApp.Models;

/// <summary>Project-level settings bound by Project Settings view (General and Grid &amp; Axes tabs).</summary>
public class ProjectSettingsModel : INotifyPropertyChanged
{
    private string _projectName = "";
    private string _author = "";
    private DateTime _createdOn = DateTime.Today;
    private string _units = "SI";
    private int _bishopMaxIterations = 100;
    private double _bishopConvergenceTolerance = 1e-6;
    private double _bishopInitialFactor = 1.0;
    private int _numberOfStages = 1;
    private double _axisXMin = 0.0;
    private double _axisXMax = 100.0;
    private double _axisYMin = 0.0;
    private double _axisYMax = 60.0;
    private double _gridSpacingMeters = 10.0;
    private bool _showGrid;
    private bool _showAxes = true;

    public string ProjectName { get => _projectName; set => Set(ref _projectName, value ?? ""); }
    public string Author { get => _author; set => Set(ref _author, value ?? ""); }
    public DateTime CreatedOn { get => _createdOn; set => Set(ref _createdOn, value); }
    public string Units { get => _units; set => Set(ref _units, value ?? "SI"); }
    public int BishopMaxIterations { get => _bishopMaxIterations; set => Set(ref _bishopMaxIterations, value); }
    public double BishopConvergenceTolerance { get => _bishopConvergenceTolerance; set => Set(ref _bishopConvergenceTolerance, value); }
    public double BishopInitialFactor { get => _bishopInitialFactor; set => Set(ref _bishopInitialFactor, value); }
    public int NumberOfStages { get => _numberOfStages; set => Set(ref _numberOfStages, value); }
    public double AxisXMin { get => _axisXMin; set => Set(ref _axisXMin, value); }
    public double AxisXMax { get => _axisXMax; set => Set(ref _axisXMax, value); }
    public double AxisYMin { get => _axisYMin; set => Set(ref _axisYMin, value); }
    public double AxisYMax { get => _axisYMax; set => Set(ref _axisYMax, value); }
    public double GridSpacingMeters { get => _gridSpacingMeters; set => Set(ref _gridSpacingMeters, value); }
    public bool ShowGrid { get => _showGrid; set => Set(ref _showGrid, value); }
    public bool ShowAxes { get => _showAxes; set => Set(ref _showAxes, value); }

    /// <summary>Optional EPSG / ground-control frame for CAD/GIS interchange.</summary>
    public ProjectCoordinateSystem CoordinateSystem { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
