using System;
using System.Collections.Generic;
using System.Windows.Threading;

namespace GeotechSlopeApp.Services;

/// <summary>
/// Debounces rapid property edits into a single undo snapshot (Phase 2 fine-grained helper).
/// </summary>
public sealed class CoalescingUndoRecorder
{
    private readonly Dictionary<string, string> _pendingDescriptions = new();
    private readonly DispatcherTimer _timer;
    private IProjectUndoHost? _host;

    public CoalescingUndoRecorder(int debounceMs = 300)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(debounceMs) };
        _timer.Tick += (_, _) => Flush();
    }

    public void SetHost(IProjectUndoHost? host) => _host = host;

    public void Request(string key, string description)
    {
        if (_host == null)
            return;
        _pendingDescriptions[key] = description;
        _timer.Stop();
        _timer.Start();
    }

    private void Flush()
    {
        _timer.Stop();
        if (_host == null || _pendingDescriptions.Count == 0)
            return;

        foreach (var description in _pendingDescriptions.Values)
            _host.RecordUndoPoint(description);
        _pendingDescriptions.Clear();
    }
}
