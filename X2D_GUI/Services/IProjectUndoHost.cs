namespace GeotechSlopeApp.Services;

/// <summary>Domain view-models call this before mutating project model state.</summary>
public interface IProjectUndoHost
{
    void RecordUndoPoint(string description);
    bool CanUndo { get; }
    bool CanRedo { get; }
    void Undo();
    void Redo();
}
