using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Services;

namespace GeotechSlopeApp.ViewModels;

/// <summary>
/// Manages the full staged-construction workflow: stages, excavation mode,
/// undo/redo of excavation actions, and sequential FE stage solve orchestration.
/// Designed like RS2 / Plaxis: each stage inherits prior-stage stress state.
/// </summary>
public class StagedConstructionViewModel : INotifyPropertyChanged
{
    // ── State ─────────────────────────────────────────────────────────────────
    private GeometryDefinitionViewModel? _geometryVM;
    private FEAnalysisViewModel? _feAnalysis;
    private MeshViewModel? _meshVM;
    private ConstructionStage? _activeStage;
    private bool _isExcavateMode;
    private string _statusMessage = "No stages defined.";
    private IProjectUndoHost? _undoHost;

    public void SetUndoHost(IProjectUndoHost? host) => _undoHost = host;
    private void RecordUndo(string description) => _undoHost?.RecordUndoPoint(description);

    public void NotifyUndoCommandsChanged()
    {
        ((RelayCommand)UndoExcavationCommand).RaiseCanExecuteChanged();
        ((RelayCommand)RedoExcavationCommand).RaiseCanExecuteChanged();
    }

    // ── Playback ─────────────────────────────────────────────────────────────
    private int _playbackStageIndex = -1;
    private bool _isPlaybackRunning;

    // ── Overlay cache (cumulative excavation zones per stage) ─────────────────
    private readonly Dictionary<int, List<StageExcavationZone>> _overlayCache = new();

    // ── Stage type options (same as FEAnalysisViewModel) ─────────────────────
    public static readonly IReadOnlyList<string> StageTypeOptions = FEAnalysisViewModel.StageTypeOptions;

    public StagedConstructionViewModel()
    {
        Stages = new ObservableCollection<ConstructionStage>();
        Stages.CollectionChanged += (_, _) =>
        {
            InvalidateOverlayCache();
            RecomputeCumulativeExcavated();
        };

        ToggleExcavateModeCommand   = new RelayCommand(ToggleExcavateMode);
        UndoExcavationCommand       = new RelayCommand(() => _undoHost?.Undo(), () => _undoHost?.CanUndo == true);
        RedoExcavationCommand       = new RelayCommand(() => _undoHost?.Redo(), () => _undoHost?.CanRedo == true);
        AddStageCommand             = new RelayCommand(AddStage);
        RemoveActiveStageCommand    = new RelayCommand(RemoveActiveStage, () => _activeStage != null);
        MoveStageUpCommand          = new RelayCommand(MoveStageUp,   () => _activeStage != null && Stages.IndexOf(_activeStage) > 0);
        MoveStageDownCommand        = new RelayCommand(MoveStageDown, () => _activeStage != null && Stages.IndexOf(_activeStage) < Stages.Count - 1);
        GoToNextStageCommand        = new RelayCommand(GoToNextStage, () => ActiveStageIndex < Stages.Count - 1);
        GoToPrevStageCommand        = new RelayCommand(GoToPrevStage, () => ActiveStageIndex > 0);
        PlaybackCommand             = new RelayCommand(StartPlayback, () => Stages.Count > 0 && !_isPlaybackRunning);
        StopPlaybackCommand         = new RelayCommand(StopPlayback,  () => _isPlaybackRunning);
        ClearExcavationsCommand     = new RelayCommand(ClearAllExcavationsInActiveStage, () => _activeStage?.ExcavationZones.Count > 0);
    }

    // ── Public interface ──────────────────────────────────────────────────────

    /// <summary>Called by MainWindowViewModel to link the geometry canvas state.</summary>
    public void SetGeometryViewModel(GeometryDefinitionViewModel? vm)
    {
        _geometryVM = vm;
    }

    /// <summary>FE staged-analysis definitions drive construction stages when staged excavation is enabled.</summary>
    public void SetFeAnalysisViewModel(FEAnalysisViewModel? vm) => _feAnalysis = vm;

    /// <summary>
    /// Mesh tab ViewModel — supplies <see cref="MeshViewModel.MeshInformationRows"/> so
    /// <see cref="CommitExcavation"/> can derive the element IDs falling inside a freshly-clicked
    /// excavation polygon and surface them in the Materials &amp; Regions summary.
    /// </summary>
    public void SetMeshViewModel(MeshViewModel? vm) => _meshVM = vm;

    /// <summary>Returns the stage number for the footer tab currently selected (Material Properties / main window).</summary>
    public Func<int?>? ResolveFooterStageNumber { get; set; }

    /// <summary>
    /// Fired when one or more <see cref="ConstructionStage.ExcavatedElementIds"/> lists change
    /// (after .dat import, manual excavation commit, or stage rebuild). Subscribers like
    /// <c>MaterialPropertiesViewModel</c> rebuild their per-stage element-ID summary in response.
    /// </summary>
    public event EventHandler? ExcavatedElementIdsChanged;

    /// <summary>Fire <see cref="ExcavatedElementIdsChanged"/> from external mutators (e.g. ScriptViewModel after .dat load).</summary>
    public void NotifyExcavatedElementIdsChanged()
    {
        RecomputeCumulativeExcavated();
        ExcavatedElementIdsChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Stages ────────────────────────────────────────────────────────────────
    public ObservableCollection<ConstructionStage> Stages { get; }

    public ConstructionStage? ActiveStage
    {
        get => _activeStage;
        set
        {
            if (_activeStage == value) return;
            _activeStage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActiveStageIndex));
            OnPropertyChanged(nameof(ActiveStageNumber));
            OnPropertyChanged(nameof(ActiveStageName));
            OnPropertyChanged(nameof(ExcavatedSurfacesInAllPriorStages));
            RecomputeCumulativeExcavated();
            ((RelayCommand)RemoveActiveStageCommand).RaiseCanExecuteChanged();
            ((RelayCommand)MoveStageUpCommand).RaiseCanExecuteChanged();
            ((RelayCommand)MoveStageDownCommand).RaiseCanExecuteChanged();
            ((RelayCommand)GoToNextStageCommand).RaiseCanExecuteChanged();
            ((RelayCommand)GoToPrevStageCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ClearExcavationsCommand).RaiseCanExecuteChanged();
            RefreshExcavationOverlayOnCanvas();
            UpdateStatus();
        }
    }

    public int ActiveStageIndex => _activeStage == null ? -1 : Stages.IndexOf(_activeStage);
    public int ActiveStageNumber => _activeStage?.StageNumber ?? 0;
    public string ActiveStageName => _activeStage?.StageName ?? "(none)";

    /// <summary>All surface numbers excavated in stages PRIOR to the current active stage (cumulative).</summary>
    public HashSet<int> ExcavatedSurfacesInAllPriorStages
    {
        get
        {
            var result = new HashSet<int>();
            if (_activeStage == null) return result;
            foreach (var s in Stages.OrderBy(x => x.StageNumber))
            {
                if (s.StageNumber >= _activeStage.StageNumber) break;
                foreach (var n in s.ExcavatedSurfaceNumbers)
                    result.Add(n);
            }
            return result;
        }
    }

    /// <summary>All excavated surface numbers up to and including the active stage (cumulative).</summary>
    public HashSet<int> ExcavatedSurfacesUpToActiveStage
    {
        get
        {
            var result = new HashSet<int>();
            if (_activeStage == null) return result;
            foreach (var s in Stages.OrderBy(x => x.StageNumber))
            {
                foreach (var n in s.ExcavatedSurfaceNumbers)
                    result.Add(n);
                if (s.StageNumber >= _activeStage.StageNumber) break;
            }
            return result;
        }
    }

    private HashSet<int> _cumulativeExcavatedElementIds = new();

    /// <summary>
    /// Union of <see cref="ConstructionStage.ExcavatedElementIds"/> across every stage with
    /// <c>StageNumber</c> in <c>[2 .. ActiveStageNumber]</c>. Empty when <c>ActiveStageNumber &lt;= 1</c>
    /// or when only one stage exists overall (Initial Stress only — no excavation defined).
    ///
    /// Drives the stage-aware mesh hide on every wrapper that calls
    /// <c>MeshTabCanvasWrapper.EnableStageAwareFilter(...)</c>: an element whose ID is in this set
    /// is dropped from the converted mesh collection so the canvas doesn't draw it.
    ///
    /// The instance is replaced (not mutated) on every recompute, so subscribers get a stable
    /// snapshot per <see cref="INotifyPropertyChanged.PropertyChanged"/> notification.
    /// </summary>
    public IReadOnlySet<int> CumulativeExcavatedElementIdsUpToActiveStage => _cumulativeExcavatedElementIds;

    private void RecomputeCumulativeExcavated()
    {
        var next = new HashSet<int>();
        if (_activeStage != null && Stages.Count > 1)
        {
            int activeNum = _activeStage.StageNumber;
            foreach (var s in Stages)
            {
                // Stage 1 = Initial Stress (implicit, never excavates).
                if (s.StageNumber < 2) continue;
                if (s.StageNumber > activeNum) continue;
                foreach (var id in s.ExcavatedElementIds)
                    next.Add(id);
            }
        }

        // Replace the snapshot only when membership actually changed — avoids spurious wrapper
        // refreshes on transient sync churn.
        if (next.Count == _cumulativeExcavatedElementIds.Count
            && next.SetEquals(_cumulativeExcavatedElementIds))
        {
            // Footer stage may change while membership is unchanged; still sync row active flags.
            _meshVM?.SyncElementActiveStatesToExcavatedSet(next);
            return;
        }

        _cumulativeExcavatedElementIds = next;
        OnPropertyChanged(nameof(CumulativeExcavatedElementIdsUpToActiveStage));

        // Stage-aware display: later-stage excavations become visible again when viewing earlier stages.
        _meshVM?.SyncElementActiveStatesToExcavatedSet(next);
    }

    /// <summary>
    /// Recomputes excavation-derived element IDs against the current mesh and refreshes each stage.
    /// Called after mesh generation/re-meshing so surface excavations stay linked to live element IDs.
    /// </summary>
    public void RederiveSurfaceExcavationElementIds()
    {
        if (Stages.Count == 0) return;
        var meshRows = _meshVM?.MeshInformationRows;
        var validElementIds = meshRows is { Count: > 0 }
            ? meshRows.Select(r => r.ElementId).ToHashSet()
            : null;

        bool anyChanged = false;
        foreach (var stage in Stages)
        {
            bool hasZones = stage.ExcavationZones.Count > 0;
            bool hasSurfaces = stage.ExcavatedSurfaceNumbers.Count > 0;
            bool meshAvailable = meshRows is { Count: > 0 };
            bool canDeriveFromGeometry = (hasZones || hasSurfaces) && meshAvailable;
            bool preserveAuthoritativeIds = stage.ExcavatedElementIds.Count > 0 && !canDeriveFromGeometry;
            if (!hasZones && !hasSurfaces && !preserveAuthoritativeIds)
                continue;

            var ids = preserveAuthoritativeIds
                ? new HashSet<int>(stage.ExcavatedElementIds)
                : new HashSet<int>();
            int before = ids.Count;
            bool membershipChanged = false;

            foreach (var zone in stage.ExcavationZones)
            {
                foreach (var oldId in zone.DerivedElementIds)
                    membershipChanged |= ids.Remove(oldId);

                zone.DerivedElementIds =
                    meshRows is { Count: > 0 } && zone.AxisPolygon?.Count >= 3
                        ? PolygonElementMatcher.ElementIdsInsidePolygon(meshRows, zone.AxisPolygon)
                        : new List<int>();

                foreach (var newId in zone.DerivedElementIds)
                    membershipChanged |= ids.Add(newId);
            }

            foreach (var oldSurfaceId in stage.SurfaceDerivedElementIds)
                membershipChanged |= ids.Remove(oldSurfaceId);
            stage.SurfaceDerivedElementIds.Clear();

            if (hasSurfaces && meshRows is { Count: > 0 } && _geometryVM != null)
            {
                foreach (var sn in stage.ExcavatedSurfaceNumbers)
                {
                    if (!_geometryVM.TryGetSurfaceAxisPolygon(sn, out var axisPts) || axisPts.Count < 3)
                        continue;

                    var axisPoly = axisPts.Select(p => (p.X, p.Y)).ToList();
                    var derived = PolygonElementMatcher.ElementIdsInsidePolygon(meshRows, axisPoly);
                    stage.SurfaceDerivedElementIds.AddRange(derived);
                    foreach (var newId in derived)
                        membershipChanged |= ids.Add(newId);
                }
            }

            if (validElementIds != null)
            {
                var stale = ids.Where(id => !validElementIds.Contains(id)).ToList();
                foreach (var staleId in stale)
                    membershipChanged |= ids.Remove(staleId);
            }

            if (preserveAuthoritativeIds)
                continue;

            var ordered = ids.OrderBy(i => i).ToList();
            bool shouldWriteBack = canDeriveFromGeometry
                ? !stage.ExcavatedElementIds.SequenceEqual(ordered)
                : membershipChanged || ids.Count != before;

            if (shouldWriteBack)
            {
                stage.ExcavatedElementIds.Clear();
                stage.ExcavatedElementIds.AddRange(ordered);
                anyChanged = true;
            }
        }

        RecomputeCumulativeExcavated();
        _meshVM?.SyncElementActiveStatesToExcavatedSet(_cumulativeExcavatedElementIds);

        if (anyChanged)
            ExcavatedElementIdsChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Excavation mode ───────────────────────────────────────────────────────
    /// <summary>True while the user is in "Excavate" pick mode (cursor changed, next canvas click excavates).</summary>
    public bool IsExcavateMode
    {
        get => _isExcavateMode;
        private set
        {
            if (_isExcavateMode == value) return;
            _isExcavateMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExcavateModeButtonLabel));
            // Push mode into geometry canvas VM
            if (_geometryVM != null)
                _geometryVM.IsExcavationMode = value;
        }
    }

    public string ExcavateModeButtonLabel => IsExcavateMode ? "Cancel Excavate" : "Excavate";

    // ── Status ────────────────────────────────────────────────────────────────
    public string StatusMessage
    {
        get => _statusMessage;
        private set { _statusMessage = value; OnPropertyChanged(); }
    }

    // ── Commands ──────────────────────────────────────────────────────────────
    public ICommand ToggleExcavateModeCommand   { get; }
    public ICommand UndoExcavationCommand       { get; }
    public ICommand RedoExcavationCommand       { get; }
    public ICommand AddStageCommand             { get; }
    public ICommand RemoveActiveStageCommand    { get; }
    public ICommand MoveStageUpCommand          { get; }
    public ICommand MoveStageDownCommand        { get; }
    public ICommand GoToNextStageCommand        { get; }
    public ICommand GoToPrevStageCommand        { get; }
    public ICommand PlaybackCommand             { get; }
    public ICommand StopPlaybackCommand         { get; }
    public ICommand ClearExcavationsCommand     { get; }

    // ── Core: apply excavation from canvas click ──────────────────────────────
    /// <summary>
    /// Called by GeometryDefinitionViewModel.ExcavateAtPoint after it identifies which
    /// surface the user clicked. Commits the excavation to the active stage, records
    /// undo, and refreshes the canvas overlay.
    /// </summary>
    public void CommitExcavation(int surfaceNumber, PointCollection canvasPolygon,
                                  List<(double X, double Y)> axisPolygon,
                                  double centroidX, double centroidY)
    {
        EnsureActiveStageMatchesFooter();

        if (_activeStage == null)
        {
            StatusMessage = "No active stage. Select a footer stage or add stages in Project Settings.";
            return;
        }

        if (_activeStage.ExcavatedSurfaceNumbers.Contains(surfaceNumber))
        {
            StatusMessage = $"Surface S{surfaceNumber} is already excavated in Stage {_activeStage.StageNumber}.";
            return;
        }

        // Check if excavated in a prior stage
        if (ExcavatedSurfacesInAllPriorStages.Contains(surfaceNumber))
        {
            StatusMessage = $"Surface S{surfaceNumber} was already excavated in an earlier stage.";
            return;
        }

        RecordUndo("Excavate surface");

        var zone = new StageExcavationZone
        {
            SurfaceNumber        = surfaceNumber,
            AppliedInStageNumber = _activeStage.StageNumber,
            CanvasPolygon        = canvasPolygon,
            AxisPolygon          = axisPolygon,
            CentroidX            = centroidX,
            CentroidY            = centroidY,
            DisplayLabel         = $"Exc S{surfaceNumber}",
        };

        _activeStage.ExcavatedSurfaceNumbers.Add(surfaceNumber);
        _activeStage.ExcavationZones.Add(zone);
        InvalidateOverlayCache();

        // Derive element IDs whose centroids fall inside the polygon and merge them into
        // ExcavatedElementIds. This keeps the Materials & Regions per-stage summary live for
        // GUI-driven excavations (the .dat-import path populates this list directly).
        bool elementIdsChanged = false;
        if (_meshVM?.MeshInformationRows is { Count: > 0 } meshRows && axisPolygon?.Count >= 3)
        {
            var derived = PolygonElementMatcher.ElementIdsInsidePolygon(meshRows, axisPolygon);
            if (derived.Count > 0)
            {
                var existing = new HashSet<int>(_activeStage.ExcavatedElementIds);
                int before = existing.Count;
                foreach (var id in derived) existing.Add(id);
                if (existing.Count != before)
                {
                    _activeStage.ExcavatedElementIds.Clear();
                    _activeStage.ExcavatedElementIds.AddRange(existing.OrderBy(i => i));
                    elementIdsChanged = true;
                }
            }
        }

        // Record for undo — handled by global ProjectHistoryService via RecordUndo above.

        StatusMessage = $"Excavated S{surfaceNumber} in Stage {_activeStage.StageNumber} ({_activeStage.StageName}).";

        RefreshExcavationOverlayOnCanvas();
        OnPropertyChanged(nameof(ExcavatedSurfacesUpToActiveStage));

        if (elementIdsChanged)
        {
            RecomputeCumulativeExcavated();
            ExcavatedElementIdsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Mesh-element excavation flow (Mesh popup "Excavate Elements" mode).
    /// Adds the clicked element ID to the active stage's ExcavatedElementIds and
    /// deactivates the element in MeshViewModel so it disappears from active mesh draw.
    /// </summary>
    public bool CommitElementExcavationFromMesh(int elementId)
    {
        EnsureActiveStageMatchesFooter();
        if (_activeStage == null)
        {
            StatusMessage = "No active stage. Select a footer stage first.";
            return false;
        }

        if (_activeStage.StageNumber < 2)
        {
            StatusMessage = "Element excavation is only allowed from Stage 2 onward.";
            return false;
        }

        if (_activeStage.ExcavatedElementIds.Contains(elementId))
        {
            StatusMessage = $"Element {elementId} is already excavated in Stage {_activeStage.StageNumber}.";
            return false;
        }

        bool inPriorStage = Stages
            .Where(s => s.StageNumber < _activeStage.StageNumber)
            .Any(s => s.ExcavatedElementIds.Contains(elementId));
        if (inPriorStage)
        {
            StatusMessage = $"Element {elementId} was already excavated in an earlier stage.";
            return false;
        }

        RecordUndo("Excavate mesh element");
        _activeStage.ExcavatedElementIds.Add(elementId);
        _activeStage.ExcavatedElementIds.Sort();
        _meshVM?.SetElementActiveState(elementId, isActive: false, statusOverride: "Excavated");

        RecomputeCumulativeExcavated();
        ExcavatedElementIdsChanged?.Invoke(this, EventArgs.Empty);
        RefreshExcavationOverlayOnCanvas();
        OnPropertyChanged(nameof(ExcavatedSurfacesUpToActiveStage));
        StatusMessage = $"Excavated element {elementId} in Stage {_activeStage.StageNumber} ({_activeStage.StageName}).";
        return true;
    }

    // ── Stage management ──────────────────────────────────────────────────────
    private void ClearAllExcavationsInActiveStage()
    {
        if (_activeStage == null) return;
        RecordUndo("Clear stage excavations");
        foreach (var zone in _activeStage.ExcavationZones.ToList())
            _activeStage.ExcavatedSurfaceNumbers.Remove(zone.SurfaceNumber);
        _activeStage.ExcavationZones.Clear();
        InvalidateOverlayCache();
        ((RelayCommand)ClearExcavationsCommand).RaiseCanExecuteChanged();
        StatusMessage = $"Cleared all excavations from Stage {_activeStage.StageNumber}.";
        RefreshExcavationOverlayOnCanvas();
        OnPropertyChanged(nameof(ExcavatedSurfacesUpToActiveStage));
    }

    private void ToggleExcavateMode()
    {
        if (_feAnalysis?.StagedExcavationEnabled == true && _feAnalysis.StageDefinitions.Count > 0)
            SyncConstructionStagesFromFeAnalysis(_feAnalysis, true);
        else if (_activeStage == null)
        {
            if (Stages.Count == 0)
                AddStage();
            else
                ActiveStage = Stages[0];
        }

        EnsureActiveStageMatchesFooter();

        if (_activeStage == null)
        {
            GeotechSlopeApp.Services.AppDialogs.Show("Add at least one stage before excavating.",
                "Excavate", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        IsExcavateMode = !IsExcavateMode;
        StatusMessage = IsExcavateMode
            ? $"Excavate mode ON — click inside a surface region to excavate it (Stage {_activeStage.StageNumber})."
            : "Excavate mode OFF.";
    }

    private void AddStage()
    {
        var nextNum = Stages.Count == 0 ? 1 : Stages.Max(s => s.StageNumber) + 1;
        var stage = new ConstructionStage
        {
            StageNumber = nextNum,
            StageName   = nextNum == 1 ? "Initial Stress" : $"Stage {nextNum}",
            StageType   = nextNum == 1 ? "Initial Stress" : "Excavation",
        };
        Stages.Add(stage);
        ActiveStage = stage;
        UpdateStatus();
    }

    private void RemoveActiveStage()
    {
        if (_activeStage == null) return;
        var idx = Stages.IndexOf(_activeStage);
        Stages.Remove(_activeStage);
        RenumberStages();
        ActiveStage = Stages.Count > 0 ? Stages[Math.Max(0, Math.Min(idx, Stages.Count - 1))] : null;
        RefreshExcavationOverlayOnCanvas();
    }

    private void MoveStageUp()
    {
        if (_activeStage == null) return;
        var idx = Stages.IndexOf(_activeStage);
        if (idx <= 0) return;
        Stages.Move(idx, idx - 1);
        RenumberStages();
        OnPropertyChanged(nameof(ActiveStageIndex));
    }

    private void MoveStageDown()
    {
        if (_activeStage == null) return;
        var idx = Stages.IndexOf(_activeStage);
        if (idx < 0 || idx >= Stages.Count - 1) return;
        Stages.Move(idx, idx + 1);
        RenumberStages();
        OnPropertyChanged(nameof(ActiveStageIndex));
    }

    private void GoToNextStage()
    {
        var idx = ActiveStageIndex;
        if (idx < Stages.Count - 1)
            ActiveStage = Stages[idx + 1];
    }

    private void GoToPrevStage()
    {
        var idx = ActiveStageIndex;
        if (idx > 0)
            ActiveStage = Stages[idx - 1];
    }

    private void RenumberStages()
    {
        for (int i = 0; i < Stages.Count; i++)
            Stages[i].StageNumber = i + 1;
    }

    // ── Step-by-step playback ─────────────────────────────────────────────────
    private System.Windows.Threading.DispatcherTimer? _playbackTimer;

    private void StartPlayback()
    {
        if (Stages.Count == 0) return;
        _isPlaybackRunning = true;
        _playbackStageIndex = 0;
        ActiveStage = Stages[0];
        ((RelayCommand)PlaybackCommand).RaiseCanExecuteChanged();
        ((RelayCommand)StopPlaybackCommand).RaiseCanExecuteChanged();
        StatusMessage = $"Playback: Stage 1 / {Stages.Count}";

        _playbackTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1200)
        };
        _playbackTimer.Tick += OnPlaybackTick;
        _playbackTimer.Start();
    }

    private void OnPlaybackTick(object? sender, EventArgs e)
    {
        _playbackStageIndex++;
        if (_playbackStageIndex >= Stages.Count)
        {
            StopPlayback();
            StatusMessage = "Playback complete.";
            return;
        }
        ActiveStage = Stages[_playbackStageIndex];
        StatusMessage = $"Playback: Stage {_playbackStageIndex + 1} / {Stages.Count}";
    }

    private void StopPlayback()
    {
        _playbackTimer?.Stop();
        _playbackTimer = null;
        _isPlaybackRunning = false;
        ((RelayCommand)PlaybackCommand).RaiseCanExecuteChanged();
        ((RelayCommand)StopPlaybackCommand).RaiseCanExecuteChanged();
        StatusMessage = "Playback stopped.";
    }

    // ── Canvas overlay push ───────────────────────────────────────────────────
    /// <summary>
    /// Collects excavation zones for the active stage display and pushes them
    /// into <see cref="GeometryDefinitionViewModel.ExcavatedStageZones"/> for rendering.
    /// Normal surface zones remain cumulative. Tunnel Wizard zones are synthetic
    /// (<see cref="StageExcavationZone.SurfaceNumber"/> is negative) and are shown only
    /// on the stage where the tunnel excavation is applied.
    /// </summary>
    private void RefreshExcavationOverlayOnCanvas()
    {
        if (_geometryVM == null) return;

        var upTo = _activeStage?.StageNumber ?? int.MaxValue;
        if (!_overlayCache.TryGetValue(upTo, out var cached))
        {
            cached = new List<StageExcavationZone>();
            foreach (var stage in Stages.OrderBy(s => s.StageNumber))
            {
                if (stage.StageNumber > upTo) break;
                foreach (var zone in stage.ExcavationZones)
                {
                    if (zone.SurfaceNumber < 0 && zone.AppliedInStageNumber != upTo)
                        continue;
                    cached.Add(zone);
                }
            }
            _overlayCache[upTo] = cached;
        }

        if (OverlayMatchesCurrent(cached))
            return;

        _geometryVM.ExcavatedStageZones.Clear();
        foreach (var zone in cached)
            _geometryVM.ExcavatedStageZones.Add(zone);
    }

    private bool OverlayMatchesCurrent(IReadOnlyList<StageExcavationZone> cached)
    {
        var current = _geometryVM?.ExcavatedStageZones;
        if (current == null) return cached.Count == 0;
        if (current.Count != cached.Count) return false;
        for (int i = 0; i < cached.Count; i++)
        {
            if (!ReferenceEquals(current[i], cached[i]))
                return false;
        }
        return true;
    }

    private void InvalidateOverlayCache() => _overlayCache.Clear();

    /// <summary>Rebuild construction stages from FE stage definitions, preserving excavations by stage number.</summary>
    public void SyncConstructionStagesFromFeAnalysis(FEAnalysisViewModel fe, bool stagedExcavationEnabled)
    {
        InvalidateOverlayCache();

        if (!stagedExcavationEnabled || fe.StageDefinitions.Count == 0)
        {
            Stages.Clear();
            _activeStage = null;
            OnPropertyChanged(nameof(ActiveStage));
            OnPropertyChanged(nameof(ActiveStageIndex));
            OnPropertyChanged(nameof(ActiveStageNumber));
            OnPropertyChanged(nameof(ActiveStageName));
            OnPropertyChanged(nameof(ExcavatedSurfacesUpToActiveStage));
            RefreshExcavationOverlayOnCanvas();
            _geometryVM?.RefreshStageAwareExcavationDisplay();
            UpdateStatus();
            return;
        }

        var oldByNum = new Dictionary<int, ConstructionStage>();
        foreach (var s in Stages)
            oldByNum[s.StageNumber] = s;
        Stages.Clear();

        foreach (var d in fe.StageDefinitions.OrderBy(x => x.StageNumber))
        {
            var stage = new ConstructionStage
            {
                StageNumber = d.StageNumber,
                StageName = d.StageName,
                StageType = d.StageType
            };
            if (oldByNum.TryGetValue(d.StageNumber, out var prev))
            {
                foreach (var n in prev.ExcavatedSurfaceNumbers)
                    stage.ExcavatedSurfaceNumbers.Add(n);
                foreach (var z in prev.ExcavationZones)
                    stage.ExcavationZones.Add(z);
                if (prev.ExcavatedElementIds.Count > 0)
                    stage.ExcavatedElementIds.AddRange(prev.ExcavatedElementIds);
            }
            Stages.Add(stage);
        }

        SyncActiveStageFromFooter(ResolveFooterStageNumber?.Invoke());
        SyncBooleanSubtractExcavationsFromGeometry();
        RecomputeCumulativeExcavated();
        ((RelayCommand)ClearExcavationsCommand).RaiseCanExecuteChanged();
        UpdateStatus();
    }

    /// <summary>Match <see cref="ActiveStage"/> to the open footer tab (by stage number).</summary>
    public void SyncActiveStageFromFooter(int? footerStageNumber)
    {
        if (Stages.Count == 0)
        {
            if (_activeStage != null)
            {
                _activeStage = null;
                OnPropertyChanged(nameof(ActiveStage));
                OnPropertyChanged(nameof(ActiveStageIndex));
                OnPropertyChanged(nameof(ActiveStageNumber));
                OnPropertyChanged(nameof(ActiveStageName));
                RecomputeCumulativeExcavated();
            }
            RefreshExcavationOverlayOnCanvas();
            _geometryVM?.RefreshStageAwareExcavationDisplay();
            return;
        }

        ConstructionStage? next = null;
        if (footerStageNumber.HasValue)
            next = Stages.FirstOrDefault(s => s.StageNumber == footerStageNumber.Value);

        next ??= Stages.OrderBy(s => s.StageNumber).FirstOrDefault();

        if (!ReferenceEquals(_activeStage, next))
        {
            _activeStage = next;
            OnPropertyChanged(nameof(ActiveStage));
            OnPropertyChanged(nameof(ActiveStageIndex));
            OnPropertyChanged(nameof(ActiveStageNumber));
            OnPropertyChanged(nameof(ActiveStageName));
            OnPropertyChanged(nameof(ExcavatedSurfacesInAllPriorStages));
            OnPropertyChanged(nameof(ExcavatedSurfacesUpToActiveStage));
            RecomputeCumulativeExcavated();
            ((RelayCommand)RemoveActiveStageCommand).RaiseCanExecuteChanged();
            ((RelayCommand)MoveStageUpCommand).RaiseCanExecuteChanged();
            ((RelayCommand)MoveStageDownCommand).RaiseCanExecuteChanged();
            ((RelayCommand)GoToNextStageCommand).RaiseCanExecuteChanged();
            ((RelayCommand)GoToPrevStageCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ClearExcavationsCommand).RaiseCanExecuteChanged();
        }

        RefreshExcavationOverlayOnCanvas();
        _geometryVM?.RefreshStageAwareExcavationDisplay();
        UpdateStatus();
    }

    public void RefreshExcavationDisplay()
    {
        RefreshExcavationOverlayOnCanvas();
        _geometryVM?.RefreshStageAwareExcavationDisplay();
    }

    private void EnsureActiveStageMatchesFooter()
        => SyncActiveStageFromFooter(ResolveFooterStageNumber?.Invoke());

    /// <summary>
    /// First excavation construction stage: StageType Excavation, else StageNumber >= 2, else 2.
    /// </summary>
    public int ResolveDefaultExcavationStageNumber()
    {
        var excavation = Stages.FirstOrDefault(s =>
            string.Equals(s.StageType, "Excavation", StringComparison.OrdinalIgnoreCase));
        if (excavation != null)
            return excavation.StageNumber;

        var stage2Plus = Stages.FirstOrDefault(s => s.StageNumber >= 2);
        if (stage2Plus != null)
            return stage2Plus.StageNumber;

        return 2;
    }

    /// <summary>
    /// Registers subtracted surfaces from a boolean Excavate operation on the assigned construction stage.
    /// </summary>
    public void RegisterBooleanSubtractForStage(BooleanOperationRow row)
    {
        if (row.TypeEnum != BooleanOperationType.Subtract)
            return;

        int stageNum = row.AppliedInStageNumber > 0
            ? row.AppliedInStageNumber
            : ResolveDefaultExcavationStageNumber();

        var stage = Stages.FirstOrDefault(s => s.StageNumber == stageNum);
        if (stage == null)
            return;

        var surfacesToExcavate = GetSubtractExcavatedSurfaceNumbers(row);
        if (surfacesToExcavate.Count == 0)
            return;

        foreach (var n in surfacesToExcavate)
            stage.ExcavatedSurfaceNumbers.Add(n);

        if (_feAnalysis != null)
        {
            var phase = _feAnalysis.StageDefinitions.FirstOrDefault(d => d.StageNumber == stageNum);
            if (phase != null)
            {
                foreach (var n in surfacesToExcavate)
                {
                    if (!phase.DeactivateSurfaceNumbers.Contains(n))
                        phase.DeactivateSurfaceNumbers.Add(n);
                }
            }
        }

        RederiveSurfaceExcavationElementIds();
        OnPropertyChanged(nameof(ExcavatedSurfacesUpToActiveStage));
        RefreshExcavationOverlayOnCanvas();
    }

    /// <summary>
    /// Moves a boolean Excavate operation's surfaces off the previous stage and onto the row's current stage.
    /// </summary>
    public void ReassignBooleanSubtractStage(BooleanOperationRow row, int oldStageNumber)
    {
        if (row.TypeEnum != BooleanOperationType.Subtract)
            return;

        var surfaces = GetSubtractExcavatedSurfaceNumbers(row);
        if (surfaces.Count > 0 && oldStageNumber > 0)
        {
            var oldStage = Stages.FirstOrDefault(s => s.StageNumber == oldStageNumber);
            if (oldStage != null)
            {
                foreach (var n in surfaces)
                    oldStage.ExcavatedSurfaceNumbers.Remove(n);
            }

            if (_feAnalysis != null)
            {
                var oldPhase = _feAnalysis.StageDefinitions.FirstOrDefault(d => d.StageNumber == oldStageNumber);
                if (oldPhase != null)
                {
                    foreach (var n in surfaces)
                        oldPhase.DeactivateSurfaceNumbers.Remove(n);
                }
            }
        }

        RegisterBooleanSubtractForStage(row);
    }

    /// <summary>
    /// Links all boolean subtract rows on the geometry VM to construction stages, e.g. after staged excavation is enabled or project reload.
    /// </summary>
    public void SyncBooleanSubtractExcavationsFromGeometry()
    {
        if (_geometryVM == null || Stages.Count == 0)
            return;

        foreach (var row in _geometryVM.ExcavationOperations.Where(r => r.TypeEnum == BooleanOperationType.Subtract))
        {
            int stageNum = row.AppliedInStageNumber > 0
                ? row.AppliedInStageNumber
                : ResolveDefaultExcavationStageNumber();

            var stage = Stages.FirstOrDefault(s => s.StageNumber == stageNum);
            if (stage == null)
                continue;

            foreach (var n in GetSubtractExcavatedSurfaceNumbers(row))
                stage.ExcavatedSurfaceNumbers.Add(n);

            if (_feAnalysis != null)
            {
                var phase = _feAnalysis.StageDefinitions.FirstOrDefault(d => d.StageNumber == stageNum);
                if (phase != null)
                {
                    foreach (var n in GetSubtractExcavatedSurfaceNumbers(row))
                    {
                        if (!phase.DeactivateSurfaceNumbers.Contains(n))
                            phase.DeactivateSurfaceNumbers.Add(n);
                    }
                }
            }
        }

        RederiveSurfaceExcavationElementIds();
        OnPropertyChanged(nameof(ExcavatedSurfacesUpToActiveStage));
        RefreshExcavationOverlayOnCanvas();
    }

    private static List<int> GetSubtractExcavatedSurfaceNumbers(BooleanOperationRow row)
    {
        var result = new List<int>();
        if (row.TypeEnum != BooleanOperationType.Subtract)
            return result;

        if (row.IsMultiRegion && row.ParticipatingSurfaceNumbers.Count >= 2)
        {
            for (int i = 1; i < row.ParticipatingSurfaceNumbers.Count; i++)
                result.Add(row.ParticipatingSurfaceNumbers[i]);
        }
        else if (row.RegionBSurfaceNumber > 0)
        {
            result.Add(row.RegionBSurfaceNumber);
        }

        return result;
    }

    // ── Staged FE solve (stress handoff) ─────────────────────────────────────
    /// <summary>
    /// Returns cumulative excavated surface numbers up to (but not including) the
    /// given stage, so the solver can zero-out stiffness for those elements.
    /// </summary>
    public HashSet<int> GetExcavatedSurfacesBeforeStage(int stageNumber)
    {
        var result = new HashSet<int>();
        foreach (var s in Stages.OrderBy(x => x.StageNumber))
        {
            if (s.StageNumber >= stageNumber) break;
            foreach (var n in s.ExcavatedSurfaceNumbers)
                result.Add(n);
        }
        return result;
    }

    /// <summary>
    /// Returns all surface numbers excavated up to and including the given stage.
    /// </summary>
    public HashSet<int> GetExcavatedSurfacesThroughStage(int stageNumber)
    {
        var result = new HashSet<int>();
        foreach (var s in Stages.OrderBy(x => x.StageNumber))
        {
            foreach (var n in s.ExcavatedSurfaceNumbers)
                result.Add(n);
            if (s.StageNumber >= stageNumber) break;
        }
        return result;
    }

    private void UpdateStatus()
    {
        if (Stages.Count == 0)
            StatusMessage = "No stages. Click Add Stage.";
        else if (_activeStage == null)
            StatusMessage = $"{Stages.Count} stage(s). Select a stage.";
        else
            StatusMessage = $"Active: Stage {_activeStage.StageNumber} — {_activeStage.StageName} ({_activeStage.ExcavatedSurfaceNumbers.Count} excavation(s)).";
    }

    // ── INotifyPropertyChanged ────────────────────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
