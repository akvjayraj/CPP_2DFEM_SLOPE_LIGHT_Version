using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GeotechSlopeApp.Services.Help;

/// <summary>A page that matched a search, with the text around the first hit.</summary>
public sealed class HelpSearchHit
{
    public HelpSearchHit(HelpTopic topic, string snippet, int score)
    {
        Topic = topic;
        Snippet = snippet;
        Score = score;
    }

    public HelpTopic Topic { get; }
    public string Snippet { get; }
    public int Score { get; }
}

/// <summary>
/// Full-text search over the bundled manual. The whole manual is a few hundred kilobytes of
/// markdown, so it is read once into memory and scanned directly rather than indexed by term.
/// </summary>
public sealed class HelpSearchIndex
{
    private sealed record Entry(HelpTopic Topic, string Title, string Body, string LowerTitle, string LowerBody);

    private readonly List<Entry> _entries = new();

    private HelpSearchIndex() { }

    public static HelpSearchIndex Build(HelpTableOfContents toc)
    {
        var index = new HelpSearchIndex();
        if (toc == null || !toc.IsAvailable) return index;

        foreach (var topic in toc.Flatten().Where(t => t.HasContent))
        {
            string body;
            try
            {
                body = PlainText(toc.ReadTopic(topic));
            }
            catch (Exception)
            {
                continue;
            }
            index._entries.Add(new Entry(
                topic, topic.Title, body,
                topic.Title.ToLowerInvariant(), body.ToLowerInvariant()));
        }
        return index;
    }

    public IReadOnlyList<HelpSearchHit> Search(string? query, int maxResults = 40)
    {
        var needle = (query ?? string.Empty).Trim().ToLowerInvariant();
        if (needle.Length < 2) return Array.Empty<HelpSearchHit>();

        var hits = new List<HelpSearchHit>();
        foreach (var entry in _entries)
        {
            var titleHits = Count(entry.LowerTitle, needle);
            var bodyHits = Count(entry.LowerBody, needle);
            if (titleHits == 0 && bodyHits == 0) continue;

            // A title match is what the reader almost always wants, so it outweighs body hits.
            var score = titleHits * 50 + Math.Min(bodyHits, 20);
            hits.Add(new HelpSearchHit(entry.Topic, Snippet(entry.Body, entry.LowerBody, needle), score));
        }

        return hits
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.Topic.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(maxResults)
            .ToList();
    }

    private static int Count(string haystack, string needle)
    {
        var count = 0;
        var at = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (at >= 0)
        {
            count++;
            at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
        }
        return count;
    }

    private static string Snippet(string body, string lowerBody, string needle)
    {
        var at = lowerBody.IndexOf(needle, StringComparison.Ordinal);
        if (at < 0) return Trim(body, 0, 120);

        var start = Math.Max(0, at - 45);
        var snippet = Trim(body, start, 140);
        return start > 0 ? "..." + snippet : snippet;
    }

    private static string Trim(string body, int start, int length)
    {
        if (start >= body.Length) return string.Empty;
        var take = Math.Min(length, body.Length - start);
        var text = body.Substring(start, take).Replace('\n', ' ').Trim();
        return start + take < body.Length ? text + "..." : text;
    }

    /// <summary>Strips the markdown syntax that would otherwise pollute matches and snippets.</summary>
    private static string PlainText(string markdown)
    {
        var sb = new StringBuilder(markdown.Length);
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("```", StringComparison.Ordinal)) continue;
            line = line.TrimStart('#', '>', '-', '*', '+', '|', ' ');
            if (line.Length == 0) continue;

            foreach (var ch in line)
            {
                if (ch is '*' or '`' or '|' or '[' or ']') continue;
                sb.Append(ch);
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
