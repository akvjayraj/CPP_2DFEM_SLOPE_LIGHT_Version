namespace GeotechSlopeApp.Services;

/// <summary>Fine-grained undo step (Phase 2 — grid typing, vertex drag).</summary>
public interface IUndoableAction
{
    string Description { get; }
    void Undo();
    void Redo();
}
