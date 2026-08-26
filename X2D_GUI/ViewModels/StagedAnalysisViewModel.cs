using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.ViewModels;

/// <summary>
/// Drives the "Staged Analysis" subtab under FE Analysis. Wraps
/// <see cref="FEAnalysisViewModel.StageDefinitions"/> as the single source of truth,
/// layers in tunneling presets (NATM, TBM, top-heading/bench/invert, single face,
/// staged loading), adds validation + execution commands, and maintains a
/// live convergence log bound to the bottom panel.
/// <para>
/// Phase selection inside this subtab (<see cref="SelectedPhase"/>) is intentionally
/// independent of the footer stage tabs: this is a <b>run / configuration surface</b>,
/// not an editing scope. The footer still governs which stage owns newly-added loads
/// (handled by <see cref="LoadConditionsViewModel"/>).
/// </para>
/// </summary>
public class StagedAnalysisViewModel : INotifyPropertyChanged
{
    private readonly FEAnalysisViewModel _fe;
    private readonly StagedConstructionViewModel _staged;
    private readonly GeometryDefinitionViewModel _geometry;

    private StageDefinition? _selectedPhase;
    private string _inspectorKind = "Phase"; // Phase | Activity | Calculation | Convergence | Results
    private bool _isRunning;
    private string _lastRunSummary = "Not run.";
    private bool _stopRequested;
    private System.DateTime _runStartedAt;

    public StagedAnalysisViewModel(
        FEAnalysisViewModel fe,
        StagedConstructionViewModel staged,
        GeometryDefinitionViewModel geometry)
    {
        _fe = fe ?? throw new ArgumentNullException(nameof(fe));
        _staged = staged ?? throw new ArgumentNullException(nameof(staged));
        _geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));

        Presets = new ObservableCollection<StagedAnalysisPreset>(BuiltInPresets());
        InspectorKinds = new ObservableCollection<string>
        {
            "Phase", "Activity", "Calculation", "Convergence", "Results"
        };

        AddPhaseCommand        = new RelayCommand(AddPhase,        () => !IsRunning);
        RemovePhaseCommand     = new RelayCommand(RemovePhase,     () => !IsRunning && SelectedPhase != null);
        MoveUpCommand          = new RelayCommand(MoveUp,          () => !IsRunning && CanMoveUp());
        MoveDownCommand        = new RelayCommand(MoveDown,        () => !IsRunning && CanMoveDown());
        ApplyPresetCommand     = new RelayCommand<StagedAnalysisPreset>(ApplyPreset, p => !IsRunning && p != null);
        ValidateCommand        = new RelayCommand(Validate,        () => !IsRunning);
        RunAllCommand          = new RelayCommand(async () => await RunAsync(runAll: true,          untilSelected: false, selectedOnly: false), () => !IsRunning);
        RunToSelectedCommand   = new RelayCommand(async () => await RunAsync(runAll: false,         untilSelected: true,  selectedOnly: false), () => !IsRunning && SelectedPhase != null);
        RunSelectedOnlyCommand = new RelayCommand(async () => await RunAsync(runAll: false,         untilSelected: false, selectedOnly: true),  () => !IsRunning && SelectedPhase != null);
        ContinueCommand        = new RelayCommand(async () => await RunAsync(runAll: true,          untilSelected: false, selectedOnly: false, @continue: true), () => !IsRunning);
        RestartCommand         = new RelayCommand(Restart,         () => !IsRunning);
        StopCommand            = new RelayCommand(Stop,            () => IsRunning);
        ClearLogCommand        = new RelayCommand(() => ConvergenceLog.Clear());
        JumpToFooterTabCommand = new RelayCommand(JumpToFooterTab, () => SelectedPhase != null);
        SetInspectorCommand    = new RelayCommand<string>(s => { if (!string.IsNullOrWhiteSpace(s)) InspectorKind = s!; });
        AddActivateSurfaceCommand   = new RelayCommand<int?>(n => AddSurface(isActivate: true,  n), n => !IsRunning && SelectedPhase != null && n.HasValue);
        AddDeactivateSurfaceCommand = new RelayCommand<int?>(n => AddSurface(isActivate: false, n), n => !IsRunning && SelectedPhase != null && n.HasValue);
        RemoveActivateSurfaceCommand   = new RelayCommand<int?>(n => RemoveSurface(isActivate: true,  n), n => !IsRunning && SelectedPhase != null && n.HasValue);
        RemoveDeactivateSurfaceCommand = new RelayCommand<int?>(n => RemoveSurface(isActivate: false, n), n => !IsRunning && SelectedPhase != null && n.HasValue);

        _fe.StageDefinitions.CollectionChanged += OnStageDefinitionsChanged;
        _geometry.GeometrySurfaces.CollectionChanged += OnGeometrySurfacesChanged;
        RebuildAvailableSurfaces();

        if (_fe.StageDefinitions.Count > 0)
            SelectedPhase = _fe.StageDefinitions[0];
    }

    // ── Phase list ───────────────────────────────────────────────────────────
    public ObservableCollection<StageDefinition> Phases => _fe.StageDefinitions;

    public StageDefinition? SelectedPhase
    {
        get => _selectedPhase;
        set
        {
            if (ReferenceEquals(_selectedPhase, value)) return;
            _selectedPhase = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedPhase));
            OnPropertyChanged(nameof(SelectedPhaseDurationBarWidth));
        }
    }

    public bool HasSelectedPhase => _selectedPhase != null;

    public int PhaseCount => _fe.StageDefinitions.Count;

    /// <summary>Available surface numbers for the activate/deactivate pickers.</summary>
    public ObservableCollection<int> AvailableSurfaces { get; } = new();

    // ── Inspector + presets ──────────────────────────────────────────────────
    public ObservableCollection<string> InspectorKinds { get; }
    public ObservableCollection<StagedAnalysisPreset> Presets { get; }

    public string InspectorKind
    {
        get => _inspectorKind;
        set
        {
            if (_inspectorKind == value) return;
            _inspectorKind = value ?? "Phase";
            OnPropertyChanged();
        }
    }

    // ── Convergence log + run state ──────────────────────────────────────────
    public ObservableCollection<ConvergenceLogEntry> ConvergenceLog { get; } = new();

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (_isRunning == value) return;
            _isRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNotRunning));
            OnPropertyChanged(nameof(RunStatusLabel));
        }
    }

    public bool IsNotRunning => !IsRunning;

    public string RunStatusLabel => IsRunning
        ? $"Running… {(_runStartedAt == default ? "" : $"({(System.DateTime.Now - _runStartedAt).TotalSeconds:0.0} s)")}"
        : _lastRunSummary;

    public string LastRunSummary
    {
        get => _lastRunSummary;
        private set { _lastRunSummary = value; OnPropertyChanged(); OnPropertyChanged(nameof(RunStatusLabel)); }
    }

    /// <summary>Gantt-row width for the currently selected phase (0..200 px).</summary>
    public double SelectedPhaseDurationBarWidth
    {
        get
        {
            var p = _selectedPhase;
            if (p == null || p.Duration <= 0) return 40;
            var max = _fe.StageDefinitions.Count > 0 ? Math.Max(1.0, _fe.StageDefinitions.Max(x => x.Duration)) : 1.0;
            return 40 + 160 * (p.Duration / max);
        }
    }

    // ── Commands ─────────────────────────────────────────────────────────────
    public ICommand AddPhaseCommand { get; }
    public ICommand RemovePhaseCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand ApplyPresetCommand { get; }
    public ICommand ValidateCommand { get; }
    public ICommand RunAllCommand { get; }
    public ICommand RunToSelectedCommand { get; }
    public ICommand RunSelectedOnlyCommand { get; }
    public ICommand ContinueCommand { get; }
    public ICommand RestartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand JumpToFooterTabCommand { get; }
    public ICommand SetInspectorCommand { get; }
    public ICommand AddActivateSurfaceCommand { get; }
    public ICommand AddDeactivateSurfaceCommand { get; }
    public ICommand RemoveActivateSurfaceCommand { get; }
    public ICommand RemoveDeactivateSurfaceCommand { get; }

    // ── Static data ──────────────────────────────────────────────────────────
    public static readonly IReadOnlyList<string> SolverModeOptions = new List<string>
    {
        "Elastic",
        "Plastic",
        "Consolidation",
        "Safety",
    };

    public static readonly IReadOnlyList<string> PhaseTypeOptions = new List<string>
    {
        "Initial Stress",
        "Excavation",
        "Lining / Support",
        "Loading",
        "Consolidation",
        "Groundwater",
        "Safety (c-φ reduction)",
        "Reset Displacements",
    };

    public IReadOnlyList<string> SolverModeOptionsRef => SolverModeOptions;
    public IReadOnlyList<string> PhaseTypeOptionsRef => PhaseTypeOptions;

    // ── Phase CRUD ───────────────────────────────────────────────────────────
    private void AddPhase()
    {
        var next = _fe.StageDefinitions.Count == 0 ? 1 : _fe.StageDefinitions.Max(s => s.StageNumber) + 1;
        var phase = new StageDefinition
        {
            StageNumber = next,
            StageName = $"Phase {next}",
            StageType = next == 1 ? "Initial Stress" : "Excavation",
            SolverMode = next == 1 ? "Elastic" : "Plastic",
            StressReleaseLambda = 1.0,
            LoadFactor = 1.0,
            ResetDisplacementsAtStart = false,
        };
        _fe.StageDefinitions.Add(phase);
        if (!_fe.StagedExcavationEnabled)
            _fe.StagedExcavationEnabled = true;
        SelectedPhase = phase;
        Log("info", $"Added {phase.StageName}.");
    }

    private void RemovePhase()
    {
        if (_selectedPhase == null) return;
        var idx = _fe.StageDefinitions.IndexOf(_selectedPhase);
        var removed = _selectedPhase;
        _fe.StageDefinitions.Remove(_selectedPhase);
        RenumberPhases();
        SelectedPhase = _fe.StageDefinitions.Count > 0
            ? _fe.StageDefinitions[Math.Min(idx, _fe.StageDefinitions.Count - 1)]
            : null;
        Log("info", $"Removed {removed.StageName}.");
    }

    private bool CanMoveUp() => _selectedPhase != null && _fe.StageDefinitions.IndexOf(_selectedPhase) > 0;
    private bool CanMoveDown() => _selectedPhase != null
        && _fe.StageDefinitions.IndexOf(_selectedPhase) >= 0
        && _fe.StageDefinitions.IndexOf(_selectedPhase) < _fe.StageDefinitions.Count - 1;

    private void MoveUp()
    {
        if (_selectedPhase == null) return;
        var i = _fe.StageDefinitions.IndexOf(_selectedPhase);
        if (i <= 0) return;
        _fe.StageDefinitions.Move(i, i - 1);
        RenumberPhases();
    }

    private void MoveDown()
    {
        if (_selectedPhase == null) return;
        var i = _fe.StageDefinitions.IndexOf(_selectedPhase);
        if (i < 0 || i >= _fe.StageDefinitions.Count - 1) return;
        _fe.StageDefinitions.Move(i, i + 1);
        RenumberPhases();
    }

    private void RenumberPhases()
    {
        for (int i = 0; i < _fe.StageDefinitions.Count; i++)
            _fe.StageDefinitions[i].StageNumber = i + 1;
    }

    // ── Preset application ──────────────────────────────────────────────────
    private void ApplyPreset(StagedAnalysisPreset? preset)
    {
        if (preset == null) return;

        if (_fe.StageDefinitions.Count > 0)
        {
            var choice = System.Windows.MessageBox.Show(
                $"Apply preset \"{preset.Name}\"?\n\nThis will replace the current {_fe.StageDefinitions.Count} phase(s).",
                "Apply Preset", System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Question);
            if (choice != System.Windows.MessageBoxResult.OK) return;
        }

        _fe.StageDefinitions.Clear();
        int n = 1;
        foreach (var tmpl in preset.Phases)
        {
            var phase = new StageDefinition
            {
                StageNumber = n++,
                StageName = tmpl.Name,
                StageType = tmpl.Type,
                SolverMode = tmpl.SolverMode,
                StressReleaseLambda = tmpl.Lambda,
                LoadFactor = tmpl.LoadFactor,
                Duration = tmpl.Duration,
                Units = tmpl.Units,
                ResetDisplacementsAtStart = tmpl.Reset,
                MaxIterations = tmpl.MaxIterations,
                Tolerance = tmpl.Tolerance,
                MaxLoadSteps = tmpl.MaxLoadSteps,
                ArcLength = tmpl.ArcLength,
                LiningEnabled = tmpl.LiningEnabled,
                BoltsEnabled = tmpl.BoltsEnabled,
                Notes = tmpl.Notes,
                IsActive = true,
            };
            _fe.StageDefinitions.Add(phase);
        }

        if (!_fe.StagedExcavationEnabled)
            _fe.StagedExcavationEnabled = true;

        SelectedPhase = _fe.StageDefinitions.FirstOrDefault();
        Log("info", $"Preset \"{preset.Name}\" applied — {preset.Phases.Count} phase(s).");
    }

    // ── Validation ──────────────────────────────────────────────────────────
    private void Validate()
    {
        int errors = 0, warnings = 0;
        ConvergenceLog.Clear();

        if (_fe.StageDefinitions.Count == 0)
        {
            Log("error", "No phases defined. Add phases or apply a preset.");
            errors++;
        }

        for (int i = 0; i < _fe.StageDefinitions.Count; i++)
        {
            var p = _fe.StageDefinitions[i];
            if (string.IsNullOrWhiteSpace(p.StageName))
            {
                Log("warn", $"Phase {p.StageNumber} has no name.");
                warnings++;
            }
            if (p.StressReleaseLambda < 0 || p.StressReleaseLambda > 1)
            {
                Log("error", $"Phase {p.StageNumber}: λ={p.StressReleaseLambda:0.##} out of [0, 1].");
                errors++;
            }
            if (p.Tolerance <= 0)
            {
                Log("error", $"Phase {p.StageNumber}: tolerance must be > 0.");
                errors++;
            }
            if (p.MaxIterations < 1)
            {
                Log("error", $"Phase {p.StageNumber}: max iterations must be ≥ 1.");
                errors++;
            }
            if (p.StartsFromStageNumber.HasValue && p.StartsFromStageNumber.Value >= p.StageNumber)
            {
                Log("error", $"Phase {p.StageNumber}: \"Starts from\" must reference an earlier phase.");
                errors++;
            }
        }

        var msg = errors == 0 && warnings == 0
            ? "Validation passed."
            : $"Validation done — {errors} error(s), {warnings} warning(s).";
        Log(errors == 0 ? "converged" : "error", msg);
        LastRunSummary = msg;
    }

    // ── Execution ───────────────────────────────────────────────────────────
    private async System.Threading.Tasks.Task RunAsync(
        bool runAll,
        bool untilSelected,
        bool selectedOnly,
        bool @continue = false)
    {
        if (_fe.StageDefinitions.Count == 0)
        {
            Log("error", "No phases defined.");
            return;
        }

        _stopRequested = false;
        IsRunning = true;
        _runStartedAt = System.DateTime.Now;

        try
        {
            IEnumerable<StageDefinition> plan = _fe.StageDefinitions;
            if (!runAll && untilSelected && _selectedPhase != null)
                plan = _fe.StageDefinitions.TakeWhile(p => p.StageNumber <= _selectedPhase.StageNumber);
            else if (!runAll && selectedOnly && _selectedPhase != null)
                plan = new[] { _selectedPhase };

            Log("info", $"{(@continue ? "Continue" : "Start")} — {plan.Count()} phase(s).");
            if (_fe.StageDefinitions.Count > 0 && !_fe.StagedExcavationEnabled)
                _fe.StagedExcavationEnabled = true;
            _staged.SyncConstructionStagesFromFeAnalysis(_fe, true);

            _fe.StagedAnalysisLogger = entry => Log(entry.Level, entry.Message, entry.PhaseNumber);
            _fe.StagedAnalysisStopRequested = () => _stopRequested;
            _fe.StagedAnalysisRunPlan = plan.Select(p => p.StageNumber).ToList();
            try
            {
                await _fe.RunStagedAnalysisAsync();
            }
            finally
            {
                _fe.StagedAnalysisLogger = null;
                _fe.StagedAnalysisStopRequested = null;
                _fe.StagedAnalysisRunPlan = null;
            }

            if (_stopRequested)
            {
                Log("warn", "Run stopped by user.");
                LastRunSummary = "Stopped.";
            }
            else
            {
                Log("converged", "Run complete.");
                var elapsed = System.DateTime.Now - _runStartedAt;
                LastRunSummary = $"Completed {plan.Count()} phase(s) in {elapsed.TotalSeconds:0.0} s.";
            }
        }
        catch (Exception ex)
        {
            Log("error", $"Run failed: {ex.Message}");
            LastRunSummary = $"Error: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void Restart()
    {
        foreach (var p in _fe.StageDefinitions)
        {
            // Let FEAnalysis solver rebuild cumulative state from scratch next run.
        }
        ConvergenceLog.Clear();
        Log("info", "State cleared. Next Run will start from scratch.");
        LastRunSummary = "Restarted.";
    }

    private void Stop()
    {
        _stopRequested = true;
        Log("warn", "Stop requested — finishing current phase then halting.");
    }

    // ── Activity helpers ─────────────────────────────────────────────────────
    private void AddSurface(bool isActivate, int? n)
    {
        if (_selectedPhase == null || !n.HasValue) return;
        var list = isActivate ? _selectedPhase.ActivateSurfaceNumbers : _selectedPhase.DeactivateSurfaceNumbers;
        if (!list.Contains(n.Value)) list.Add(n.Value);
    }

    private void RemoveSurface(bool isActivate, int? n)
    {
        if (_selectedPhase == null || !n.HasValue) return;
        var list = isActivate ? _selectedPhase.ActivateSurfaceNumbers : _selectedPhase.DeactivateSurfaceNumbers;
        list.Remove(n.Value);
    }

    private void JumpToFooterTab()
    {
        if (_selectedPhase == null) return;
        if (!_fe.StagedExcavationEnabled)
            _fe.StagedExcavationEnabled = true;
        if (_staged.Stages.Count == 0 && _fe.StageDefinitions.Count > 0)
            _staged.SyncConstructionStagesFromFeAnalysis(_fe, true);

        // Users can manually set the footer tab selection; this helper surfaces the stage.
        _staged.SyncActiveStageFromFooter(_selectedPhase.StageNumber);
        Log("info", $"Footer stage synced to phase {_selectedPhase.StageNumber}.");
    }

    // ── Surface list maintenance ─────────────────────────────────────────────
    private void OnGeometrySurfacesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RebuildAvailableSurfaces();

    private void RebuildAvailableSurfaces()
    {
        AvailableSurfaces.Clear();
        foreach (var n in _geometry.GeometrySurfaces.Select(s => s.SurfaceNumber).OrderBy(x => x))
            AvailableSurfaces.Add(n);
    }

    private void OnStageDefinitionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(PhaseCount));
        OnPropertyChanged(nameof(SelectedPhaseDurationBarWidth));
        if (_selectedPhase != null && !_fe.StageDefinitions.Contains(_selectedPhase))
            SelectedPhase = _fe.StageDefinitions.FirstOrDefault();
    }

    // ── Logging ─────────────────────────────────────────────────────────────
    public void Log(string level, string message, int? phaseNumber = null)
    {
        var entry = new ConvergenceLogEntry
        {
            Timestamp = System.DateTime.Now,
            Level = string.IsNullOrWhiteSpace(level) ? "info" : level.ToLowerInvariant(),
            Message = message,
            PhaseNumber = phaseNumber
        };
        // Always append on UI thread; log rarely exceeds a few hundred entries per run.
        if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == false)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => ConvergenceLog.Add(entry)));
        }
        else
        {
            ConvergenceLog.Add(entry);
        }
    }

    // ── Presets library ─────────────────────────────────────────────────────
    private static List<StagedAnalysisPreset> BuiltInPresets() => new()
    {
        new StagedAnalysisPreset
        {
            Name = "NATM 3-face (Top heading / Bench / Invert)",
            Description = "Classic 3-face NATM: each face excavated with partial stress release, followed by shotcrete lining.",
            Phases = new()
            {
                new() { Name = "Initial Stress",             Type = "Initial Stress",     SolverMode = "Elastic", Lambda = 0.0, LoadFactor = 1.0, Notes = "K0 procedure." },
                new() { Name = "Top Heading — Excavation",   Type = "Excavation",         SolverMode = "Plastic", Lambda = 0.4, LoadFactor = 1.0, Duration = 1 },
                new() { Name = "Top Heading — Lining",       Type = "Lining / Support",   SolverMode = "Plastic", Lambda = 1.0, LoadFactor = 1.0, Duration = 1, LiningEnabled = true },
                new() { Name = "Bench — Excavation",         Type = "Excavation",         SolverMode = "Plastic", Lambda = 0.5, LoadFactor = 1.0, Duration = 1 },
                new() { Name = "Bench — Lining",             Type = "Lining / Support",   SolverMode = "Plastic", Lambda = 1.0, LoadFactor = 1.0, Duration = 1, LiningEnabled = true },
                new() { Name = "Invert — Excavation",        Type = "Excavation",         SolverMode = "Plastic", Lambda = 0.6, LoadFactor = 1.0, Duration = 1 },
                new() { Name = "Final Lining",               Type = "Lining / Support",   SolverMode = "Plastic", Lambda = 1.0, LoadFactor = 1.0, Duration = 2, LiningEnabled = true },
            }
        },
        new StagedAnalysisPreset
        {
            Name = "TBM shield advance",
            Description = "TBM: quick full-face excavation with segment lining installed inside the shield.",
            Phases = new()
            {
                new() { Name = "Initial Stress",       Type = "Initial Stress",   SolverMode = "Elastic", Lambda = 0.0, LoadFactor = 1.0 },
                new() { Name = "Full Face — Excavate", Type = "Excavation",       SolverMode = "Plastic", Lambda = 0.3, LoadFactor = 1.0, Duration = 1 },
                new() { Name = "Segmental Lining",     Type = "Lining / Support", SolverMode = "Plastic", Lambda = 1.0, LoadFactor = 1.0, Duration = 1, LiningEnabled = true },
                new() { Name = "Grouting / Close Up",  Type = "Loading",          SolverMode = "Plastic", Lambda = 1.0, LoadFactor = 1.0, Duration = 1 },
            }
        },
        new StagedAnalysisPreset
        {
            Name = "Single face (Full face)",
            Description = "Single-pass full-face excavation followed by lining.",
            Phases = new()
            {
                new() { Name = "Initial Stress",   Type = "Initial Stress",   SolverMode = "Elastic", Lambda = 0.0, LoadFactor = 1.0 },
                new() { Name = "Full Excavation",  Type = "Excavation",       SolverMode = "Plastic", Lambda = 0.5, LoadFactor = 1.0, Duration = 1 },
                new() { Name = "Lining",           Type = "Lining / Support", SolverMode = "Plastic", Lambda = 1.0, LoadFactor = 1.0, Duration = 1, LiningEnabled = true },
            }
        },
        new StagedAnalysisPreset
        {
            Name = "Staged loading (sequential surcharge)",
            Description = "Applies external loads in stepped increments.",
            Phases = new()
            {
                new() { Name = "Initial Stress",   Type = "Initial Stress", SolverMode = "Elastic", Lambda = 0.0, LoadFactor = 1.0 },
                new() { Name = "Load Step 1",      Type = "Loading",        SolverMode = "Plastic", Lambda = 1.0, LoadFactor = 0.33, Duration = 1 },
                new() { Name = "Load Step 2",      Type = "Loading",        SolverMode = "Plastic", Lambda = 1.0, LoadFactor = 0.67, Duration = 1 },
                new() { Name = "Load Step 3",      Type = "Loading",        SolverMode = "Plastic", Lambda = 1.0, LoadFactor = 1.00, Duration = 1 },
            }
        },
        new StagedAnalysisPreset
        {
            Name = "Safety (c-φ reduction) after construction",
            Description = "Construct tunnel, then reduce cohesion and friction to find factor of safety.",
            Phases = new()
            {
                new() { Name = "Initial Stress",   Type = "Initial Stress",     SolverMode = "Elastic", Lambda = 0.0, LoadFactor = 1.0 },
                new() { Name = "Excavation",       Type = "Excavation",         SolverMode = "Plastic", Lambda = 0.5, LoadFactor = 1.0, Duration = 1 },
                new() { Name = "Lining",           Type = "Lining / Support",   SolverMode = "Plastic", Lambda = 1.0, LoadFactor = 1.0, Duration = 1, LiningEnabled = true },
                new() { Name = "Safety FOS",       Type = "Safety (c-φ reduction)", SolverMode = "Safety",  Lambda = 1.0, LoadFactor = 1.0, Duration = 0, MaxLoadSteps = 50 },
            }
        },
    };

    // ── INotifyPropertyChanged ──────────────────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One preset bundle (named tunneling schedule) shown in the top-toolbar combo.</summary>
public class StagedAnalysisPreset
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<StagedAnalysisPhaseTemplate> Phases { get; set; } = new();
    public override string ToString() => Name;
}

public class StagedAnalysisPhaseTemplate
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "Excavation";
    public string SolverMode { get; set; } = "Plastic";
    public double Lambda { get; set; } = 1.0;
    public double LoadFactor { get; set; } = 1.0;
    public double Duration { get; set; } = 0;
    public string Units { get; set; } = "day";
    public bool Reset { get; set; }
    public int MaxIterations { get; set; } = 100;
    public double Tolerance { get; set; } = 1e-3;
    public int MaxLoadSteps { get; set; } = 10;
    public bool ArcLength { get; set; }
    public bool LiningEnabled { get; set; }
    public bool BoltsEnabled { get; set; }
    public string Notes { get; set; } = "";
}

/// <summary>Structured entry for the bottom convergence-log list.</summary>
public class ConvergenceLogEntry
{
    public System.DateTime Timestamp { get; set; }
    public string Level { get; set; } = "info"; // info | warn | error | converged
    public string Message { get; set; } = "";
    public int? PhaseNumber { get; set; }

    public string TimestampText => Timestamp.ToString("HH:mm:ss.fff");
    public string PhaseText => PhaseNumber.HasValue ? $"P{PhaseNumber.Value}" : "--";
}
