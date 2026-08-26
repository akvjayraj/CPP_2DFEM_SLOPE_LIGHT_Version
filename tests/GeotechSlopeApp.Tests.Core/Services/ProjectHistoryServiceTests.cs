using GeotechSlopeApp.Services;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Services;

/// <summary>
/// VM-independent invariants for the undo/redo history. Full snapshot round-trips that
/// require a constructed MainWindowViewModel live in the ViewModels (STA) test project.
/// </summary>
[TestFixture]
public class ProjectHistoryServiceTests
{
    [Test]
    public void NewHistory_CannotUndoOrRedo()
    {
        var history = new ProjectHistoryService();

        Assert.That(history.CanUndo, Is.False);
        Assert.That(history.CanRedo, Is.False);
        Assert.That(history.UndoDescription, Is.Empty);
        Assert.That(history.RedoDescription, Is.Empty);
    }

    [Test]
    public void RecordSnapshot_WithNullViewModel_DoesNotRecord()
    {
        var history = new ProjectHistoryService();

        history.RecordSnapshot(null!, "no-op");

        Assert.That(history.CanUndo, Is.False);
    }

    [Test]
    public void Clear_RaisesHistoryChanged()
    {
        var history = new ProjectHistoryService();
        bool raised = false;
        history.HistoryChanged += (_, _) => raised = true;

        history.Clear();

        Assert.That(raised, Is.True);
    }
}
