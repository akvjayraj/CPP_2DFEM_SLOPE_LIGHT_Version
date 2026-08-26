using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Models.FiniteElement;
using OxyPlot;

namespace GeotechSlopeApp.ViewModels;

public partial class ResultsViewModel
{
    public const string TransientQuantityExcessPorePressure = "Excess Pore Pressure";
    public const string TransientQuantityDegreeOfConsolidation = "Degree of Consolidation U (%)";

    private int _transientPlotNodeId = 1;
    private int _lastValidTransientPlotNodeId = 1;
    private string _selectedTransientPlotQuantity = TransientQuantityExcessPorePressure;
    private string _transientPlotStatus = "";

    public ICommand PlayTransientCommand { get; private set; } = null!;
    public ICommand PauseTransientCommand { get; private set; } = null!;
    public ICommand StepTransientBackwardCommand { get; private set; } = null!;
    public ICommand StepTransientForwardCommand { get; private set; } = null!;
    public ICommand JumpTransientToStartCommand { get; private set; } = null!;
    public ICommand JumpTransientToEndCommand { get; private set; } = null!;

    public IReadOnlyList<string> AvailableTransientPlotQuantities { get; } =
        new[]
        {
            TransientQuantityExcessPorePressure,
            TransientQuantityDegreeOfConsolidation,
        };

    public int TransientPlotNodeId
    {
        get => _transientPlotNodeId;
        set
        {
            if (_transientPlotNodeId == value) return;
            _transientPlotNodeId = value;
            OnPropertyChanged();
            RebuildTransientTimeHistoryPlot();
        }
    }

    public string SelectedTransientPlotQuantity
    {
        get => _selectedTransientPlotQuantity;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? TransientQuantityExcessPorePressure
                : value;
            if (_selectedTransientPlotQuantity == normalized) return;
            _selectedTransientPlotQuantity = normalized;
            OnPropertyChanged();
            RebuildTransientTimeHistoryPlot();
        }
    }

    public string TransientPlotStatus
    {
        get => _transientPlotStatus;
        private set
        {
            if (_transientPlotStatus == value) return;
            _transientPlotStatus = value ?? "";
            OnPropertyChanged();
        }
    }

    public bool ShowTransientForceDisplacementPanel => HasTransientResults;

    /// <summary>True when Project Settings has Transient (p84/p88) selected.</summary>
    public bool IsTransientAnalysisSelected =>
        _projectSettings?.IsTransientP84Selected == true
        || _projectSettings?.IsTransientP88Selected == true;

    /// <summary>
    /// Force-Displacement: show Monitor node / Contour step / Contour intervals when Transient is selected.
    /// </summary>
    public bool ShowTransientOutputSettings => IsTransientAnalysisSelected;

    /// <summary>Shared transient solver settings (same instance as Project Settings / DAT export).</summary>
    public TransientAnalysisSettings? TransientSettings => _projectSettings?.TransientSettings;

    public PlotModel? TransientTimeHistoryPlotModel
    {
        get => _transientTimeHistoryPlotModel;
        private set
        {
            _transientTimeHistoryPlotModel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasTransientTimeHistoryPlot));
        }
    }

    public bool HasTransientTimeHistoryPlot => TransientTimeHistoryPlotModel != null;

    public int TransientFrameCount => _transientRunResult?.Steps.Count ?? 0;

    /// <summary>Slider maximum (last frame index).</summary>
    public int TransientTimeIndexMax => Math.Max(0, TransientFrameCount - 1);

    public int TransientTimeIndex
    {
        get => _transientTimeIndex;
        set
        {
            int clamped = TransientFrameCount == 0
                ? 0
                : Math.Clamp(value, 0, TransientFrameCount - 1);
            if (_transientTimeIndex == clamped) return;
            _transientTimeIndex = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentTransientTimeLabel));
            OnPropertyChanged(nameof(TransientProgressFraction));
            RefreshTransientPlaybackCommands();

            if (_transientRunResult == null || AvailableStages.Count == 0)
                return;

            if (clamped >= 0 && clamped < AvailableStages.Count
                && AvailableStages[clamped] is TransientTimeStepListItem item)
            {
                _suppressTransientIndexSync = true;
                try { SelectedStageInfo = item; }
                finally { _suppressTransientIndexSync = false; }
            }
        }
    }

    public double TransientProgressFraction
        => TransientFrameCount <= 1 ? 0.0 : (double)TransientTimeIndex / (TransientFrameCount - 1);

    public string CurrentTransientTimeLabel
    {
        get
        {
            if (_transientRunResult?.Steps is not { Count: > 0 })
                return "No transient frames";
            int i = Math.Clamp(TransientTimeIndex, 0, _transientRunResult.Steps.Count - 1);
            var frame = _transientRunResult.Steps[i];
            return $"t = {frame.Time:G6}   |   step {frame.Step} / {_transientRunResult.Nstep}   |   frame {i + 1}/{_transientRunResult.Steps.Count}";
        }
    }

    public bool IsTransientPlaying
    {
        get => _isTransientPlaying;
        private set
        {
            if (_isTransientPlaying == value) return;
            _isTransientPlaying = value;
            OnPropertyChanged();
            RefreshTransientPlaybackCommands();
        }
    }

    public TransientRunResult? LastTransientRun => _transientRunResult;

    /// <summary>
    /// Installs a multi-frame transient consolidation result (p84/p88) with time scrubber,
    /// Force-Displacement probe chart, and per-step excess pore-pressure contours.
    /// </summary>
    public void LoadTransientResult(TransientRunResult r, string? statusMessage = null)
    {
        if (r.Nodes.Count == 0 || r.Steps.Count == 0) return;

        OnPauseTransient();
        _transientRunResult = r;
        _seepageRunResult = r.AsSeepageSnapshot();
        _p610RunResult = null;
        _constructionStageSnapshots = null;
        _constructionBaseNodes = new List<Node>();
        _constructionBaseElements = new List<ElementBase>();
        _lastP610RunDatContent = null;
        _feMeshGeometrySource = null;
        FeMeshSmithEmb2dQ8LocalOrder = false;
        StagedAnalysisResult = null;
        SeepageOriginalDomainOutline = Array.Empty<(double X, double Y)>();

        var nodes = r.Nodes
            .OrderBy(n => n.Id)
            .Select(n => new Node { Id = n.Id, X = n.X, Y = n.Y, Displacements = new double[] { 0, 0 } })
            .ToList();
        var idToIndex = new Dictionary<int, int>(nodes.Count);
        for (int i = 0; i < nodes.Count; i++) idToIndex[nodes[i].Id] = i;

        var elements = new List<ElementBase>(r.Elements.Count);
        for (int e = 0; e < r.Elements.Count; e++)
        {
            var conn = r.Elements[e];
            var idx = conn.Select(id => idToIndex.TryGetValue(id, out var k) ? k : 0).ToArray();
            ElementBase el = conn.Length switch
            {
                8 => new QuadraticQuadrilateralElement(),
                6 => new QuadraticTriangleElement(),
                3 => new LinearTriangleElement(),
                _ => new LinearQuadrilateralElement(),
            };
            el.Id = e + 1;
            el.NodeIds = idx;
            elements.Add(el);
        }

        FEElements = elements;
        FENodes = nodes;
        FeNodalDisplacements = new List<object>();
        FeElementStresses = new List<ElementStress>();
        JointResults = Array.Empty<P610T3JointResult>();

        var list = new List<object>(r.Steps.Count);
        for (int i = 0; i < r.Steps.Count; i++)
        {
            var frame = r.Steps[i];
            double pMax = frame.Pressures.Count > 0 ? frame.Pressures.Max() : 0.0;
            double pMin = frame.Pressures.Count > 0 ? frame.Pressures.Min() : 0.0;
            double pMon = 0.0;
            int mon = Math.Max(1, r.MonitorNode);
            if (mon - 1 >= 0 && mon - 1 < frame.Pressures.Count)
                pMon = frame.Pressures[mon - 1];

            list.Add(new TransientTimeStepListItem
            {
                FrameIndex = i,
                Step = frame.Step,
                Time = frame.Time,
                DisplayName = $"t = {frame.Time:G5}",
                Summary = $"step {frame.Step}  |  P∈[{pMin:G4}, {pMax:G4}]  |  P@{mon}={pMon:G4}",
                StageResult = new P610T3StagedRowDetails(),
            });
        }

        AvailableStages = list;
        VisualizationMode = VisualizationModeContour2D;
        RefreshAvailableOutputTypes();
        SelectedOutputType = OutputTransientExcessPressure;

        int defaultNode = Math.Max(1, r.MonitorNode > 0 ? r.MonitorNode : 1);
        if (r.NodeCount > 0)
            defaultNode = Math.Clamp(defaultNode, 1, r.NodeCount);
        _transientPlotNodeId = defaultNode;
        _lastValidTransientPlotNodeId = defaultNode;
        _selectedTransientPlotQuantity = TransientQuantityExcessPorePressure;
        TransientPlotStatus = "";

        _transientTimeIndex = Math.Max(0, list.Count - 1);
        ApplyTransientFrame(_transientTimeIndex);
        RebuildTransientTimeHistoryPlot();

        if (!string.IsNullOrEmpty(statusMessage))
            FeStatusMessage = statusMessage;
        else
        {
            FeStatusMessage =
                $"Transient consolidation ({r.Solver}): {r.NodeCount} nodes, {r.ElementCount} elements, " +
                $"{r.Nstep} steps (Δt={r.Dtim:G4}, θ={r.Theta:G3}), {r.Steps.Count} spatial frames, " +
                $"monitor node {r.MonitorNode}.";
        }

        StageSummary = FeStatusMessage;
        OnPropertyChanged(nameof(HasTransientResults));
        OnPropertyChanged(nameof(HasStagedExcavationResults));
        OnPropertyChanged(nameof(HasStagedResults));
        OnPropertyChanged(nameof(ShowTransientForceDisplacementPanel));
        OnPropertyChanged(nameof(ShowSrfForceDisplacementPanel));
        OnPropertyChanged(nameof(ShowElasticForceDisplacementControls));
        OnPropertyChanged(nameof(TransientFrameCount));
        OnPropertyChanged(nameof(TransientTimeIndexMax));
        OnPropertyChanged(nameof(TransientTimeIndex));
        OnPropertyChanged(nameof(CurrentTransientTimeLabel));
        OnPropertyChanged(nameof(TransientProgressFraction));
        OnPropertyChanged(nameof(TransientPlotNodeId));
        OnPropertyChanged(nameof(SelectedTransientPlotQuantity));
        OnPropertyChanged(nameof(LastTransientRun));
        OnPropertyChanged(nameof(LastP610Run));
        RefreshTransientPlaybackCommands();

        _suppressTransientIndexSync = true;
        try { SelectedStageInfo = list[_transientTimeIndex]; }
        finally { _suppressTransientIndexSync = false; }

        FeResultsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyTransientFrame(int frameIndex)
    {
        if (_transientRunResult == null || _transientRunResult.Steps.Count == 0)
            return;

        int i = Math.Clamp(frameIndex, 0, _transientRunResult.Steps.Count - 1);
        var frame = _transientRunResult.Steps[i];
        var map = new Dictionary<int, SeepageNodeResult>();
        foreach (var n in _transientRunResult.Nodes)
        {
            double p = n.PressureHead;
            int idx = n.Id - 1;
            if (idx >= 0 && idx < frame.Pressures.Count)
                p = frame.Pressures[idx];
            map[n.Id] = new SeepageNodeResult
            {
                Id = n.Id,
                X = n.X,
                Y = n.Y,
                TotalHead = p,
                PressureHead = p,
                FlowRate = 0,
            };
        }

        _seepageNodesById = map;
        OnPropertyChanged(nameof(FENodes));
        OnPropertyChanged(nameof(SelectedOutputType));
        RebuildTransientTimeHistoryPlot(frame.Time);
    }

    private void RebuildTransientTimeHistoryPlot(double? currentTime = null)
    {
        if (_transientRunResult == null)
        {
            TransientTimeHistoryPlotModel = null;
            TransientPlotStatus = "";
            return;
        }

        int nodeCount = _transientRunResult.NodeCount > 0
            ? _transientRunResult.NodeCount
            : _transientRunResult.Nodes.Count;
        int requestedNode = TransientPlotNodeId;
        int plotNode = _lastValidTransientPlotNodeId;
        string status = "";

        if (nodeCount <= 0 || requestedNode < 1 || requestedNode > nodeCount)
        {
            status = $"Node {requestedNode} is out of range (valid 1…{Math.Max(1, nodeCount)}). Showing last valid node {plotNode}.";
        }
        else
        {
            plotNode = requestedNode;
            _lastValidTransientPlotNodeId = requestedNode;
        }

        if (!TryBuildNodeTimeSeries(plotNode, SelectedTransientPlotQuantity, out var series, out var quantityNote))
        {
            TransientPlotStatus = string.IsNullOrEmpty(status) ? quantityNote : status + " " + quantityNote;
            return;
        }

        TransientPlotStatus = string.IsNullOrEmpty(status)
            ? quantityNote
            : (string.IsNullOrEmpty(quantityNote) ? status : status + " " + quantityNote);

        double? t = currentTime;
        if (t == null && _transientRunResult.Steps.Count > 0)
        {
            int i = Math.Clamp(TransientTimeIndex, 0, _transientRunResult.Steps.Count - 1);
            t = _transientRunResult.Steps[i].Time;
        }

        GetQuantityAxisLabels(SelectedTransientPlotQuantity, out var quantityLabel, out var yAxis);
        TransientTimeHistoryPlotModel = TransientTimeHistoryPlotBuilder.Build(
            series,
            plotNode,
            quantityLabel,
            yAxis,
            t,
            $"{quantityLabel} — node {plotNode}");
    }

    /// <summary>Builds a probe series for tests and the Force-Displacement chart.</summary>
    public bool TryBuildNodeTimeSeries(
        int nodeId,
        string quantity,
        out IReadOnlyList<TransientTimeSeriesPoint> series,
        out string note)
    {
        series = Array.Empty<TransientTimeSeriesPoint>();
        note = "";
        if (_transientRunResult == null || _transientRunResult.Steps.Count == 0)
        {
            note = "No transient frames loaded.";
            return false;
        }

        int nodeCount = _transientRunResult.NodeCount > 0
            ? _transientRunResult.NodeCount
            : _transientRunResult.Nodes.Count;
        if (nodeId < 1 || nodeId > nodeCount)
        {
            note = $"Node {nodeId} is out of range.";
            return false;
        }

        bool preferMonitor =
            string.Equals(quantity, TransientQuantityExcessPorePressure, StringComparison.Ordinal);
        var pressureSeries = ExtractPressureSeries(nodeId, preferMonitorHistory: preferMonitor);
        if (pressureSeries.Count == 0)
        {
            note = $"No pressure samples for node {nodeId}.";
            return false;
        }

        if (string.Equals(quantity, TransientQuantityDegreeOfConsolidation, StringComparison.Ordinal))
        {
            double u0 = 0;
            bool found = false;
            foreach (var p in pressureSeries)
            {
                if (Math.Abs(p.Value) > 1e-12)
                {
                    u0 = p.Value;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                note = $"Degree of consolidation undefined for node {nodeId} (initial excess pressure ≈ 0).";
                series = pressureSeries.Select(p => new TransientTimeSeriesPoint(p.Time, 0.0)).ToList();
                return true;
            }

            series = pressureSeries
                .Select(p =>
                {
                    double u = 100.0 * (1.0 - p.Value / u0);
                    if (!double.IsFinite(u)) u = 0;
                    return new TransientTimeSeriesPoint(p.Time, u);
                })
                .ToList();
            note = "";
            return true;
        }

        // Default / Excess Pore Pressure
        series = pressureSeries;
        note = "";
        return true;
    }

    private List<TransientTimeSeriesPoint> ExtractPressureSeries(int nodeId, bool preferMonitorHistory)
    {
        var result = new List<TransientTimeSeriesPoint>();
        if (_transientRunResult == null) return result;

        bool useMonitorHistory =
            preferMonitorHistory
            && nodeId == Math.Max(1, _transientRunResult.MonitorNode)
            && _transientRunResult.MonitorHistory.Count > 0;

        if (useMonitorHistory)
        {
            foreach (var s in _transientRunResult.MonitorHistory.OrderBy(h => h.Time))
                result.Add(new TransientTimeSeriesPoint(s.Time, s.Pressure));
            return result;
        }

        int idx = nodeId - 1;
        foreach (var frame in _transientRunResult.Steps.OrderBy(f => f.Time))
        {
            if (idx < 0 || idx >= frame.Pressures.Count) continue;
            result.Add(new TransientTimeSeriesPoint(frame.Time, frame.Pressures[idx]));
        }

        return result;
    }

    private static void GetQuantityAxisLabels(string quantity, out string label, out string yAxis)
    {
        if (string.Equals(quantity, TransientQuantityDegreeOfConsolidation, StringComparison.Ordinal))
        {
            label = TransientQuantityDegreeOfConsolidation;
            yAxis = "Degree of consolidation U (%)";
            return;
        }

        label = TransientQuantityExcessPorePressure;
        yAxis = "Excess pore pressure";
    }

    private void ClearTransientResultState()
    {
        OnPauseTransient();
        _transientRunResult = null;
        TransientTimeHistoryPlotModel = null;
        _transientTimeIndex = 0;
        IsTransientPlaying = false;
        TransientPlotStatus = "";
        OnPropertyChanged(nameof(HasTransientResults));
        OnPropertyChanged(nameof(HasStagedExcavationResults));
        OnPropertyChanged(nameof(ShowTransientForceDisplacementPanel));
        OnPropertyChanged(nameof(ShowSrfForceDisplacementPanel));
        OnPropertyChanged(nameof(ShowElasticForceDisplacementControls));
        OnPropertyChanged(nameof(TransientFrameCount));
        OnPropertyChanged(nameof(TransientTimeIndexMax));
        OnPropertyChanged(nameof(TransientTimeIndex));
        OnPropertyChanged(nameof(CurrentTransientTimeLabel));
        OnPropertyChanged(nameof(TransientProgressFraction));
        OnPropertyChanged(nameof(LastTransientRun));
        RefreshTransientPlaybackCommands();
    }

    private void OnPlayTransient()
    {
        if (!HasTransientResults) return;
        if (TransientTimeIndex >= TransientFrameCount - 1)
            TransientTimeIndex = 0;

        _transientPlaybackTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(160),
        };
        _transientPlaybackTimer.Tick -= OnTransientPlaybackTick;
        _transientPlaybackTimer.Tick += OnTransientPlaybackTick;
        _transientPlaybackTimer.Start();
        IsTransientPlaying = true;
    }

    private void OnPauseTransient()
    {
        if (_transientPlaybackTimer != null)
        {
            _transientPlaybackTimer.Stop();
            _transientPlaybackTimer.Tick -= OnTransientPlaybackTick;
        }
        IsTransientPlaying = false;
    }

    private void OnTransientPlaybackTick(object? sender, EventArgs e)
    {
        if (!HasTransientResults)
        {
            OnPauseTransient();
            return;
        }

        if (TransientTimeIndex >= TransientFrameCount - 1)
        {
            OnPauseTransient();
            return;
        }

        TransientTimeIndex++;
    }

    private void RefreshTransientPlaybackCommands()
    {
        (PlayTransientCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (PauseTransientCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (StepTransientBackwardCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (StepTransientForwardCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (JumpTransientToStartCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (JumpTransientToEndCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }
}
