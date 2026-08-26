using System.Windows;

namespace GeotechSlopeApp.Services;

/// <summary>
/// Abstraction over modal message dialogs so view-model logic can run headlessly under test.
/// The production implementation forwards to <see cref="System.Windows.MessageBox"/>; tests swap in
/// a recording stub via <see cref="Dialogs.Service"/> so command paths that would otherwise pop a
/// blocking dialog return a scripted result instead of hanging the test runner.
/// </summary>
public interface IDialogService
{
    MessageBoxResult Show(string message);
    MessageBoxResult Show(string message, string caption);
    MessageBoxResult Show(string message, string caption, MessageBoxButton button);
    MessageBoxResult Show(string message, string caption, MessageBoxButton button, MessageBoxImage image);
}

/// <summary>Default WPF implementation; behaviour is identical to calling MessageBox.Show directly.</summary>
public sealed class WpfDialogService : IDialogService
{
    public MessageBoxResult Show(string message) => MessageBox.Show(message);

    public MessageBoxResult Show(string message, string caption) => MessageBox.Show(message, caption);

    public MessageBoxResult Show(string message, string caption, MessageBoxButton button)
        => MessageBox.Show(message, caption, button);

    public MessageBoxResult Show(string message, string caption, MessageBoxButton button, MessageBoxImage image)
        => MessageBox.Show(message, caption, button, image);
}

/// <summary>
/// Ambient accessor for the current dialog service. Defaults to <see cref="WpfDialogService"/>.
/// Replace <see cref="Service"/> in tests to capture or script dialog interactions.
/// (Named AppDialogs rather than Dialogs to avoid colliding with the GeotechSlopeApp.ViewModels.Dialogs namespace.)
/// </summary>
public static class AppDialogs
{
    public static IDialogService Service { get; set; } = new WpfDialogService();

    public static MessageBoxResult Show(string message) => Service.Show(message);

    public static MessageBoxResult Show(string message, string caption) => Service.Show(message, caption);

    public static MessageBoxResult Show(string message, string caption, MessageBoxButton button)
        => Service.Show(message, caption, button);

    public static MessageBoxResult Show(string message, string caption, MessageBoxButton button, MessageBoxImage image)
        => Service.Show(message, caption, button, image);
}
