using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GeotechSlopeApp.Models.FiniteElement;

namespace GeotechSlopeApp.Backend;

/// <summary>Result of a <c>fem_cpp.exe</c> geotechnical run (JSON mesh + .res summary).</summary>
public sealed class FemCppRunResult
{
    public string ProgramId { get; init; } = "";
    public string BaseName { get; init; } = "";
    public string WorkDir { get; init; } = "";
    public string Stdout { get; init; } = "";
    public P610T3RunResult JsonResult { get; init; } = null!;
    public P64ResFileParser.P64ResSummary ResSummary { get; init; } = null!;
}

/// <summary>
/// Runs C++ FEM executables (p41, p51, etc.) as child processes and returns parsed results.
/// </summary>
public class CppAnalysisRunner
{
    private readonly P51DatFileWriter _datWriter = new();
    private readonly P51ResFileParser _resParser = new();
    private readonly P41ResFileParser _p41ResParser = new();

    /// <summary>Reads p51 companion diag file written next to the .res file when the native solver starts factorization.</summary>
    private static async Task<string?> TryReadP51DiagFileAsync(string workDir, string baseName, CancellationToken cancellationToken)
    {
        try
        {
            var path = Path.Combine(workDir, baseName + ".res.diag.txt");
            if (!File.Exists(path)) return null;
            return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static string NativeExitHint(int exitCode)
    {
        if (exitCode >= 0) return "";
        unchecked
        {
            var u = (uint)exitCode;
            return $"\n(Native process exited with 0x{u:X8}. This usually means an assertion/crash in the C++ solver — rebuild the solver from current sources.)";
        }
    }

    /// <summary>
    /// Formats a solver label for error messages. When branded and process executables differ,
    /// both names are shown.
    /// </summary>
    public static string FormatSolverErrorLabel(string processProgram, string? brandedProgram = null)
    {
        if (string.IsNullOrWhiteSpace(brandedProgram)
            || string.Equals(brandedProgram, processProgram, StringComparison.OrdinalIgnoreCase))
            return processProgram;
        return $"{brandedProgram} (process: {processProgram})";
    }

    /// <summary>Removes Fortran-style full-line <c>!</c> comments so p51's first token is the header, not <c>!</c>.</summary>
    internal static string StripFortranFullLineComments(string datContent)
    {
        if (string.IsNullOrEmpty(datContent)) return datContent;
        var sb = new StringBuilder(datContent.Length);
        foreach (var raw in datContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
        {
            var trimmed = raw.TrimStart();
            if (trimmed.Length > 0 && trimmed[0] == '!')
                continue;
            sb.Append(raw).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Path to the directory containing p51.exe (and other executables).
    /// Default: build directory relative to app, or current directory.
    /// </summary>
    public string ExecutableDirectory { get; set; } = "";

    /// <summary>
    /// Program name (e.g. "p51"). Will look for p51.exe on Windows.
    /// </summary>
    public string ProgramName { get; set; } = "p51";

    /// <summary>
    /// When true, resolve executables from the repo's library\solver folder.
    /// Used by Script Editor execution so loaded scripts always run the solver
    /// binaries beside their source files.
    /// </summary>
    public bool ForceSourceSolverDirectory { get; set; }

    /// <summary>
    /// Working directory for the run. .dat and .res files are written here.
    /// </summary>
    public string WorkingDirectory { get; set; } = "";

    /// <summary>
    /// Resolve the full path to the executable.
    /// </summary>
    public string GetExecutablePath()
    {
        if (ForceSourceSolverDirectory)
            return ResolveSourceSolverExecutablePath(ProgramName);

        // library\solver is the canonical home for solver executables. Dedicated
        // subfolders take precedence for p610 structural-static, T3/T6 SRF, and Q4/Q8 SRF runs.
        // Prefer it whenever
        // the exe exists there, so GUI-driven and Script Editor runs resolve identically.
        var canonical = TryResolveSourceSolverExecutable(ProgramName);
        if (canonical != null)
            return canonical;

        if (ProgramName.StartsWith("p610_", StringComparison.OrdinalIgnoreCase)
            || ProgramName.StartsWith("slope_stability_", StringComparison.OrdinalIgnoreCase))
            return ResolveSourceSolverExecutablePath(ProgramName);

        var dir = string.IsNullOrEmpty(ExecutableDirectory)
            ? AppDomain.CurrentDomain.BaseDirectory
            : ExecutableDirectory;
        var exe = ProgramName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? ProgramName : ProgramName + ".exe";
        return Path.Combine(dir, exe);
    }

    private static string ResolveSourceSolverExecutablePath(string programName)
    {
        var exe = programName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? programName : programName + ".exe";
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var preferredSolverFolder = GetPreferredSolverSubfolder(programName);

        // Deployed/release layout: library\solver lives next to the GUI .exe (the
        // locked-license release ships exactly this layout). Use it unconditionally
        // when present so we never accidentally walk up to a sibling repo's
        // CMakeLists.txt that happens to be in a parent folder.
        //
        // Dedicated subfolders (Q4-Q8 SRF, SRF, Structural-Static Elasticity) are checked FIRST
        // when applicable, so they are authoritative for their program families even
        // when a same-named copy also exists in the flat library\solver root.
        if (!string.IsNullOrWhiteSpace(preferredSolverFolder))
        {
            var deployedPreferred = Path.Combine(baseDir, "library", "solver", preferredSolverFolder, exe);
            if (File.Exists(deployedPreferred))
                return deployedPreferred;
        }

        var deployed = Path.Combine(baseDir, "library", "solver", exe);
        if (File.Exists(deployed))
            return deployed;

        var repoRoot = FindRepoRoot(baseDir);
        if (repoRoot != null)
        {
            // Dedicated subfolder first, then the canonical flat library\solver, then build
            // dirs as fallbacks for exes not yet synced there.
            var rels = string.IsNullOrWhiteSpace(preferredSolverFolder)
                ? new[] { @"library\solver", @"build\Release", @"build\Debug" }
                : new[] { Path.Combine("library", "solver", preferredSolverFolder), @"library\solver", @"build\Release", @"build\Debug" };
            foreach (var rel in rels)
            {
                var built = Path.Combine(repoRoot, rel, exe);
                if (File.Exists(built))
                    return built;
            }
        }

        if (!string.IsNullOrWhiteSpace(preferredSolverFolder))
        {
            return repoRoot == null
                ? Path.Combine(baseDir, "library", "solver", preferredSolverFolder, exe)
                : Path.Combine(repoRoot, "library", "solver", preferredSolverFolder, exe);
        }

        return repoRoot == null
            ? Path.Combine(baseDir, "library", "solver", exe)
            : Path.Combine(repoRoot, "library", "solver", exe);
    }

    private static string GetPreferredSolverSubfolder(string programName)
    {
        var name = Path.GetFileNameWithoutExtension(programName ?? "");
        if (string.Equals(name, "fem_cpp", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "p64", StringComparison.OrdinalIgnoreCase))
            return "Q4-Q8 SRF";
        if (name.StartsWith("slope_stability_Q4_", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "slope_stability_Q4_SRF", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("slope_stability_Q8_", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "slope_stability_Q8_SRF", StringComparison.OrdinalIgnoreCase))
            return "SRF";
        if (name.StartsWith("slope_stability_", StringComparison.OrdinalIgnoreCase)
            && (name.EndsWith("_SRF_axisym", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("_SRF", StringComparison.OrdinalIgnoreCase)))
            return "SRF";
        if (name.StartsWith("p610_", StringComparison.OrdinalIgnoreCase))
            return "Structural-Static Elasticity";
        return "";
    }

    private static string? TryResolveSourceSolverExecutable(string programName)
    {
        var candidate = ResolveSourceSolverExecutablePath(programName);
        return File.Exists(candidate) ? candidate : null;
    }

    private static string? FindRepoRoot(string startDir)
    {
        try
        {
            var dir = new DirectoryInfo(Path.GetFullPath(startDir));
            for (int i = 0; i < 8 && dir != null; ++i, dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "CMakeLists.txt")))
                    return dir.FullName;
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Run p51 analysis with the given input. Writes .dat, runs exe, parses .res.
    /// </summary>
    /// <param name="input">P51 input (mesh, BCs, loads)</param>
    /// <param name="baseName">Base name for .dat and .res files (e.g. "analysis_1")</param>
    /// <param name="cancellationToken">Optional cancellation</param>
    /// <returns>Parsed result with nodes, elements, stresses</returns>
    public async Task<P51ResFileParser.P51ParseResult> RunP51Async(
        P51DatFileWriter.P51Input input,
        string baseName = "p51_run",
        CancellationToken cancellationToken = default)
    {
        var workDir = string.IsNullOrEmpty(WorkingDirectory)
            ? Path.GetTempPath()
            : WorkingDirectory;

        var datPath = Path.Combine(workDir, baseName + ".dat");
        var resPath = Path.Combine(workDir, baseName + ".res");

        await using (var sw = new StreamWriter(datPath, false, Encoding.ASCII))
        {
            var buffer = new StringWriter();
            _datWriter.Write(buffer, input);
            await sw.WriteAsync(StripFortranFullLineComments(buffer.ToString())).ConfigureAwait(false);
        }

        var exePath = GetExecutablePath();
        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException(
                $"C++ executable not found: {exePath}\n\n" +
                "1) Build the C++ project (e.g. run build_cpp.bat or: cmake --build build --config Debug)\n" +
                "2) Place the solver exe in library\\solver (the canonical solver folder), or set FE executable folder to your build folder.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = baseName,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using (cancellationToken.Register(() =>
        {
            try { process.Kill(); } catch { }
        }))
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var diag = await TryReadP51DiagFileAsync(workDir, baseName, cancellationToken).ConfigureAwait(false);
            var diagBlock = string.IsNullOrWhiteSpace(diag) ? "" : $"\n--- p51 diag file ---\n{diag.TrimEnd()}\n";
            throw new InvalidOperationException(
                $"C++ analysis failed (exit code {process.ExitCode}).{NativeExitHint(process.ExitCode)}{diagBlock}\nStdout: {stdout}\nStderr: {stderr}");
        }

        if (!File.Exists(resPath))
        {
            throw new InvalidOperationException($"Result file not created: {resPath}");
        }

        return _resParser.Parse(resPath, datPath);
    }

    /// <summary>
    /// Run p51 with .dat text from the Script Editor (same format as <see cref="P51DatFileWriter"/> / p51.cpp).
    /// </summary>
    public async Task<P51ResFileParser.P51ParseResult> RunP51FromDatContentAsync(
        string datContent,
        string baseName = "p51_run",
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var workDir = string.IsNullOrEmpty(workingDirectory)
            ? Path.GetTempPath()
            : workingDirectory;

        var datPath = Path.Combine(workDir, baseName + ".dat");
        var resPath = Path.Combine(workDir, baseName + ".res");

        var sanitized = StripFortranFullLineComments(datContent);
        await File.WriteAllTextAsync(datPath, sanitized, cancellationToken).ConfigureAwait(false);

        var savedProgram = ProgramName;
        ProgramName = "p51";
        string exePath;
        try
        {
            exePath = GetExecutablePath();
        }
        finally
        {
            ProgramName = savedProgram;
        }

        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException(
                $"p51.exe not found: {exePath}\n\n" +
                "1) Build the C++ project (e.g. cmake --build build --config Release --target p51)\n" +
                "2) Place p51.exe in library\\solver (the canonical solver folder), or set FE executable folder to your build folder.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = baseName,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using (cancellationToken.Register(() =>
        {
            try { process.Kill(); } catch { }
        }))
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var diag = await TryReadP51DiagFileAsync(workDir, baseName, cancellationToken).ConfigureAwait(false);
            var diagBlock = string.IsNullOrWhiteSpace(diag) ? "" : $"\n--- p51 diag file ---\n{diag.TrimEnd()}\n";
            throw new InvalidOperationException(
                $"p51 failed (exit code {process.ExitCode}).{NativeExitHint(process.ExitCode)}{diagBlock}\nStdout: {stdout}\nStderr: {stderr}");
        }

        if (!File.Exists(resPath))
        {
            throw new InvalidOperationException($"Result file not created: {resPath}");
        }

        return _resParser.Parse(resPath, datPath);
    }

    /// <summary>
    /// Synchronous wrapper for RunP51Async.
    /// </summary>
    public P51ResFileParser.P51ParseResult RunP51(
        P51DatFileWriter.P51Input input,
        string baseName = "p51_run")
    {
        return RunP51Async(input, baseName).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Run p41 (1D rod) analysis: write .dat content to file, run p41.exe, parse .res.
    /// </summary>
    /// <param name="datContent">Full contents of the .dat file (e.g. from Script tab).</param>
    /// <param name="baseName">Base name for .dat and .res files (e.g. "p41_run").</param>
    /// <param name="workingDirectory">Directory for .dat/.res and exe working dir. If null, uses temp.</param>
    /// <param name="cancellationToken">Optional cancellation.</param>
    public async Task<P41ResFileParser.P41ParseResult> RunP41Async(
        string datContent,
        string baseName = "p41_run",
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var workDir = string.IsNullOrEmpty(workingDirectory)
            ? Path.GetTempPath()
            : workingDirectory;

        var datPath = Path.Combine(workDir, baseName + ".dat");
        var resPath = Path.Combine(workDir, baseName + ".res");

        await File.WriteAllTextAsync(datPath, datContent, cancellationToken).ConfigureAwait(false);

        var savedProgram = ProgramName;
        ProgramName = "p41";
        string exePath;
        try
        {
            exePath = GetExecutablePath();
        }
        finally
        {
            ProgramName = savedProgram;
        }

        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException(
                $"p41.exe not found: {exePath}\n\n" +
                "1) Build the C++ project (e.g. build_cpp.bat or cmake --build build --config Debug)\n" +
                "2) Place p41.exe in library\\solver (the canonical solver folder).");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = baseName,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        var tcs = new TaskCompletionSource<int>();
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => tcs.TrySetResult(process.ExitCode);

        process.Start();

        using (cancellationToken.Register(() =>
        {
            try { process.Kill(); } catch { }
            tcs.TrySetCanceled(cancellationToken);
        }))
        {
            await tcs.Task.ConfigureAwait(false);
        }

        if (process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
            var stdout = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                $"p41 failed (exit code {process.ExitCode}).\nStdout: {stdout}\nStderr: {stderr}");
        }

        if (!File.Exists(resPath))
        {
            throw new InvalidOperationException($"Result file not created: {resPath}");
        }

        return _p41ResParser.Parse(resPath, datPath);
    }

    /// <summary>
    /// Run 1D rod / script .dat: prefers unified <c>fem_cpp.exe</c> (same CLI as p41: <c>exe basename</c>),
    /// falls back to <c>p41.exe</c>. Parses <c>.res</c> with <see cref="P41ResFileParser"/>.
    /// </summary>
    public async Task<P41ResFileParser.P41ParseResult> RunScriptRodFemAsync(
        string datContent,
        string baseName = "fe_script_run",
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var workDir = string.IsNullOrEmpty(workingDirectory)
            ? Path.GetTempPath()
            : workingDirectory;

        var datPath = Path.Combine(workDir, baseName + ".dat");
        var resPath = Path.Combine(workDir, baseName + ".res");

        await File.WriteAllTextAsync(datPath, datContent, cancellationToken).ConfigureAwait(false);

        if (!TryResolveRodSolverExe(out var exePath, out var exeLabel))
        {
            throw new FileNotFoundException(
                "Neither fem_cpp.exe nor p41.exe was found.\n\n" +
                "Build the unified solver (cmake --build build --config Release --target fem_cpp)\n" +
                "and place fem_cpp.exe (or p41.exe) in library\\solver (the canonical solver folder),\n" +
                "or set the FE executable folder to your build\\Release directory.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = baseName,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        var tcs = new TaskCompletionSource<int>();
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => tcs.TrySetResult(process.ExitCode);

        process.Start();

        using (cancellationToken.Register(() =>
        {
            try { process.Kill(); } catch { }
            tcs.TrySetCanceled(cancellationToken);
        }))
        {
            await tcs.Task.ConfigureAwait(false);
        }

        if (process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
            var stdout = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                $"{exeLabel} failed (exit code {process.ExitCode}).\nStdout: {stdout}\nStderr: {stderr}");
        }

        if (!File.Exists(resPath))
        {
            throw new InvalidOperationException($"Result file not created: {resPath}");
        }

        return _p41ResParser.Parse(resPath, datPath);
    }

    /// <summary>
    /// Run <c>fem_cpp.exe</c> with geotechnical .dat content (p64, p65, …).
    /// Expects <c>{baseName}.json</c> (p610-compatible) and <c>{baseName}.res</c>.
    /// </summary>
    public async Task<FemCppRunResult> RunFemCppFromDatContentAsync(
        string programId,
        string datContent,
        string baseName = "fe_script_run",
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var workDir = string.IsNullOrEmpty(workingDirectory)
            ? Path.GetTempPath()
            : workingDirectory;
        Directory.CreateDirectory(workDir);

        var prepared = FemCppProgramRouter.EnsureProgramHeader(datContent, programId);

        var datPath = Path.Combine(workDir, baseName + ".dat");
        var resPath = Path.Combine(workDir, baseName + ".res");
        var jsonPath = Path.Combine(workDir, baseName + ".json");

        // Keep comment headers (! <solver:p64>, AnalysisOptions) so fem_cpp can resolve the driver.
        await File.WriteAllTextAsync(datPath, prepared, cancellationToken).ConfigureAwait(false);

        if (!TryResolveRodSolverExe(out var exePath, out var exeLabel))
        {
            throw new FileNotFoundException(
                "fem_cpp.exe was not found.\n\n" +
                "Build: cmake --build build --config Release --target fem_cpp\n" +
                "It is synced to library\\solver (the canonical solver folder) post-build;\n" +
                "alternatively set the FE executable folder to build\\Release.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = baseName,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using (cancellationToken.Register(() => { try { process.Kill(); } catch { } }))
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        var combined = string.IsNullOrEmpty(stderr) ? stdout : stdout + "\n" + stderr;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{exeLabel} ({programId}) failed (exit code {process.ExitCode}).{NativeExitHint(process.ExitCode)}\n{combined}");
        }

        if (!File.Exists(resPath))
            throw new InvalidOperationException($"Result file not created: {resPath}");

        if (!File.Exists(jsonPath))
            throw new InvalidOperationException(
                $"JSON result not created: {jsonPath}\n\n" +
                "Rebuild fem_cpp after enabling p64 JSON export in slope stability_Q8_SRF.cpp.");

        var jsonResult = P610T3JsonParser.ParseFile(jsonPath);
        var resSummary = P64ResFileParser.ParseFile(resPath);

        return new FemCppRunResult
        {
            ProgramId = programId,
            BaseName = baseName,
            WorkDir = workDir,
            Stdout = combined,
            JsonResult = jsonResult,
            ResSummary = resSummary
        };
    }

    /// <summary>Result of a seepage/transient run via fem_cpp: parsed JSON + raw .res text.</summary>
    public sealed class SeepageCppRunResult
    {
        public string ProgramId { get; init; } = "";
        public string WorkDir { get; init; } = "";
        public string Stdout { get; init; } = "";
        public string ResText { get; init; } = "";
        public SeepageRunResult Result { get; init; } = new();
        /// <summary>Populated for p84/p88 multi-frame transient consolidation JSON.</summary>
        public TransientRunResult? TransientResult { get; init; }
    }

    /// <summary>
    /// Run <c>fem_cpp.exe</c> with a steady-seepage .dat (first token p72/p73/p74) and
    /// parse the nodal-heads JSON output.
    /// </summary>
    public async Task<SeepageCppRunResult> RunSeepageFromDatContentAsync(
        string programId,
        string datContent,
        string baseName = "seepage_run",
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var workDir = string.IsNullOrEmpty(workingDirectory)
            ? Path.GetTempPath()
            : workingDirectory;
        Directory.CreateDirectory(workDir);

        var datPath = Path.Combine(workDir, baseName + ".dat");
        var resPath = Path.Combine(workDir, baseName + ".res");
        var jsonPath = Path.Combine(workDir, baseName + ".json");
        await File.WriteAllTextAsync(datPath, datContent, cancellationToken).ConfigureAwait(false);

        if (!TryResolveRodSolverExe(out var exePath, out _) || !exePath.EndsWith("fem_cpp.exe", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException(
                "fem_cpp.exe was not found (required for seepage programs p72/p73/p74).\n\n" +
                "Build: cmake --build build --config Release --target fem_cpp");

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = baseName,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using (cancellationToken.Register(() => { try { process.Kill(); } catch { } }))
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        var combined = string.IsNullOrEmpty(stderr) ? stdout : stdout + "\n" + stderr;

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"fem_cpp ({programId}) failed (exit code {process.ExitCode}).{NativeExitHint(process.ExitCode)}\n{combined}");
        if (!File.Exists(jsonPath))
            throw new InvalidOperationException($"Seepage JSON result not created: {jsonPath}\n{combined}");

        TransientRunResult? transient = null;
        SeepageRunResult seepage;
        if (TransientJsonParser.LooksLikeTransient(jsonPath))
        {
            transient = TransientJsonParser.ParseFile(jsonPath);
            seepage = transient.AsSeepageSnapshot();
        }
        else
        {
            seepage = SeepageJsonParser.ParseFile(jsonPath);
        }

        return new SeepageCppRunResult
        {
            ProgramId = programId,
            WorkDir = workDir,
            Stdout = combined,
            ResText = File.Exists(resPath) ? await File.ReadAllTextAsync(resPath, cancellationToken).ConfigureAwait(false) : "",
            Result = seepage,
            TransientResult = transient,
        };
    }

    /// <summary>Prefer fem_cpp.exe from library\solver (canonical), then build dirs / <see cref="ExecutableDirectory"/>; same order for p41.exe.</summary>
    private bool TryResolveRodSolverExe(out string exePath, out string label)
    {
        var baseDir = Path.GetFullPath(string.IsNullOrEmpty(ExecutableDirectory)
            ? AppDomain.CurrentDomain.BaseDirectory
            : ExecutableDirectory);
        var repoRoot = FindRepoRoot(baseDir);

        // library\solver is the canonical solver folder (CMake post-build syncs
        // fem_cpp.exe there), so it leads the candidate list. The newest-wins scan
        // below still protects against stale copies (GW JSON export requires a
        // recent fem_cpp build).
        var candidates = new List<string>();
        candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "library", "solver", "fem_cpp.exe"));
        if (repoRoot != null)
        {
            candidates.Add(Path.Combine(repoRoot, "library", "solver", "fem_cpp.exe"));
            candidates.Add(Path.Combine(repoRoot, "build", "Release", "fem_cpp.exe"));
            candidates.Add(Path.Combine(repoRoot, "build", "Debug", "fem_cpp.exe"));
        }

        foreach (var d in EnumerateFeSearchDirs(baseDir))
            candidates.Add(Path.Combine(d, "fem_cpp.exe"));

        string? newest = null;
        var newestTime = DateTime.MinValue;
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(candidate))
                continue;

            var writeTime = File.GetLastWriteTimeUtc(candidate);
            if (writeTime <= newestTime)
                continue;

            newestTime = writeTime;
            newest = candidate;
        }

        if (newest != null)
        {
            exePath = newest;
            label = "fem_cpp";
            return true;
        }

        var canonicalP41 = TryResolveSourceSolverExecutable("p41");
        if (canonicalP41 != null)
        {
            exePath = canonicalP41;
            label = "p41";
            return true;
        }

        foreach (var d in EnumerateFeSearchDirs(baseDir))
        {
            var p41 = Path.Combine(d, "p41.exe");
            if (File.Exists(p41))
            {
                exePath = p41;
                label = "p41";
                return true;
            }
        }

        exePath = "";
        label = "";
        return false;
    }

    private static IEnumerable<string> EnumerateFeSearchDirs(string primaryDir)
    {
        yield return primaryDir;
        yield return Path.Combine(primaryDir, "Release");
        yield return Path.Combine(primaryDir, "Debug");
    }

    /// <summary>Synchronous wrapper for RunP41Async.</summary>
    public P41ResFileParser.P41ParseResult RunP41(
        string datContent,
        string baseName = "p41_run",
        string? workingDirectory = null)
    {
        return RunP41Async(datContent, baseName, workingDirectory).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Run p610_t3_elastoplastic (elasto-plastic T3: staged excavation OR infinite-medium
    /// Mohr-Coulomb) with .dat text from the Script Editor. Writes .dat, runs
    /// p610_t3_elastoplastic.exe, reads the .json result file (may contain multiple stages).
    /// The on-disk exe basename mirrors the source file (p610_t3_elastoplastic.cpp);
    /// the in-file routing tag `! &lt;solver:p610_t3&gt;` is independent and unchanged.
    /// </summary>
    public async Task<P610T3RunResult> RunP610T3FromDatContentAsync(
        string datContent,
        string baseName = "p610t3_run",
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
        => await RunP610T3FamilyAsync("p610_t3_elastoplastic", datContent, baseName, workingDirectory, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Run p610_t3_elastic (linear-elastic T3, far-field traction BCs) with .dat text from the
    /// Script Editor. Output JSON has the same shape as p610_t3 so we reuse <see cref="P610T3JsonParser"/>.
    /// </summary>
    public async Task<P610T3RunResult> RunP610T3ElasticFromDatContentAsync(
        string datContent,
        string baseName = "p610t3_elastic_run",
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
        => await RunP610T3FamilyAsync("p610_t3_elastic", datContent, baseName, workingDirectory, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Run any p610_t3-family executable that emits the common JSON schema.
    /// </summary>
    public async Task<P610T3RunResult> RunP610T3VariantFromDatContentAsync(
        string program,
        string datContent,
        string baseName = "p610t3_variant_run",
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        datContent = P610AnalysisOptionsHelper.PrepareDatForNativeSlopeSrfExecutable(datContent, program);
        datContent = DatSolverControlExporter.SanitizeP610DatForSolver(datContent);
        // #region agent log
        DebugAgentLog.Write("H1", "CppAnalysisRunner.cs:RunP610T3VariantFromDatContentAsync",
            "Prepared dat solver control",
            DebugSrfDatProbe.Probe(datContent, baseName));
        // #endregion
        var result = await RunP610T3FamilyAsync(program, datContent, baseName, workingDirectory, cancellationToken)
            .ConfigureAwait(false);
        // #region agent log
        var workDir = string.IsNullOrEmpty(workingDirectory) ? Path.GetTempPath() : workingDirectory;
        DebugAgentLog.Write("H2", "CppAnalysisRunner.cs:RunP610T3VariantFromDatContentAsync",
            "Solver finished — res/json probe",
            DebugSrfDatProbe.ProbeResultFiles(workDir, baseName, result));
        // #endregion
        return result;
    }

    /// <summary>
    /// Shared runner for the p610_t3 family of executables (currently
    /// <c>p610_t3_elastoplastic</c> and <c>p610_t3_elastic</c>). Both write a JSON result file alongside the .dat input
    /// that <see cref="P610T3JsonParser"/> can parse identically.
    /// </summary>
    private async Task<P610T3RunResult> RunP610T3FamilyAsync(
        string program,
        string datContent,
        string baseName,
        string? workingDirectory,
        CancellationToken cancellationToken,
        string? brandedProgram = null)
    {
        var workDir = string.IsNullOrEmpty(workingDirectory) ? Path.GetTempPath() : workingDirectory;
        var datPath  = Path.Combine(workDir, baseName + ".dat");
        var jsonPath = Path.Combine(workDir, baseName + ".json");

        await File.WriteAllTextAsync(datPath, datContent, cancellationToken).ConfigureAwait(false);

        var savedProgram = ProgramName;
        ProgramName = program;
        string exePath;
        try   { exePath = GetExecutablePath(); }
        finally { ProgramName = savedProgram; }

        if (!File.Exists(exePath))
            throw new FileNotFoundException(
                $"{program}.exe not found: {exePath}\n\n" +
                $"Build the C++ project (cmake --build build --config Release --target {program})\n" +
                $"and ensure {program}.exe is available in {Path.GetDirectoryName(exePath)}.");

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = baseName,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using (cancellationToken.Register(() => { try { process.Kill(); } catch { } }))
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var label = FormatSolverErrorLabel(program, brandedProgram);
            throw new InvalidOperationException(
                $"{label} failed (exit code {process.ExitCode}).{NativeExitHint(process.ExitCode)}\nStdout: {stdout}\nStderr: {stderr}");
        }

        if (!File.Exists(jsonPath))
        {
            var label = FormatSolverErrorLabel(program, brandedProgram);
            throw new InvalidOperationException($"{label} result file not created: {jsonPath}");
        }

        return P610T3JsonParser.ParseFile(jsonPath);
    }

    /// <summary>
    /// Runs a branded Core B <c>slope_stability_*_SRF</c> executable against an explicit p64-layout deck
    /// without p610 sanitisation or gravity-equilibration rewriting.
    /// </summary>
    public async Task<P610T3RunResult> RunNativeSlopeSrfFromDatContentAsync(
        string program,
        string datContent,
        string baseName = "fe_script_run",
        string? workingDirectory = null,
        CancellationToken cancellationToken = default,
        string? brandedProgram = null)
        => await RunP610T3FamilyAsync(program, datContent, baseName, workingDirectory, cancellationToken, brandedProgram)
            .ConfigureAwait(false);
}
