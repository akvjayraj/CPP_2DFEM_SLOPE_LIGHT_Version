using System.Diagnostics;
using System.IO;
using System.Text.Json;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Verification;

internal static class Jr03Solver
{
    public static string? FindP610()
    {
        var root = FindRepoRoot();
        var names = new[]
        {
            Path.Combine(root, "library", "solver", "Structural-Static Elasticity", "p610_t3_elastoplastic.exe"),
            Path.Combine(root, "library", "solver", "p610_t3_elastoplastic.exe"),
            Path.Combine(root, "build", "Release", "p610_t3_elastoplastic.exe"),
            Path.Combine(root, "build", "Debug", "p610_t3_elastoplastic.exe")
        };
        return names.FirstOrDefault(File.Exists);
    }

    public static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CMakeLists.txt"))
                && Directory.Exists(Path.Combine(dir.FullName, "Verification")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", ".."));
    }

    public static JsonDocument RunJson(string exe, string work, string stem, int timeoutMs = 180_000)
    {
        var psi = new ProcessStartInfo(exe, stem)
        {
            WorkingDirectory = work,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("failed to start solver");
        if (!p.WaitForExit(timeoutMs))
        {
            try { p.Kill(true); } catch { /* ignore */ }
            Assert.Fail("p610 timed out");
        }
        var jsonPath = Path.Combine(work, stem + ".json");
        if (!File.Exists(jsonPath))
            Assert.Fail($"solver produced no JSON (exit {p.ExitCode})\n{p.StandardError.ReadToEnd()}\n{p.StandardOutput.ReadToEnd()}");
        return JsonDocument.Parse(File.ReadAllText(jsonPath));
    }

    public static JsonSerializerOptions XfemJsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
