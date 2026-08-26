using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GeotechSlopeApp.Models;

/// <summary>
/// Represents one construction / analysis stage (Initial Stress, Excavation, Loading, etc.).
/// Used by the staged analysis UI in Project Settings → Analysis Type and by the
/// Staged Analysis subtab under FE Analysis.
/// </summary>
public class StageDefinition : INotifyPropertyChanged
{
    private int _stageNumber;
    private string _stageName = "";
    private string _stageType = "Initial Stress";
    private string _description = "";
    private bool _isActive = true;

    // Phase timing + branching
    private double _duration;
    private string _units = "day";
    private int? _startsFromStageNumber;
    private bool _resetDisplacementsAtStart;

    // Load / stress release
    private double _stressReleaseLambda = 1.0;
    private double _loadFactor = 1.0;

    // Pseudo-static seismic (per-stage when Stage Type = Seismic / Dynamic)
    private double _pseudoStaticKh;
    private double _pseudoStaticKv;

    // Solver
    private string _solverMode = "Plastic"; // Elastic | Plastic | Consolidation | Safety
    private int _maxIterations = 100;
    private double _tolerance = 1e-3;
    private int _maxLoadSteps = 10;
    private bool _arcLength;

    // Activity (surfaces / structural elements / groundwater)
    private bool _liningEnabled;
    private bool _boltsEnabled;
    private string? _groundwaterLineTag;

    // Results export flags (drive what the Results tab shows for this phase)
    private bool _exportUx = true;
    private bool _exportUy = true;
    private bool _exportSxx = true;
    private bool _exportSyy = true;
    private bool _exportTxy = true;
    private bool _exportPlasticZones = true;
    private bool _exportFosHistory;

    private string _notes = "";

    public int StageNumber
    {
        get => _stageNumber;
        set { _stageNumber = value; OnPropertyChanged(); }
    }

    public string StageName
    {
        get => _stageName;
        set { _stageName = value?.Trim() ?? ""; OnPropertyChanged(); }
    }

    public string StageType
    {
        get => _stageType;
        set
        {
            _stageType = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSeismicDynamicStage));
        }
    }

    /// <summary>Matches Project Settings → Stage Analysis → Stage Type "Seismic / Dynamic".</summary>
    public bool IsSeismicDynamicStage =>
        string.Equals(_stageType?.Trim(), "Seismic / Dynamic", StringComparison.OrdinalIgnoreCase);

    /// <summary>Horizontal pseudo-static coefficient k_h (dimensionless, typically 0–0.5).</summary>
    public double PseudoStaticKh
    {
        get => _pseudoStaticKh;
        set { _pseudoStaticKh = Math.Clamp(value, -1.0, 1.0); OnPropertyChanged(); }
    }

    /// <summary>Vertical pseudo-static coefficient k_v (0 = full gravity on vertical).</summary>
    public double PseudoStaticKv
    {
        get => _pseudoStaticKv;
        set { _pseudoStaticKv = Math.Clamp(value, -1.0, 1.0); OnPropertyChanged(); }
    }

    public string Description
    {
        get => _description;
        set { _description = value; OnPropertyChanged(); }
    }

    public bool IsActive
    {
        get => _isActive;
        set { _isActive = value; OnPropertyChanged(); }
    }

    public double Duration
    {
        get => _duration;
        set { _duration = value; OnPropertyChanged(); }
    }

    public string Units
    {
        get => _units;
        set { _units = value ?? "day"; OnPropertyChanged(); }
    }

    public int? StartsFromStageNumber
    {
        get => _startsFromStageNumber;
        set { _startsFromStageNumber = value; OnPropertyChanged(); }
    }

    public bool ResetDisplacementsAtStart
    {
        get => _resetDisplacementsAtStart;
        set { _resetDisplacementsAtStart = value; OnPropertyChanged(); }
    }

    public double StressReleaseLambda
    {
        get => _stressReleaseLambda;
        set
        {
            var v = value;
            if (v < 0.0) v = 0.0;
            if (v > 1.0) v = 1.0;
            _stressReleaseLambda = v;
            OnPropertyChanged();
        }
    }

    public double LoadFactor
    {
        get => _loadFactor;
        set { _loadFactor = value; OnPropertyChanged(); }
    }

    public string SolverMode
    {
        get => _solverMode;
        set { _solverMode = string.IsNullOrWhiteSpace(value) ? "Plastic" : value; OnPropertyChanged(); }
    }

    public int MaxIterations
    {
        get => _maxIterations;
        set { _maxIterations = value < 1 ? 1 : value; OnPropertyChanged(); }
    }

    public double Tolerance
    {
        get => _tolerance;
        set { _tolerance = value <= 0 ? 1e-6 : value; OnPropertyChanged(); }
    }

    public int MaxLoadSteps
    {
        get => _maxLoadSteps;
        set { _maxLoadSteps = value < 1 ? 1 : value; OnPropertyChanged(); }
    }

    public bool ArcLength
    {
        get => _arcLength;
        set { _arcLength = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Surfaces to activate (re-enable) at the start of this phase.
    /// Stored as surface numbers referencing <c>GeometryDefinitionVM.GeometrySurfaces</c>.
    /// </summary>
    public ObservableCollection<int> ActivateSurfaceNumbers { get; } = new();

    /// <summary>
    /// Surfaces to deactivate (excavate) at the start of this phase, in addition to any
    /// excavations defined in <c>StagedConstructionViewModel</c>.
    /// </summary>
    public ObservableCollection<int> DeactivateSurfaceNumbers { get; } = new();

    public bool LiningEnabled
    {
        get => _liningEnabled;
        set { _liningEnabled = value; OnPropertyChanged(); }
    }

    public bool BoltsEnabled
    {
        get => _boltsEnabled;
        set { _boltsEnabled = value; OnPropertyChanged(); }
    }

    public string? GroundwaterLineTag
    {
        get => _groundwaterLineTag;
        set { _groundwaterLineTag = value; OnPropertyChanged(); }
    }

    public string Notes
    {
        get => _notes;
        set { _notes = value ?? ""; OnPropertyChanged(); }
    }

    public bool ExportUx
    {
        get => _exportUx;
        set { _exportUx = value; OnPropertyChanged(); }
    }

    public bool ExportUy
    {
        get => _exportUy;
        set { _exportUy = value; OnPropertyChanged(); }
    }

    public bool ExportSxx
    {
        get => _exportSxx;
        set { _exportSxx = value; OnPropertyChanged(); }
    }

    public bool ExportSyy
    {
        get => _exportSyy;
        set { _exportSyy = value; OnPropertyChanged(); }
    }

    public bool ExportTxy
    {
        get => _exportTxy;
        set { _exportTxy = value; OnPropertyChanged(); }
    }

    public bool ExportPlasticZones
    {
        get => _exportPlasticZones;
        set { _exportPlasticZones = value; OnPropertyChanged(); }
    }

    public bool ExportFosHistory
    {
        get => _exportFosHistory;
        set { _exportFosHistory = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
