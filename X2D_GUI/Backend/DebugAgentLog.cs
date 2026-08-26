using System;
using System.IO;
using System.Text.Json;

namespace GeotechSlopeApp.Backend;

/// <summary>NDJSON debug logger for agent debug sessions (session 5625fd).</summary>
internal static class DebugAgentLog
{
    private static string LogPath => Path.Combine(ResolveLogDirectory(), "debug-5625fd.log");

    private static string ResolveLogDirectory()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "X2D_GUI"))
                || File.Exists(Path.Combine(dir, "X2D_GUI", "GeotechSlopeApp.csproj")))
                return dir;

            var parent = Directory.GetParent(dir);
            if (parent == null)
                break;
            dir = parent.FullName;
        }

        return Directory.GetCurrentDirectory();
    }

    public static void Write(
        string hypothesisId,
        string location,
        string message,
        object? data = null,
        string runId = "pre-fix")
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                sessionId = "5625fd",
                hypothesisId,
                location,
                message,
                data,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                runId,
            });
            File.AppendAllText(LogPath, payload + Environment.NewLine);
        }
        catch
        {
            // ignore logging failures
        }
    }
}
