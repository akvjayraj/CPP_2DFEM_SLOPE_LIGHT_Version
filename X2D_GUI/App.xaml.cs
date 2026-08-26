using System.Windows;
using System.Diagnostics;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Services.Appearance;

namespace GeotechSlopeApp;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

#if LOCKED_LICENSE
        // Hold the runtime in OnExplicitShutdown across the modal sign-in dialog. Without this,
        // closing LoginDialog (which WPF auto-promoted to MainWindow because it was the first
        // window opened) triggers OnLastWindowClose and the dispatcher posts a Shutdown that
        // fires before/around the StartupUri-driven MainWindow.xaml has a chance to open,
        // making the app silently exit after a successful sign-in.
        var savedShutdownMode = ShutdownMode;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        bool authorized;
        try
        {
            authorized = LicenseGate.TryAuthorize(this);
        }
        finally
        {
            ShutdownMode = savedShutdownMode;
        }

        if (!authorized)
        {
            // Gate has already called Shutdown(); skip base.OnStartup so WPF does not try to
            // open MainWindow.xaml after a Shutdown has been requested.
            return;
        }

        // The (now-closed) LoginDialog was implicitly assigned to Application.MainWindow.
        // Clearing it lets the next opened window (MainWindow.xaml from StartupUri) claim
        // the role automatically, and restores OnLastWindowClose semantics for the real app.
        MainWindow = null;
#endif

        UiAppearanceService.Instance.Load();
        base.OnStartup(e);
    }

    private static void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Debug.WriteLine("Unhandled UI exception: " + e.Exception);
        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nDetails have been written to the Output window.",
            "Application Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            Debug.WriteLine("Unhandled domain exception: " + ex);
            MessageBox.Show(
                $"An unexpected error occurred:\n\n{ex.Message}\n\nDetails have been written to the Output window.",
                "Application Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
