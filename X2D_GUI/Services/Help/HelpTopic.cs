using System;
using System.Collections.Generic;
using System.Linq;

namespace GeotechSlopeApp.Services.Help;

/// <summary>
/// One page of the bundled user manual: a markdown file plus its place in the contents tree.
/// </summary>
public sealed class HelpTopic
{
    private readonly List<HelpTopic> _children = new();

    public HelpTopic(string id, string title, string? relativePath, int? tabIndex)
    {
        Id = id;
        Title = title;
        RelativePath = Normalize(relativePath);
        TabIndex = tabIndex;
    }

    /// <summary>Stable identifier used by context-sensitive help and by internal links.</summary>
    public string Id { get; }

    public string Title { get; }

    /// <summary>Markdown file relative to the Help folder, with forward slashes. Empty for group-only nodes.</summary>
    public string RelativePath { get; }

    /// <summary>
    /// Main-window tab this topic documents, so F1 can open the page for whatever the user is looking at.
    /// </summary>
    public int? TabIndex { get; }

    public HelpTopic? Parent { get; private set; }

    public IReadOnlyList<HelpTopic> Children => _children;

    public bool HasContent => RelativePath.Length > 0;

    /// <summary>Titles from the root down to this topic, for the breadcrumb.</summary>
    public IReadOnlyList<string> BreadcrumbTitles
    {
        get
        {
            var trail = new List<string>();
            for (var node = this; node != null; node = node.Parent)
                trail.Add(node.Title);
            trail.Reverse();
            return trail;
        }
    }

    public void AddChild(HelpTopic child)
    {
        if (child == null) throw new ArgumentNullException(nameof(child));
        child.Parent = this;
        _children.Add(child);
    }

    /// <summary>This topic followed by every descendant, depth first.</summary>
    public IEnumerable<HelpTopic> Flatten()
        => new[] { this }.Concat(_children.SelectMany(c => c.Flatten()));

    /// <summary>The first topic in this subtree that actually has a page.</summary>
    public HelpTopic? FirstWithContent()
        => Flatten().FirstOrDefault(t => t.HasContent);

    internal static string Normalize(string? relativePath)
        => string.IsNullOrWhiteSpace(relativePath)
            ? string.Empty
            : relativePath.Replace('\\', '/').TrimStart('/');
}
