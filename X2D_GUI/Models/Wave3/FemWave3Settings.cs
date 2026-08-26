using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GeotechSlopeApp.Models.Wave3;

public sealed class FemSensitivitySettings : INotifyPropertyChanged
{
    private int _simulationCount = 2;
    private double _materialVariationPercent = 20;
    private bool _isProbabilistic;

    public int SimulationCount
    {
        get => _simulationCount;
        set { if (_simulationCount == value) return; _simulationCount = Math.Max(1, value); OnPropertyChanged(); }
    }

    public double MaterialVariationPercent
    {
        get => _materialVariationPercent;
        set { if (Math.Abs(_materialVariationPercent - value) < 1e-12) return; _materialVariationPercent = value; OnPropertyChanged(); }
    }

    public bool IsProbabilistic
    {
        get => _isProbabilistic;
        set { if (_isProbabilistic == value) return; _isProbabilistic = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class FemProbabilisticSettings : INotifyPropertyChanged
{
    private FemProbabilisticEngine _engine = FemProbabilisticEngine.LatinHypercube;
    private int _sampleCount = 100;
    private double _designFactorOfSafety = 1.0;
    private int _randomSeed = 1;
    private bool _useFixedSeed = true;

    public FemProbabilisticEngine Engine
    {
        get => _engine;
        set { if (_engine == value) return; _engine = value; OnPropertyChanged(); }
    }

    public int SampleCount
    {
        get => _sampleCount;
        set { if (_sampleCount == value) return; _sampleCount = Math.Max(1, value); OnPropertyChanged(); }
    }

    public double DesignFactorOfSafety
    {
        get => _designFactorOfSafety;
        set { if (Math.Abs(_designFactorOfSafety - value) < 1e-15) return; _designFactorOfSafety = value; OnPropertyChanged(); }
    }

    public int RandomSeed
    {
        get => _randomSeed;
        set { if (_randomSeed == value) return; _randomSeed = value; OnPropertyChanged(); }
    }

    public bool UseFixedSeed
    {
        get => _useFixedSeed;
        set { if (_useFixedSeed == value) return; _useFixedSeed = value; OnPropertyChanged(); }
    }

    public int ResolveSeed() => UseFixedSeed ? RandomSeed : Environment.TickCount;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class FemBackAnalysisSettings : INotifyPropertyChanged
{
    private double _targetFs = 1.0;
    private double _fsTolerance = 1e-4;
    private int _maxIterations = 40;
    private double _minFactor = 0.05;
    private double _maxFactor = 20;

    public double TargetFs
    {
        get => _targetFs;
        set { if (Math.Abs(_targetFs - value) < 1e-15) return; _targetFs = value; OnPropertyChanged(); }
    }

    public double FsTolerance
    {
        get => _fsTolerance;
        set { if (Math.Abs(_fsTolerance - value) < 1e-15) return; _fsTolerance = value; OnPropertyChanged(); }
    }

    public int MaxIterations
    {
        get => _maxIterations;
        set { if (_maxIterations == value) return; _maxIterations = Math.Max(1, value); OnPropertyChanged(); }
    }

    public double MinFactor
    {
        get => _minFactor;
        set { if (Math.Abs(_minFactor - value) < 1e-15) return; _minFactor = value; OnPropertyChanged(); }
    }

    public double MaxFactor
    {
        get => _maxFactor;
        set { if (Math.Abs(_maxFactor - value) < 1e-15) return; _maxFactor = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class FemReportOptions : INotifyPropertyChanged
{
    private string _company = "";
    private string _reportTitle = "Finite Element Slope Stability Report";
    private string _client = "";
    private string _location = "";
    private string _documentNumber = "";
    private string _revision = "A";
    private string _preparedBy = "";
    private string _checkedBy = "";
    private string _notes = "";
    private bool _includeCoverPage = true;
    private bool _includeMaterials = true;
    private bool _includeGroundwater = true;
    private bool _includeSensitivity = true;
    private bool _includeProbabilistic = true;
    private bool _includeBackAnalysis = true;
    private bool _includeValidation = true;

    public string Company { get => _company; set { if (_company == value) return; _company = value; OnPropertyChanged(); } }
    public string ReportTitle { get => _reportTitle; set { if (_reportTitle == value) return; _reportTitle = value; OnPropertyChanged(); } }
    public string Client { get => _client; set { if (_client == value) return; _client = value; OnPropertyChanged(); } }
    public string Location { get => _location; set { if (_location == value) return; _location = value; OnPropertyChanged(); } }
    public string DocumentNumber { get => _documentNumber; set { if (_documentNumber == value) return; _documentNumber = value; OnPropertyChanged(); } }
    public string Revision { get => _revision; set { if (_revision == value) return; _revision = value; OnPropertyChanged(); } }
    public string PreparedBy { get => _preparedBy; set { if (_preparedBy == value) return; _preparedBy = value; OnPropertyChanged(); } }
    public string CheckedBy { get => _checkedBy; set { if (_checkedBy == value) return; _checkedBy = value; OnPropertyChanged(); } }
    public string Notes { get => _notes; set { if (_notes == value) return; _notes = value; OnPropertyChanged(); } }
    public bool IncludeCoverPage { get => _includeCoverPage; set { if (_includeCoverPage == value) return; _includeCoverPage = value; OnPropertyChanged(); } }
    public bool IncludeMaterials { get => _includeMaterials; set { if (_includeMaterials == value) return; _includeMaterials = value; OnPropertyChanged(); } }
    public bool IncludeGroundwater { get => _includeGroundwater; set { if (_includeGroundwater == value) return; _includeGroundwater = value; OnPropertyChanged(); } }
    public bool IncludeSensitivity { get => _includeSensitivity; set { if (_includeSensitivity == value) return; _includeSensitivity = value; OnPropertyChanged(); } }
    public bool IncludeProbabilistic { get => _includeProbabilistic; set { if (_includeProbabilistic == value) return; _includeProbabilistic = value; OnPropertyChanged(); } }
    public bool IncludeBackAnalysis { get => _includeBackAnalysis; set { if (_includeBackAnalysis == value) return; _includeBackAnalysis = value; OnPropertyChanged(); } }
    public bool IncludeValidation { get => _includeValidation; set { if (_includeValidation == value) return; _includeValidation = value; OnPropertyChanged(); } }

    public FemReportOptions Clone() => new()
    {
        Company = Company,
        ReportTitle = ReportTitle,
        Client = Client,
        Location = Location,
        DocumentNumber = DocumentNumber,
        Revision = Revision,
        PreparedBy = PreparedBy,
        CheckedBy = CheckedBy,
        Notes = Notes,
        IncludeCoverPage = IncludeCoverPage,
        IncludeMaterials = IncludeMaterials,
        IncludeGroundwater = IncludeGroundwater,
        IncludeSensitivity = IncludeSensitivity,
        IncludeProbabilistic = IncludeProbabilistic,
        IncludeBackAnalysis = IncludeBackAnalysis,
        IncludeValidation = IncludeValidation,
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class FemAnalysisCase : INotifyPropertyChanged
{
    private string _name = "Case 1";
    private bool _isActive = true;
    private double _seismicKh;
    private double _seismicKv;
    private double _loadFactor = 1.0;
    private string _notes = "";
    private double? _lastFoS;
    private string _status = "Not run";

    public string Name { get => _name; set { if (_name == value) return; _name = value; OnPropertyChanged(); } }
    public bool IsActive { get => _isActive; set { if (_isActive == value) return; _isActive = value; OnPropertyChanged(); } }
    public double SeismicKh { get => _seismicKh; set { if (Math.Abs(_seismicKh - value) < 1e-15) return; _seismicKh = value; OnPropertyChanged(); } }
    public double SeismicKv { get => _seismicKv; set { if (Math.Abs(_seismicKv - value) < 1e-15) return; _seismicKv = value; OnPropertyChanged(); } }
    public double LoadFactor { get => _loadFactor; set { if (Math.Abs(_loadFactor - value) < 1e-15) return; _loadFactor = value; OnPropertyChanged(); } }
    public string Notes { get => _notes; set { if (_notes == value) return; _notes = value; OnPropertyChanged(); } }
    public double? LastFoS { get => _lastFoS; set { if (_lastFoS == value) return; _lastFoS = value; OnPropertyChanged(); OnPropertyChanged(nameof(LastFoSText)); } }
    public string Status { get => _status; set { if (_status == value) return; _status = value; OnPropertyChanged(); } }
    public string LastFoSText => LastFoS is { } f && double.IsFinite(f) ? f.ToString("F3") : "—";

    public FemAnalysisCase Clone() => new()
    {
        Name = Name,
        IsActive = IsActive,
        SeismicKh = SeismicKh,
        SeismicKv = SeismicKv,
        LoadFactor = LoadFactor,
        Notes = Notes,
        LastFoS = LastFoS,
        Status = Status,
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class FemAnalysisCaseSet
{
    public ObservableCollection<FemAnalysisCase> Cases { get; } = new();

    public FemAnalysisCase AddDefault()
    {
        var item = new FemAnalysisCase { Name = $"Case {Cases.Count + 1}" };
        Cases.Add(item);
        return item;
    }
}
