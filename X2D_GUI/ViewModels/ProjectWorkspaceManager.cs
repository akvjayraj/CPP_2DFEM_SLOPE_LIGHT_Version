using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using GeotechSlopeApp.Persistence;

namespace GeotechSlopeApp.ViewModels;

public sealed class ProjectWorkspaceSlot
{
    public string Label { get; set; } = "";
    public XfemProjectPersistence.XfemDocument Document { get; set; } = null!;
    public string? FilePath { get; set; }
}

/// <summary>
/// In-memory multi-project workspace: each footer tab holds a full <see cref="XfemProjectPersistence.XfemDocument"/> snapshot.
/// </summary>
public sealed class ProjectWorkspaceManager : INotifyPropertyChanged
{
    private int _activeIndex;
    private bool _isSwitching;

    public ObservableCollection<ProjectWorkspaceSlot> Slots { get; } = new();

    public bool IsSwitching => _isSwitching;

    public int ActiveIndex
    {
        get => _activeIndex;
        set
        {
            if (_activeIndex == value) return;
            _activeIndex = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Seeds the first workspace slot from the live VM state at startup.</summary>
    public void Initialize(MainWindowViewModel vm)
    {
        if (Slots.Count > 0) return;
        Slots.Add(new ProjectWorkspaceSlot
        {
            Label = "Project 1",
            Document = XfemProjectPersistence.CaptureSnapshot(vm),
            FilePath = vm.CurrentXfemProjectPath
        });
        ActiveIndex = 0;
        OnPropertyChanged(nameof(Slots));
    }

    public void CaptureActiveSlot(MainWindowViewModel vm)
    {
        if (ActiveIndex < 0 || ActiveIndex >= Slots.Count) return;
        var slot = Slots[ActiveIndex];
        slot.Document = XfemProjectPersistence.CaptureSnapshot(vm);
        slot.FilePath = vm.CurrentXfemProjectPath;
    }

    public void SwitchTo(MainWindowViewModel vm, int index)
    {
        if (_isSwitching || index < 0 || index >= Slots.Count || index == ActiveIndex)
            return;

        _isSwitching = true;
        try
        {
            CaptureActiveSlot(vm);

            var target = Slots[index];
            XfemProjectPersistence.ApplyDocument(vm, target.Document);
            vm.CurrentXfemProjectPath = target.FilePath;
            ActiveIndex = index;

            vm.SyncFooterWithStagedAnalysisGrid();
            vm.RestoreCurrentStageFromStageFooter();
        }
        finally
        {
            _isSwitching = false;
        }
    }

    public int AddBlankWorkspace(MainWindowViewModel vm)
    {
        CaptureActiveSlot(vm);

        var labelIndex = Slots.Count + 1;
        Slots.Add(new ProjectWorkspaceSlot
        {
            Label = $"Project {labelIndex}",
            Document = XfemProjectPersistence.CreateBlankDocument(),
            FilePath = null
        });
        OnPropertyChanged(nameof(Slots));

        var newIndex = Slots.Count - 1;
        XfemProjectPersistence.ApplyNewBlankProject(vm);
        vm.CurrentXfemProjectPath = null;
        ActiveIndex = newIndex;

        return newIndex;
    }

    /// <summary>Removes the active workspace slot and switches to another, or resets to a single blank project.</summary>
    public void CloseActiveWorkspace(MainWindowViewModel vm)
    {
        if (Slots.Count == 0) return;

        var closingIndex = ActiveIndex;
        Slots.RemoveAt(closingIndex);
        OnPropertyChanged(nameof(Slots));

        if (Slots.Count == 0)
        {
            Slots.Add(new ProjectWorkspaceSlot
            {
                Label = "Project 1",
                Document = XfemProjectPersistence.CreateBlankDocument(),
                FilePath = null
            });
            OnPropertyChanged(nameof(Slots));
            XfemProjectPersistence.ApplyNewBlankProject(vm);
            vm.CurrentXfemProjectPath = null;
            ActiveIndex = 0;
            return;
        }

        _isSwitching = true;
        try
        {
            var newIndex = closingIndex >= Slots.Count ? Slots.Count - 1 : closingIndex;
            var target = Slots[newIndex];
            XfemProjectPersistence.ApplyDocument(vm, target.Document);
            vm.CurrentXfemProjectPath = target.FilePath;
            ActiveIndex = newIndex;

            vm.SyncFooterWithStagedAnalysisGrid();
            vm.RestoreCurrentStageFromStageFooter();
        }
        finally
        {
            _isSwitching = false;
        }
    }

    /// <summary>After Open/Load, refresh the active slot snapshot and file path.</summary>
    public void UpdateActiveSlotFromVm(MainWindowViewModel vm, string? filePath)
    {
        if (ActiveIndex < 0 || ActiveIndex >= Slots.Count) return;
        var slot = Slots[ActiveIndex];
        slot.Document = XfemProjectPersistence.CaptureSnapshot(vm);
        slot.FilePath = filePath;
    }

    public void UpdateActiveSlotPath(string? path)
    {
        if (ActiveIndex < 0 || ActiveIndex >= Slots.Count) return;
        Slots[ActiveIndex].FilePath = path;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
