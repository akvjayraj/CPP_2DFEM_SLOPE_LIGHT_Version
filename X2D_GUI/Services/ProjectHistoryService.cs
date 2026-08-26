using System;
using System.Collections.Generic;
using GeotechSlopeApp.Persistence;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Services;

/// <summary>Snapshot-based project undo/redo history.</summary>
public sealed class ProjectHistoryService
{
    private const int MaxSnapshotSteps = 50;

    private readonly LinkedList<HistoryEntry> _undo = new();
    private readonly LinkedList<HistoryEntry> _redo = new();
    private int _suppressRecordingDepth;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public string UndoDescription => CanUndo ? _undo.Last!.Value.Description : string.Empty;
    public string RedoDescription => CanRedo ? _redo.Last!.Value.Description : string.Empty;

    public event EventHandler? HistoryChanged;

    public void BeginSuppressRecording() => _suppressRecordingDepth++;

    public void EndSuppressRecording()
    {
        if (_suppressRecordingDepth > 0)
            _suppressRecordingDepth--;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        RaiseHistoryChanged();
    }

    /// <summary>Capture current model state before a mutation (call immediately before the change).</summary>
    public void RecordSnapshot(MainWindowViewModel vm, string description)
    {
        if (_suppressRecordingDepth > 0 || vm == null)
            return;
        if (string.IsNullOrWhiteSpace(description))
            description = "Change";

        var snapshot = XfemProjectPersistence.CaptureModelSnapshot(vm, skipMaintenance: true);
        _undo.AddLast(new HistoryEntry(description.Trim(), snapshot));
        TrimOldest(_undo);
        _redo.Clear();
        RaiseHistoryChanged();
    }

    public void Undo(MainWindowViewModel vm)
    {
        if (!CanUndo)
            return;

        var entry = _undo.Last!.Value;
        _undo.RemoveLast();

        var current = XfemProjectPersistence.CaptureModelSnapshot(vm, skipMaintenance: true);
        _redo.AddLast(new HistoryEntry(entry.Description, current));
        TrimOldest(_redo);

        ApplyEntry(vm, entry);
        RaiseHistoryChanged();
    }

    public void Redo(MainWindowViewModel vm)
    {
        if (!CanRedo)
            return;

        var entry = _redo.Last!.Value;
        _redo.RemoveLast();

        var current = XfemProjectPersistence.CaptureModelSnapshot(vm, skipMaintenance: true);
        _undo.AddLast(new HistoryEntry(entry.Description, current));
        TrimOldest(_undo);

        ApplyEntry(vm, entry);
        RaiseHistoryChanged();
    }

    private void ApplyEntry(MainWindowViewModel vm, HistoryEntry entry)
    {
        try
        {
            BeginSuppressRecording();
            XfemProjectPersistence.ApplyModelSnapshot(vm, entry.Snapshot, preserveUiShell: true);
            vm.RefreshAllCanvasesAfterModelRestore();
        }
        finally
        {
            EndSuppressRecording();
        }
    }

    private static void TrimOldest(LinkedList<HistoryEntry> stack)
    {
        while (stack.Count > MaxSnapshotSteps)
            stack.RemoveFirst();
    }

    private void RaiseHistoryChanged() => HistoryChanged?.Invoke(this, EventArgs.Empty);

    private readonly record struct HistoryEntry(string Description, XfemProjectPersistence.XfemDocument Snapshot);
}
