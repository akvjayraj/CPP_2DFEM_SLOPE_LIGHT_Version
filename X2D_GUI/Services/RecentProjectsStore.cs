using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace GeotechSlopeApp.Services;

/// <summary>One row in File → Open Recent (numbered label + optional Ctrl+N shortcut hint).</summary>
public sealed class RecentProjectEntry
{
    public int Index { get; init; }
    public string FilePath { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string ShortcutText { get; init; } = "";
    public string ToolTip => FilePath;
    public string FileName => string.IsNullOrWhiteSpace(FilePath) ? "" : Path.GetFileName(FilePath);
    public string Folder => string.IsNullOrWhiteSpace(FilePath) ? "" : Path.GetDirectoryName(FilePath) ?? "";
    public string ModifiedDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath))
                return "File not found";
            return File.GetLastWriteTime(FilePath).ToString("yyyy-MM-dd HH:mm");
        }
    }
}

/// <summary>
/// Persists the most-recently-used project list under
/// %LocalAppData%/GeotechSlopeApp/recent_{appId}.json.
/// </summary>
public sealed class RecentProjectsStore : INotifyPropertyChanged
{
    public const int MaxEntries = 10;

    private readonly string _appId;
    private readonly List<string> _paths = new();

    public ObservableCollection<RecentProjectEntry> Entries { get; } = new();

    public bool HasEntries => _paths.Count > 0;

    public RecentProjectsStore(string appId)
    {
        _appId = appId;
    }

    private string StorePath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GeotechSlopeApp.Light");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"recent_{_appId}.json");
        }
    }

    public void Load()
    {
        _paths.Clear();
        try
        {
            if (File.Exists(StorePath))
            {
                var json = File.ReadAllText(StorePath);
                var loaded = JsonSerializer.Deserialize<List<string>>(json);
                if (loaded != null)
                {
                    foreach (var p in loaded)
                    {
                        if (!string.IsNullOrWhiteSpace(p))
                            _paths.Add(p);
                    }
                }
            }
        }
        catch
        {
            _paths.Clear();
        }

        while (_paths.Count > MaxEntries)
            _paths.RemoveAt(_paths.Count - 1);

        RebuildEntries();
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_paths, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StorePath, json);
        }
        catch
        {
            // MRU persistence must never break the app.
        }
    }

    public void Add(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        var full = Path.GetFullPath(path);
        _paths.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        _paths.Insert(0, full);
        while (_paths.Count > MaxEntries)
            _paths.RemoveAt(_paths.Count - 1);

        RebuildEntries();
        Save();
    }

    public void Remove(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var full = Path.GetFullPath(path);
        _paths.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        RebuildEntries();
        Save();
    }

    public void Clear()
    {
        _paths.Clear();
        RebuildEntries();
        Save();
    }

    public string? GetPathAt(int oneBasedIndex)
    {
        if (oneBasedIndex < 1 || oneBasedIndex > _paths.Count) return null;
        return _paths[oneBasedIndex - 1];
    }

    private void RebuildEntries()
    {
        Entries.Clear();
        for (var i = 0; i < _paths.Count; i++)
        {
            var index = i + 1;
            var path = _paths[i];
            Entries.Add(new RecentProjectEntry
            {
                Index = index,
                FilePath = path,
                DisplayName = $"{index}. {Path.GetFileName(path)}",
                ShortcutText = index <= 9 ? $"Ctrl+{index}" : ""
            });
        }

        OnPropertyChanged(nameof(HasEntries));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
