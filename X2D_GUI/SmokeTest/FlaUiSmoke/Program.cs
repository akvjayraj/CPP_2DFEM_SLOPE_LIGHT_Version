using System.IO;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace FlaUiSmoke;

internal static class Program
{
    private const string MainWindowTitle = "Geotechnical Slope Stability";

    private static int Main(string[] args)
    {
        Application? app = null;
        try
        {
            var exePath = args.Length > 0
                ? Path.GetFullPath(args[0])
                : ResolveDefaultAppExe();

            if (!File.Exists(exePath))
                throw new FileNotFoundException(
                    $"App executable not found: {exePath}. Build X2D_GUI/GeotechSlopeApp.csproj first.",
                    exePath);

            app = Application.Launch(exePath);
            using var automation = new UIA3Automation();

            var window = WaitForMainWindow(app, automation, TimeSpan.FromSeconds(30));
            Assert(window.Title == MainWindowTitle, $"Unexpected title: {window.Title}");
            Assert(window.IsAvailable, "Main window is not available.");

            var fileMenu = FindFirstByName(window, "File", ControlType.MenuItem)
                           ?? FindFirstByName(window, "_File", ControlType.MenuItem);
            Assert(fileMenu != null, "File menu item was not found.");

            var projectSettings = FindFirstByName(window, "Project Settings", ControlType.TabItem);
            var geometryDefinition = FindFirstByName(window, "Geometry Definition", ControlType.TabItem);
            var scriptEditor = FindFirstByName(window, "Script Editor", ControlType.TabItem);

            Assert(projectSettings != null, "Project Settings tab was not found.");
            Assert(geometryDefinition != null, "Geometry Definition tab was not found.");
            Assert(scriptEditor != null, "Script Editor tab was not found.");
            var scriptEditorTab = scriptEditor!.AsTabItem();

            scriptEditorTab.Select();
            Thread.Sleep(500);
            Assert(scriptEditorTab.Patterns.SelectionItem.Pattern.IsSelected.Value, "Script Editor tab did not become selected.");

            Console.WriteLine("OK FlaUI smoke: main window, menu, and primary tabs are accessible.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAILED FlaUI smoke:");
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            try
            {
                if (app is { HasExited: false })
                    app.Close();
            }
            catch
            {
                try { app?.Kill(); } catch { }
            }
        }
    }

    private static Window WaitForMainWindow(Application app, UIA3Automation automation, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? lastError = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var window = app.GetMainWindow(automation);
                if (window != null && window.Title == MainWindowTitle)
                    return window;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            Thread.Sleep(250);
        }

        throw new TimeoutException($"Timed out waiting for '{MainWindowTitle}'. Last error: {lastError?.Message}");
    }

    private static AutomationElement? FindFirstByName(Window window, string name, ControlType controlType)
    {
        var cf = window.Automation.ConditionFactory;
        return window.FindFirstDescendant(cf.ByControlType(controlType).And(cf.ByName(name)));
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static string ResolveDefaultAppExe()
    {
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory)
                       ?? FindRepoRoot(Directory.GetCurrentDirectory())
                       ?? throw new InvalidOperationException("Could not locate repository root.");

        return Path.Combine(repoRoot, "X2D_GUI", "bin", "Debug", "net8.0-windows", "GeotechSlopeApp.exe");
    }

    private static string? FindRepoRoot(string start)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(start));
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CMakeLists.txt"))
                && File.Exists(Path.Combine(dir.FullName, "X2D_GUI", "GeotechSlopeApp.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}
