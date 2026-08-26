using System;
using System.IO;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Integration.Support;

/// <summary>Locates the repository root and fixture decks for integration tests.</summary>
internal static class RepoFixtures
{
    /// <summary>Walks up from the test output folder to the repo root (folder containing CMakeLists.txt).</summary>
    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir != null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CMakeLists.txt"))
                && Directory.Exists(Path.Combine(dir.FullName, "Verification")))
                return dir.FullName;
        }
        Assert.Ignore("Repository root (CMakeLists.txt + Verification) not found; skipping file-dependent test.");
        return ""; // unreachable
    }

    /// <summary>Returns the absolute path of a repo-relative fixture, or Ignores the test if absent.</summary>
    public static string RequireFile(string relativePath)
    {
        var full = Path.Combine(RepoRoot(), relativePath);
        if (!File.Exists(full))
            Assert.Ignore($"Fixture not found on disk: {relativePath}");
        return full;
    }

    /// <summary>Reads a repo-relative fixture's text, or Ignores the test if absent.</summary>
    public static string ReadFixture(string relativePath) => File.ReadAllText(RequireFile(relativePath));
}
