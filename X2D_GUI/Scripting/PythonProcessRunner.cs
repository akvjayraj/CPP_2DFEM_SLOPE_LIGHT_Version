using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace GeotechSlopeApp.Scripting;

internal static class PythonProcessRunner
{
    public const string PreferredEnvVar = "XFEM_PYTHON";

    public static string PreferredPathFile =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GeotechSlopeApp",
            "python-exe.txt");

    public static bool TryResolve(out string fileName, out string prefixArgs)
    {
        foreach (var candidate in EnumerateCandidates())
        {
            if (TryAccept(candidate.FileName, candidate.Prefix, out fileName, out prefixArgs))
                return true;
        }

        fileName = "";
        prefixArgs = "";
        return false;
    }

    public static void SetPreferredExecutable(string path)
    {
        var dir = Path.GetDirectoryName(PreferredPathFile);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(PreferredPathFile, path.Trim());
    }

    public static string? ReadPreferredExecutable()
    {
        try
        {
            if (!File.Exists(PreferredPathFile))
                return null;
            var text = File.ReadAllText(PreferredPathFile).Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<(int ExitCode, string Output)> RunAsync(
        string fileName, string prefixArgs, string scriptPath, string pipeName, string clientDir)
    {
        var args = string.IsNullOrWhiteSpace(prefixArgs)
            ? Quote(scriptPath)
            : prefixArgs + " " + Quote(scriptPath);
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? clientDir
        };
        psi.Environment["XFEM_RPC_PIPE"] = pipeName;
        psi.Environment["XFEM_CLIENT_DIR"] = clientDir;
        var path = psi.Environment.TryGetValue("PYTHONPATH", out var existing) ? existing : "";
        psi.Environment["PYTHONPATH"] = string.IsNullOrEmpty(path) ? clientDir : clientDir + Path.PathSeparator + path;

        var sb = new StringBuilder();
        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync().ConfigureAwait(true);
        return (proc.ExitCode, sb.ToString());
    }

    public static string? ResolveClientDirectory()
    {
        var nextToExe = Path.Combine(AppContext.BaseDirectory, "xfem_client");
        if (File.Exists(Path.Combine(nextToExe, "xfem.py")))
            return nextToExe;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "scripts", "xfem_client");
            if (File.Exists(Path.Combine(candidate, "xfem.py")))
                return candidate;
        }

        return null;
    }

    public static string WrapUserScript(string userScript, string clientDir)
    {
        var header =
            "import sys\n" +
            "sys.path.insert(0, " + System.Text.Json.JsonSerializer.Serialize(clientDir) + ")\n" +
            "from xfem import project\n\n";
        var body = userScript ?? "";
        if (body.Contains("from xfem import", StringComparison.Ordinal))
            return "import sys\n" +
                   "sys.path.insert(0, " + System.Text.Json.JsonSerializer.Serialize(clientDir) + ")\n" +
                   body;
        return header + body;
    }

    internal static bool IsWindowsStoreStub(string pathOrName)
    {
        if (string.IsNullOrWhiteSpace(pathOrName))
            return false;
        if (pathOrName.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase))
            return true;
        try
        {
            if (Path.IsPathRooted(pathOrName) && File.Exists(pathOrName) && new FileInfo(pathOrName).Length == 0)
                return true;
        }
        catch
        {
            // ignore
        }

        return false;
    }

    internal static IEnumerable<string> EnumerateInstallPaths()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in CandidateDirectories())
        {
            var exe = dir.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? dir
                : Path.Combine(dir, "python.exe");
            if (seen.Add(exe))
                yield return exe;
        }
    }

    private static IEnumerable<(string FileName, string Prefix)> EnumerateCandidates()
    {
        var env = Environment.GetEnvironmentVariable(PreferredEnvVar);
        if (!string.IsNullOrWhiteSpace(env))
            yield return (env, "");

        var preferred = ReadPreferredExecutable();
        if (!string.IsNullOrWhiteSpace(preferred))
            yield return (preferred, "");

        foreach (var exe in EnumerateInstallPaths())
            yield return (exe, "");

        yield return ("py", "-3");
        yield return ("python", "");
        yield return ("python3", "");
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        foreach (var fromReg in EnumerateRegistryInstallPaths())
            yield return fromReg;

        var localPrograms = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Python");
        if (Directory.Exists(localPrograms))
        {
            foreach (var d in Directory.GetDirectories(localPrograms, "Python3*")
                         .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
                yield return d;
        }

        foreach (var pf in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                 })
        {
            if (!Directory.Exists(pf))
                continue;
            foreach (var d in Directory.GetDirectories(pf, "Python3*"))
                yield return d;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var d in new[]
                 {
                     Path.Combine(local, "GeotechSlopeApp", "python-embed"),
                     Path.Combine(AppContext.BaseDirectory, "python"),
                     Path.Combine(home, "miniconda3"),
                     Path.Combine(home, "anaconda3"),
                     Path.Combine(local, "miniconda3"),
                     Path.Combine(local, "anaconda3"),
                 })
            yield return d;

        var walk = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && walk != null; i++, walk = walk.Parent)
        {
            yield return Path.Combine(walk.FullName, "tools", "python-embed");
            yield return Path.Combine(walk.FullName, "python");
        }
    }

    private static IEnumerable<string> EnumerateRegistryInstallPaths()
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (var keyPath in new[]
                     {
                         @"SOFTWARE\Python\PythonCore",
                         @"SOFTWARE\WOW6432Node\Python\PythonCore"
                     })
            {
                using var core = hive.OpenSubKey(keyPath);
                if (core == null)
                    continue;
                foreach (var ver in core.GetSubKeyNames().OrderByDescending(s => s, StringComparer.OrdinalIgnoreCase))
                {
                    using var install = core.OpenSubKey(ver + @"\InstallPath");
                    if (install == null)
                        continue;
                    if (install.GetValue("ExecutablePath") is string exe && !string.IsNullOrWhiteSpace(exe))
                        yield return exe;
                    if (install.GetValue("") is string dir && !string.IsNullOrWhiteSpace(dir))
                        yield return dir;
                }
            }
        }
    }

    private static bool TryAccept(string pathOrName, string prefix, out string fileName, out string prefixArgs)
    {
        fileName = "";
        prefixArgs = "";
        if (string.IsNullOrWhiteSpace(pathOrName))
            return false;

        var resolved = pathOrName.Trim().Trim('"');
        if (Directory.Exists(resolved))
            resolved = Path.Combine(resolved, "python.exe");
        if (IsWindowsStoreStub(resolved))
            return false;
        if (Path.IsPathRooted(resolved) && !File.Exists(resolved))
            return false;
        if (!TryProbe(resolved, prefix))
            return false;

        fileName = resolved;
        prefixArgs = prefix;
        return true;
    }

    private static bool TryProbe(string exe, string prefix)
    {
        try
        {
            const string code = "import sys; raise SystemExit(0 if sys.version_info >= (3, 11) else 1)";
            var args = string.IsNullOrWhiteSpace(prefix)
                ? "-c \"" + code + "\""
                : prefix + " -c \"" + code + "\"";
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null)
                return false;
            if (!p.WaitForExit(4000))
            {
                try { p.Kill(); } catch { /* ignore */ }
                return false;
            }

            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string Quote(string path) => "\"" + path.Replace("\"", "\\\"") + "\"";
}
