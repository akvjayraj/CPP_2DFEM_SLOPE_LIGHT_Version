using System;
using System.Text;

namespace GeotechSlopeApp.Services.Help;

/// <summary>Where a link inside a help page points: another page, a heading, or the web.</summary>
public sealed class HelpLinkTarget
{
    private HelpLinkTarget(string? topicPath, string? anchor, string? externalUrl)
    {
        TopicPath = topicPath ?? string.Empty;
        Anchor = anchor ?? string.Empty;
        ExternalUrl = externalUrl ?? string.Empty;
    }

    /// <summary>Help-relative markdown path, empty when the link stays on the current page.</summary>
    public string TopicPath { get; }

    /// <summary>Heading slug to scroll to, empty when the link targets the top of a page.</summary>
    public string Anchor { get; }

    public string ExternalUrl { get; }

    public bool IsExternal => ExternalUrl.Length > 0;

    /// <summary>
    /// Turns a markdown href into a target. Paths are resolved against the page holding the link,
    /// so <c>../analysis/lem-methods.md</c> works the same way it does on disk.
    /// </summary>
    public static HelpLinkTarget Resolve(string? currentTopicPath, string? href)
    {
        var raw = (href ?? string.Empty).Trim();
        if (raw.Length == 0)
            return new HelpLinkTarget(null, null, null);

        if (IsExternalHref(raw))
            return new HelpLinkTarget(null, null, raw);

        var hash = raw.IndexOf('#');
        var path = hash >= 0 ? raw[..hash] : raw;
        var anchor = hash >= 0 ? raw[(hash + 1)..] : string.Empty;

        if (path.Length == 0)
            return new HelpLinkTarget(HelpTopic.Normalize(currentTopicPath), anchor, null);

        return new HelpLinkTarget(Combine(DirectoryOf(currentTopicPath), path), anchor, null);
    }

    /// <summary>Resolves a relative resource reference, such as an image, against the current page.</summary>
    public static string ResolveRelativePath(string? currentTopicPath, string? reference)
        => Combine(DirectoryOf(currentTopicPath), reference ?? string.Empty);

    private static bool IsExternalHref(string raw)
        => raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
           || raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
           || raw.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

    private static string DirectoryOf(string? topicPath)
    {
        var normalized = HelpTopic.Normalize(topicPath);
        var slash = normalized.LastIndexOf('/');
        return slash < 0 ? string.Empty : normalized[..slash];
    }

    /// <summary>Joins two help-relative paths and collapses <c>.</c> and <c>..</c> segments.</summary>
    private static string Combine(string directory, string relative)
    {
        var normalized = HelpTopic.Normalize(relative);
        var segments = new System.Collections.Generic.List<string>();

        if (!normalized.StartsWith('/') && directory.Length > 0)
            segments.AddRange(directory.Split('/', StringSplitOptions.RemoveEmptyEntries));

        foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            if (segment == "..")
            {
                if (segments.Count > 0) segments.RemoveAt(segments.Count - 1);
                continue;
            }
            segments.Add(segment);
        }

        var sb = new StringBuilder();
        for (var i = 0; i < segments.Count; i++)
        {
            if (i > 0) sb.Append('/');
            sb.Append(segments[i]);
        }
        return sb.ToString();
    }
}
