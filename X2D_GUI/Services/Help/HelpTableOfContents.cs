using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GeotechSlopeApp.Services.Help;

/// <summary>
/// The bundled user manual: the contents tree from <c>Help/toc.json</c> plus the folder it lives in.
/// </summary>
public sealed class HelpTableOfContents
{
    public const string ManifestFileName = "toc.json";
    public const string HomeTopicId = "index";

    private readonly Dictionary<string, HelpTopic> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HelpTopic> _byPath = new(StringComparer.OrdinalIgnoreCase);

    private HelpTableOfContents(string title, string directory, IReadOnlyList<HelpTopic> roots)
    {
        Title = title;
        Directory = directory;
        Roots = roots;
        foreach (var topic in roots.SelectMany(r => r.Flatten()))
        {
            _byId.TryAdd(topic.Id, topic);
            if (topic.HasContent)
                _byPath.TryAdd(topic.RelativePath, topic);
        }
    }

    public string Title { get; }

    /// <summary>Absolute path of the Help folder; empty when the manual could not be located.</summary>
    public string Directory { get; }

    public IReadOnlyList<HelpTopic> Roots { get; }

    public bool IsAvailable => Directory.Length > 0 && Roots.Count > 0;

    public IEnumerable<HelpTopic> Flatten() => Roots.SelectMany(r => r.Flatten());

    public HelpTopic? FindById(string? id)
        => id != null && _byId.TryGetValue(id, out var topic) ? topic : null;

    public HelpTopic? FindByPath(string? relativePath)
    {
        var key = HelpTopic.Normalize(relativePath);
        return key.Length > 0 && _byPath.TryGetValue(key, out var topic) ? topic : null;
    }

    /// <summary>Topic documenting a main-window tab, for context-sensitive help.</summary>
    public HelpTopic? FindByTabIndex(int tabIndex)
        => Flatten().FirstOrDefault(t => t.TabIndex == tabIndex && t.HasContent);

    /// <summary>The landing page, falling back to the first page in the tree.</summary>
    public HelpTopic? Home()
        => FindById(HomeTopicId) ?? Roots.Select(r => r.FirstWithContent()).FirstOrDefault(t => t != null);

    public string AbsolutePathOf(HelpTopic topic)
    {
        if (topic == null) throw new ArgumentNullException(nameof(topic));
        return topic.HasContent && Directory.Length > 0
            ? Path.GetFullPath(Path.Combine(Directory, topic.RelativePath.Replace('/', Path.DirectorySeparatorChar)))
            : string.Empty;
    }

    public string ReadTopic(HelpTopic topic)
    {
        var path = AbsolutePathOf(topic);
        if (path.Length == 0 || !File.Exists(path))
            return $"# {topic?.Title}\n\nThis page is not installed next to the application.\n";
        return File.ReadAllText(path);
    }

    /// <summary>Loads the manual from the Help folder beside the executable, or from the source tree.</summary>
    public static HelpTableOfContents Load()
    {
        var directory = ResolveHelpDirectory();
        if (directory == null)
            return Empty();

        try
        {
            var json = File.ReadAllText(Path.Combine(directory, ManifestFileName));
            var manifest = JsonSerializer.Deserialize<Manifest>(json, SerializerOptions);
            if (manifest?.Topics == null || manifest.Topics.Count == 0)
                return Empty();

            var roots = manifest.Topics.Select(Build).ToList();
            var title = string.IsNullOrWhiteSpace(manifest.Title) ? "User Manual" : manifest.Title!;
            return new HelpTableOfContents(title, directory, roots);
        }
        catch (Exception)
        {
            // A malformed or unreadable manual must never stop the app from opening Help.
            return Empty();
        }
    }

    /// <summary>Folder holding <c>toc.json</c>: beside the exe, in the project, or up the source tree.</summary>
    public static string? ResolveHelpDirectory()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new List<string>
        {
            Path.Combine(baseDir, "Help"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Help")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "X2D_GUI", "Help"))
        };

        // Test runners and tooling can sit well below the repository root.
        for (var dir = new DirectoryInfo(baseDir); dir != null; dir = dir.Parent)
            candidates.Add(Path.Combine(dir.FullName, "X2D_GUI", "Help"));

        return candidates.FirstOrDefault(c => File.Exists(Path.Combine(c, ManifestFileName)));
    }

    private static HelpTableOfContents Empty()
        => new("User Manual", string.Empty, Array.Empty<HelpTopic>());

    private static HelpTopic Build(TopicEntry entry)
    {
        var id = string.IsNullOrWhiteSpace(entry.Id)
            ? HelpTopic.Normalize(entry.File).Replace(".md", "", StringComparison.OrdinalIgnoreCase)
            : entry.Id!;
        var topic = new HelpTopic(
            id,
            string.IsNullOrWhiteSpace(entry.Title) ? id : entry.Title!,
            entry.File,
            entry.TabIndex);
        foreach (var child in entry.Children ?? new List<TopicEntry>())
            topic.AddChild(Build(child));
        return topic;
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private sealed class Manifest
    {
        public string? Title { get; set; }
        public List<TopicEntry>? Topics { get; set; }
    }

    private sealed class TopicEntry
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? File { get; set; }
        public int? TabIndex { get; set; }
        public List<TopicEntry>? Children { get; set; }
    }
}
