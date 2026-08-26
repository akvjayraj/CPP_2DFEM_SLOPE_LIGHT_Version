using System;
using System.Collections.Generic;
using System.IO;

namespace GeotechSlopeApp.Backend;

/// <summary>Locates canonical p64 verification decks relative to the repo or app directory.</summary>
public static class P64EmbankmentDeckPaths
{
    public const string Program64Relative = "Verification/slope/p64_embankment_program64.dat";

    public static string? ResolveProgram64Explicit()
    {
        foreach (var root in CandidateRepoRoots())
        {
            var path = Path.Combine(root, Program64Relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    private static IEnumerable<string> CandidateRepoRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string?[] seeds =
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        };

        foreach (var seed in seeds)
        {
            if (string.IsNullOrWhiteSpace(seed)) continue;
            var dir = Path.GetFullPath(seed);
            for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
            {
                if (seen.Add(dir))
                    yield return dir;
                dir = Directory.GetParent(dir)?.FullName ?? "";
            }
        }
    }
}
