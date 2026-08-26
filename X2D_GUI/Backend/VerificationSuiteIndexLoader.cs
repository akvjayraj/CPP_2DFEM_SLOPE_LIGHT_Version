using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Reads <c>Verification/_index.csv</c> from the repository tree (the master
/// suite-level summary written by <c>tools/build_all_*_packages.ps1</c>) and
/// turns each row into a <see cref="VerificationSuiteRow"/> with absolute paths
/// already resolved.
///
/// Search strategy: walk up from the GUI's base directory looking for a folder
/// containing <c>Verification\_index.csv</c>; this matches the pattern used by
/// <c>ScriptViewModel.FindRepoRoot</c> for resolving solver work directories.
/// When the GUI runs from outside the worktree (installed build) the loader
/// returns an empty list and the caller surfaces an explanatory status message
/// rather than throwing.
/// </summary>
public static class VerificationSuiteIndexLoader
{
    private const string IndexRelativePath = @"Verification\_index.csv";
    private const string VerificationFolderName = "Verification";

    public sealed class LoadResult
    {
        public IReadOnlyList<VerificationSuiteRow> Rows { get; init; } =
            Array.Empty<VerificationSuiteRow>();
        public string? IndexPath { get; init; }
        public string? RepoRoot { get; init; }
        public string? Error { get; init; }
    }

    public static LoadResult LoadFromBaseDirectory(string baseDir)
    {
        try
        {
            string? repoRoot = FindRepoRootContainingIndex(baseDir);
            if (repoRoot is null)
            {
                return new LoadResult
                {
                    Error = $"Could not find {IndexRelativePath} above '{baseDir}'. " +
                            "Run the GUI from inside the worktree, or build the verification suite first."
                };
            }

            string indexPath = Path.Combine(repoRoot, IndexRelativePath);
            if (!File.Exists(indexPath))
            {
                return new LoadResult
                {
                    Error = $"Index file not found at {indexPath}.",
                    RepoRoot = repoRoot
                };
            }

            string verificationRoot = Path.Combine(repoRoot, VerificationFolderName);
            var rows = ParseCsv(indexPath, verificationRoot);
            return new LoadResult
            {
                Rows = rows,
                IndexPath = indexPath,
                RepoRoot = repoRoot
            };
        }
        catch (Exception ex)
        {
            return new LoadResult { Error = "Failed to load verification index: " + ex.Message };
        }
    }

    private static string? FindRepoRootContainingIndex(string startDir)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(startDir));
        for (int i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, IndexRelativePath);
            if (File.Exists(candidate))
                return dir.FullName;
        }
        return null;
    }

    private static List<VerificationSuiteRow> ParseCsv(string indexPath, string verificationRoot)
    {
        var rows = new List<VerificationSuiteRow>();
        var lines = File.ReadAllLines(indexPath, Encoding.UTF8);
        if (lines.Length == 0) return rows;

        // First line is always the header. We tolerate column-order changes by
        // building a name->index lookup keyed by the literal column names from
        // tools/build_all_*_packages.ps1: Suite, Folder, Case, MeshType,
        // FailureCriterion, MaterialType, Verdict, Notes.
        var headers = SplitCsvLine(lines[0]);
        var col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Length; i++)
            col[headers[i]] = i;

        int? Idx(string name) => col.TryGetValue(name, out int v) ? v : null;
        int? iSuite = Idx("Suite");
        int? iFolder = Idx("Folder");
        int? iCase = Idx("Case");
        int? iMesh = Idx("MeshType");
        int? iFailure = Idx("FailureCriterion");
        int? iMaterial = Idx("MaterialType");
        int? iVerdict = Idx("Verdict");
        int? iNotes = Idx("Notes");

        for (int li = 1; li < lines.Length; li++)
        {
            string line = lines[li];
            if (string.IsNullOrWhiteSpace(line)) continue;

            var fields = SplitCsvLine(line);
            string suite = Read(fields, iSuite);
            string folder = Read(fields, iFolder);
            string caseName = Read(fields, iCase);

            // Resolve folder + dat paths once so the GUI can render an "Open"
            // button without re-walking the disk per row.
            string caseFolder = ResolveCaseFolder(verificationRoot, suite, folder);
            string datPath = ResolveDatPath(caseFolder, caseName);

            rows.Add(new VerificationSuiteRow
            {
                Suite = suite,
                Folder = folder,
                Case = caseName,
                MeshType = Read(fields, iMesh),
                FailureCriterion = Read(fields, iFailure),
                MaterialType = Read(fields, iMaterial),
                Verdict = Read(fields, iVerdict),
                Notes = Read(fields, iNotes),
                CaseFolderPath = caseFolder,
                DatFilePath = datPath
            });
        }
        return rows;
    }

    private static string Read(string[] fields, int? idx)
        => idx.HasValue && idx.Value < fields.Length ? fields[idx.Value] : "";

    private static string ResolveCaseFolder(string verificationRoot, string suite, string folder)
    {
        if (string.IsNullOrEmpty(suite)) return "";

        // The orchestrator emits suite names in two layouts:
        //   - Legacy depth-2: "salencon" -> Verification\salencon\
        //   - Categorized depth-3: "multi_element/salencon" -> Verification\multi_element\salencon\
        // The slash is what tools/run_all_verifications.ps1 writes; we split on
        // either '/' or '\' so the path resolution works on Windows and Linux.
        string[] suiteSegments = suite.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);

        // Build candidate roots in priority order so the first that exists wins:
        //   1) verificationRoot + suiteSegments + folder         (categorized + folder)
        //   2) verificationRoot + suiteSegments                  (categorized, no folder)
        //   3) verificationRoot + last(suiteSegments) + folder   (legacy depth-2 + folder)
        //   4) verificationRoot + last(suiteSegments)            (legacy depth-2)
        var candidates = new List<string>();
        var nestedSegs = new List<string> { verificationRoot };
        nestedSegs.AddRange(suiteSegments);
        if (!string.IsNullOrEmpty(folder)) candidates.Add(Path.Combine(nestedSegs.Concat(new[] { folder }).ToArray()));
        candidates.Add(Path.Combine(nestedSegs.ToArray()));

        if (suiteSegments.Length > 0)
        {
            string leaf = suiteSegments[^1];
            if (!string.IsNullOrEmpty(folder))
                candidates.Add(Path.Combine(verificationRoot, leaf, folder));
            candidates.Add(Path.Combine(verificationRoot, leaf));
        }

        foreach (var c in candidates)
        {
            if (Directory.Exists(c)) return c;
        }
        return "";
    }

    private static string ResolveDatPath(string caseFolder, string caseName)
    {
        if (string.IsNullOrEmpty(caseFolder) || string.IsNullOrEmpty(caseName)) return "";
        string candidate = Path.Combine(caseFolder, caseName + ".dat");
        return File.Exists(candidate) ? candidate : "";
    }

    /// <summary>
    /// Single-line CSV parser tuned for the stable schema the orchestrators
    /// emit: every field is double-quoted, embedded quotes are doubled per RFC
    /// 4180. We deliberately do not depend on a CSV library to avoid pulling
    /// new packages into the GUI for one read-only feature.
    /// </summary>
    private static string[] SplitCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }
        result.Add(current.ToString());
        return result.ToArray();
    }
}
