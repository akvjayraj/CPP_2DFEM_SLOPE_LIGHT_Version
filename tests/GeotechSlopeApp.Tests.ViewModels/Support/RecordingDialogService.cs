using System.Collections.Generic;
using System.Windows;
using GeotechSlopeApp.Services;

namespace GeotechSlopeApp.Tests.ViewModels.Support;

/// <summary>
/// Test double for <see cref="IDialogService"/>: never shows a real (blocking) dialog. Records every
/// message and returns a scripted <see cref="MessageBoxResult"/> so view-model command paths that
/// would otherwise pop a MessageBox run to completion headlessly.
/// </summary>
public sealed class RecordingDialogService : IDialogService
{
    public List<string> Messages { get; } = new();

    /// <summary>Result returned from every Show overload (default Yes so confirmation paths proceed).</summary>
    public MessageBoxResult ResultToReturn { get; set; } = MessageBoxResult.Yes;

    public MessageBoxResult Show(string message) => Record(message);

    public MessageBoxResult Show(string message, string caption) => Record(message);

    public MessageBoxResult Show(string message, string caption, MessageBoxButton button) => Record(message);

    public MessageBoxResult Show(string message, string caption, MessageBoxButton button, MessageBoxImage image)
        => Record(message);

    private MessageBoxResult Record(string message)
    {
        Messages.Add(message);
        return ResultToReturn;
    }

    /// <summary>Installs a fresh recorder as the ambient dialog service and returns it.</summary>
    public static RecordingDialogService Install()
    {
        var recorder = new RecordingDialogService();
        AppDialogs.Service = recorder;
        return recorder;
    }
}
