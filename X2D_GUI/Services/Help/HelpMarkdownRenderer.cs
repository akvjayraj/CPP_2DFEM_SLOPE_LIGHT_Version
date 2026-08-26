using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GeotechSlopeApp.Services.Help;

/// <summary>A rendered help page plus the heading anchors the viewer can scroll to.</summary>
public sealed class HelpRenderResult
{
    public HelpRenderResult(FlowDocument document, IReadOnlyDictionary<string, TextElement> anchors)
    {
        Document = document;
        Anchors = anchors;
    }

    public FlowDocument Document { get; }

    public IReadOnlyDictionary<string, TextElement> Anchors { get; }
}

/// <summary>
/// Renders the markdown of a help page into a <see cref="FlowDocument"/>: headings, paragraphs,
/// lists, pipe tables, fenced code, blockquote callouts, images and links.
/// </summary>
public static class HelpMarkdownRenderer
{
    private static readonly Brush TextBrush = Frozen(Color.FromRgb(0x1E, 0x29, 0x3B));
    private static readonly Brush HeadingBrush = Frozen(Color.FromRgb(0x0F, 0x17, 0x2A));
    private static readonly Brush MutedBrush = Frozen(Color.FromRgb(0x64, 0x74, 0x8B));
    private static readonly Brush LinkBrush = Frozen(Color.FromRgb(0x1D, 0x4E, 0xD8));
    private static readonly Brush RuleBrush = Frozen(Color.FromRgb(0xE2, 0xE8, 0xF0));
    private static readonly Brush CodeBackground = Frozen(Color.FromRgb(0xF1, 0xF5, 0xF9));
    private static readonly Brush HeaderBackground = Frozen(Color.FromRgb(0xF8, 0xFA, 0xFC));
    private static readonly Brush NoteAccent = Frozen(Color.FromRgb(0x25, 0x63, 0xEB));
    private static readonly Brush WarnAccent = Frozen(Color.FromRgb(0xD9, 0x77, 0x06));

    private static readonly FontFamily BodyFont = new("Segoe UI");
    private static readonly FontFamily CodeFont = new("Consolas");

    private static readonly Regex BulletItem = new(@"^(\s*)[-*+]\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex NumberedItem = new(@"^(\s*)\d+[.)]\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex HorizontalRule = new(@"^\s*([-*_])(\s*\1){2,}\s*$", RegexOptions.Compiled);

    /// <summary>Everything the renderer needs to resolve links and images relative to the page.</summary>
    public sealed class Context
    {
        public Context(string helpDirectory, string currentTopicPath, Action<HelpLinkTarget>? navigate)
        {
            HelpDirectory = helpDirectory ?? string.Empty;
            CurrentTopicPath = HelpTopic.Normalize(currentTopicPath);
            Navigate = navigate;
        }

        public string HelpDirectory { get; }
        public string CurrentTopicPath { get; }
        public Action<HelpLinkTarget>? Navigate { get; }
    }

    public static HelpRenderResult Render(string markdown, Context context)
    {
        var anchors = new Dictionary<string, TextElement>(StringComparer.OrdinalIgnoreCase);
        var document = new FlowDocument
        {
            FontFamily = BodyFont,
            FontSize = 13.5,
            Foreground = TextBrush,
            LineHeight = 20,
            PagePadding = new Thickness(24, 18, 28, 28),
            // Without this the viewer breaks long pages into newspaper columns.
            ColumnWidth = double.PositiveInfinity,
            IsOptimalParagraphEnabled = false
        };

        var lines = (markdown ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        var index = 0;
        while (index < lines.Length)
            AppendBlock(document.Blocks, lines, ref index, context, anchors);

        return new HelpRenderResult(document, anchors);
    }

    private static void AppendBlock(
        BlockCollection blocks,
        string[] lines,
        ref int index,
        Context context,
        Dictionary<string, TextElement> anchors)
    {
        var line = lines[index];

        if (string.IsNullOrWhiteSpace(line))
        {
            index++;
            return;
        }

        if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
        {
            blocks.Add(ReadCodeBlock(lines, ref index));
            return;
        }

        if (IsTableStart(lines, index))
        {
            blocks.Add(ReadTable(lines, ref index, context));
            return;
        }

        if (HorizontalRule.IsMatch(line))
        {
            index++;
            blocks.Add(new BlockUIContainer(new Border
            {
                BorderBrush = RuleBrush,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Margin = new Thickness(0, 10, 0, 10)
            }));
            return;
        }

        if (line.TrimStart().StartsWith(">", StringComparison.Ordinal))
        {
            blocks.Add(ReadCallout(lines, ref index, context));
            return;
        }

        var heading = HeadingLevel(line);
        if (heading > 0)
        {
            var text = line.TrimStart().TrimStart('#').Trim();
            index++;
            var paragraph = HeadingParagraph(text, heading, context);
            anchors[Slug(text)] = paragraph;
            blocks.Add(paragraph);
            return;
        }

        if (BulletItem.IsMatch(line) || NumberedItem.IsMatch(line))
        {
            blocks.Add(ReadList(lines, ref index, IndentOf(line), context));
            return;
        }

        blocks.Add(ReadParagraph(lines, ref index, context));
    }

    private static int HeadingLevel(string line)
    {
        var trimmed = line.TrimStart();
        var hashes = 0;
        while (hashes < trimmed.Length && trimmed[hashes] == '#') hashes++;
        return hashes is >= 1 and <= 4 && hashes < trimmed.Length && trimmed[hashes] == ' ' ? hashes : 0;
    }

    private static Paragraph HeadingParagraph(string text, int level, Context context)
    {
        var (size, top) = level switch
        {
            1 => (24.0, 4.0),
            2 => (18.5, 20.0),
            3 => (15.5, 16.0),
            _ => (13.5, 12.0)
        };

        var paragraph = new Paragraph
        {
            FontSize = size,
            FontWeight = FontWeights.SemiBold,
            Foreground = HeadingBrush,
            Margin = new Thickness(0, top, 0, level == 1 ? 10 : 6)
        };
        AppendInlines(paragraph.Inlines, text, context);

        if (level <= 2)
        {
            paragraph.BorderBrush = RuleBrush;
            paragraph.BorderThickness = new Thickness(0, 0, 0, 1);
            paragraph.Padding = new Thickness(0, 0, 0, 5);
        }
        return paragraph;
    }

    private static Paragraph ReadParagraph(string[] lines, ref int index, Context context)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 10) };
        var first = true;
        while (index < lines.Length && !IsBlockBoundary(lines, index))
        {
            var line = lines[index];
            if (!first)
                paragraph.Inlines.Add(line.StartsWith("  ", StringComparison.Ordinal)
                    ? new LineBreak()
                    : new Run(" "));
            AppendInlines(paragraph.Inlines, line.Trim(), context);
            first = false;
            index++;
        }
        return paragraph;
    }

    private static bool IsBlockBoundary(string[] lines, int index)
    {
        var line = lines[index];
        if (string.IsNullOrWhiteSpace(line)) return true;
        if (HeadingLevel(line) > 0) return true;
        if (HorizontalRule.IsMatch(line)) return true;
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("```", StringComparison.Ordinal)
               || trimmed.StartsWith(">", StringComparison.Ordinal)
               || trimmed.StartsWith("|", StringComparison.Ordinal)
               || BulletItem.IsMatch(line)
               || NumberedItem.IsMatch(line);
    }

    private static Block ReadCodeBlock(string[] lines, ref int index)
    {
        index++; // opening fence
        var code = new StringBuilder();
        while (index < lines.Length && !lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal))
        {
            code.AppendLine(lines[index]);
            index++;
        }
        if (index < lines.Length) index++; // closing fence

        var paragraph = new Paragraph(new Run(code.ToString().TrimEnd()))
        {
            FontFamily = CodeFont,
            FontSize = 12.5,
            Background = CodeBackground,
            Foreground = TextBrush,
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 2, 0, 12),
            LineHeight = 17
        };
        return paragraph;
    }

    private static Block ReadCallout(string[] lines, ref int index, Context context)
    {
        var body = new List<string>();
        while (index < lines.Length && lines[index].TrimStart().StartsWith(">", StringComparison.Ordinal))
        {
            var stripped = lines[index].TrimStart();
            stripped = stripped.Length > 1 ? stripped[1..] : string.Empty;
            body.Add(stripped.StartsWith(" ", StringComparison.Ordinal) ? stripped[1..] : stripped);
            index++;
        }

        var joined = string.Join("\n", body);
        var accent = joined.Contains("Warning", StringComparison.OrdinalIgnoreCase)
                     || joined.Contains("Caution", StringComparison.OrdinalIgnoreCase)
            ? WarnAccent
            : NoteAccent;

        var section = new Section
        {
            BorderBrush = accent,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Background = HeaderBackground,
            Padding = new Thickness(12, 8, 12, 2),
            Margin = new Thickness(0, 2, 0, 12)
        };

        var inner = body.ToArray();
        var innerIndex = 0;
        var anchors = new Dictionary<string, TextElement>(StringComparer.OrdinalIgnoreCase);
        while (innerIndex < inner.Length)
            AppendBlock(section.Blocks, inner, ref innerIndex, context, anchors);

        return section;
    }

    private static int IndentOf(string line)
    {
        var match = BulletItem.Match(line);
        if (!match.Success) match = NumberedItem.Match(line);
        return match.Success ? match.Groups[1].Value.Replace("\t", "    ").Length : 0;
    }

    private static Block ReadList(string[] lines, ref int index, int indent, Context context)
    {
        var ordered = NumberedItem.IsMatch(lines[index]);
        var list = new List
        {
            MarkerStyle = ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(indent > 0 ? 8 : 2, 0, 0, 10),
            Padding = new Thickness(20, 0, 0, 0)
        };
        ListItem? lastItem = null;

        while (index < lines.Length)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line)) break;

            var match = BulletItem.Match(line);
            var isOrdered = false;
            if (!match.Success)
            {
                match = NumberedItem.Match(line);
                isOrdered = match.Success;
            }
            if (!match.Success) break;

            var itemIndent = match.Groups[1].Value.Replace("\t", "    ").Length;
            if (itemIndent < indent || isOrdered != ordered && itemIndent == indent) break;

            if (itemIndent > indent)
            {
                var nested = ReadList(lines, ref index, itemIndent, context);
                if (lastItem != null)
                {
                    lastItem.Blocks.Add(nested);
                }
                else
                {
                    lastItem = new ListItem();
                    lastItem.Blocks.Add(nested);
                    list.ListItems.Add(lastItem);
                }
                continue;
            }

            index++;
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 4) };
            AppendInlines(paragraph.Inlines, match.Groups[2].Value.Trim(), context);

            // Wrapped continuation lines belong to the item they follow.
            while (index < lines.Length
                   && !string.IsNullOrWhiteSpace(lines[index])
                   && !BulletItem.IsMatch(lines[index])
                   && !NumberedItem.IsMatch(lines[index])
                   && IsIndentedContinuation(lines[index], indent))
            {
                paragraph.Inlines.Add(new Run(" "));
                AppendInlines(paragraph.Inlines, lines[index].Trim(), context);
                index++;
            }

            lastItem = new ListItem(paragraph);
            list.ListItems.Add(lastItem);
        }

        return list;
    }

    private static bool IsIndentedContinuation(string line, int indent)
    {
        var leading = line.Length - line.TrimStart().Length;
        return leading > indent && HeadingLevel(line) == 0 && !line.TrimStart().StartsWith("|", StringComparison.Ordinal);
    }

    private static bool IsTableStart(string[] lines, int index)
    {
        if (!lines[index].TrimStart().StartsWith("|", StringComparison.Ordinal)) return false;
        if (index + 1 >= lines.Length) return false;
        var separator = lines[index + 1].Trim();
        return separator.StartsWith("|", StringComparison.Ordinal)
               && separator.Contains('-')
               && separator.All(c => c is '|' or '-' or ':' or ' ');
    }

    private static Block ReadTable(string[] lines, ref int index, Context context)
    {
        var header = SplitRow(lines[index]);
        var alignments = SplitRow(lines[index + 1]).Select(ParseAlignment).ToList();
        index += 2;

        var rows = new List<List<string>>();
        while (index < lines.Length && lines[index].TrimStart().StartsWith("|", StringComparison.Ordinal))
        {
            rows.Add(SplitRow(lines[index]));
            index++;
        }

        var columnCount = Math.Max(header.Count, rows.Count > 0 ? rows.Max(r => r.Count) : 0);
        var table = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 2, 0, 14),
            FontSize = 12.5
        };

        // Share the width in proportion to the widest cell, so label columns stay narrow.
        for (var c = 0; c < columnCount; c++)
        {
            var widest = Math.Max(
                CellAt(header, c).Length,
                rows.Count > 0 ? rows.Max(r => CellAt(r, c).Length) : 0);
            table.Columns.Add(new TableColumn
            {
                Width = new GridLength(Math.Clamp(widest, 6, 46), GridUnitType.Star)
            });
        }

        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        group.Rows.Add(BuildRow(header, columnCount, alignments, context, isHeader: true));
        foreach (var row in rows)
            group.Rows.Add(BuildRow(row, columnCount, alignments, context, isHeader: false));
        return table;
    }

    private static TableRow BuildRow(
        List<string> cells,
        int columnCount,
        List<TextAlignment> alignments,
        Context context,
        bool isHeader)
    {
        var row = new TableRow();
        if (isHeader) row.Background = HeaderBackground;

        for (var c = 0; c < columnCount; c++)
        {
            var paragraph = new Paragraph
            {
                Margin = new Thickness(0),
                LineHeight = 18,
                TextAlignment = c < alignments.Count ? alignments[c] : TextAlignment.Left
            };
            if (isHeader) paragraph.FontWeight = FontWeights.SemiBold;
            AppendInlines(paragraph.Inlines, CellAt(cells, c), context);

            row.Cells.Add(new TableCell(paragraph)
            {
                BorderBrush = RuleBrush,
                BorderThickness = new Thickness(c == 0 ? 1 : 0, isHeader ? 1 : 0, 1, 1),
                Padding = new Thickness(9, 6, 9, 6)
            });
        }
        return row;
    }

    private static string CellAt(List<string> cells, int index)
        => index < cells.Count ? cells[index] : string.Empty;

    private static TextAlignment ParseAlignment(string spec)
    {
        var trimmed = spec.Trim();
        if (trimmed.StartsWith(':') && trimmed.EndsWith(':')) return TextAlignment.Center;
        if (trimmed.EndsWith(':')) return TextAlignment.Right;
        return TextAlignment.Left;
    }

    private static List<string> SplitRow(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('|')) trimmed = trimmed[1..];
        if (trimmed.EndsWith('|')) trimmed = trimmed[..^1];

        var cells = new List<string>();
        var current = new StringBuilder();
        var inCode = false;
        foreach (var ch in trimmed)
        {
            if (ch == '`') inCode = !inCode;
            if (ch == '|' && !inCode)
            {
                cells.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }
            current.Append(ch);
        }
        cells.Add(current.ToString().Trim());
        return cells;
    }

    private static void AppendInlines(InlineCollection target, string text, Context context)
    {
        var buffer = new StringBuilder();
        var i = 0;

        void Flush()
        {
            if (buffer.Length == 0) return;
            target.Add(new Run(buffer.ToString()));
            buffer.Clear();
        }

        while (i < text.Length)
        {
            var ch = text[i];

            if (ch == '`')
            {
                var end = text.IndexOf('`', i + 1);
                if (end > i)
                {
                    Flush();
                    target.Add(new Run(text[(i + 1)..end])
                    {
                        FontFamily = CodeFont,
                        FontSize = 12.5,
                        Background = CodeBackground
                    });
                    i = end + 1;
                    continue;
                }
            }

            if (ch == '!' && i + 1 < text.Length && text[i + 1] == '['
                && TryReadLink(text, i + 1, out var altText, out var source, out var afterImage))
            {
                Flush();
                target.Add(BuildImage(altText, source, context));
                i = afterImage;
                continue;
            }

            if (ch == '[' && TryReadLink(text, i, out var label, out var href, out var afterLink))
            {
                Flush();
                target.Add(BuildLink(label, href, context));
                i = afterLink;
                continue;
            }

            if (ch == '*' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var end = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (end > i)
                {
                    Flush();
                    var span = new Span { FontWeight = FontWeights.SemiBold };
                    AppendInlines(span.Inlines, text[(i + 2)..end], context);
                    target.Add(span);
                    i = end + 2;
                    continue;
                }
            }

            // Only '*' marks emphasis; '_' is left alone so symbols such as k_y survive intact.
            if (ch == '*')
            {
                var end = text.IndexOf('*', i + 1);
                if (end > i + 1)
                {
                    Flush();
                    var span = new Span { FontStyle = FontStyles.Italic };
                    AppendInlines(span.Inlines, text[(i + 1)..end], context);
                    target.Add(span);
                    i = end + 1;
                    continue;
                }
            }

            buffer.Append(ch);
            i++;
        }

        Flush();
    }

    private static bool TryReadLink(string text, int start, out string label, out string href, out int next)
    {
        label = string.Empty;
        href = string.Empty;
        next = start;

        var close = text.IndexOf(']', start + 1);
        if (close < 0 || close + 1 >= text.Length || text[close + 1] != '(') return false;

        var depth = 1;
        var i = close + 2;
        var hrefStart = i;
        while (i < text.Length && depth > 0)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')') depth--;
            if (depth == 0) break;
            i++;
        }
        if (depth != 0) return false;

        label = text[(start + 1)..close];
        href = text[hrefStart..i].Trim();
        next = i + 1;
        return true;
    }

    private static Inline BuildLink(string label, string href, Context context)
    {
        var target = HelpLinkTarget.Resolve(context.CurrentTopicPath, href);
        var link = new Hyperlink { Foreground = LinkBrush, Cursor = System.Windows.Input.Cursors.Hand };
        AppendInlines(link.Inlines, label, context);
        link.ToolTip = target.IsExternal ? target.ExternalUrl : href;
        link.Click += (_, _) => context.Navigate?.Invoke(target);
        return link;
    }

    private static Inline BuildImage(string altText, string source, Context context)
    {
        var relative = HelpLinkTarget.ResolveRelativePath(context.CurrentTopicPath, source);
        var absolute = context.HelpDirectory.Length > 0 && relative.Length > 0
            ? Path.Combine(context.HelpDirectory, relative.Replace('/', Path.DirectorySeparatorChar))
            : string.Empty;

        if (absolute.Length == 0 || !File.Exists(absolute))
        {
            return new Run(string.IsNullOrWhiteSpace(altText) ? "[figure]" : $"[{altText}]")
            {
                FontStyle = FontStyles.Italic,
                Foreground = MutedBrush
            };
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(absolute, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            var image = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                MaxWidth = bitmap.PixelWidth,
                ToolTip = altText
            };
            return new InlineUIContainer(image) { BaselineAlignment = BaselineAlignment.Bottom };
        }
        catch (Exception)
        {
            return new Run($"[{altText}]") { FontStyle = FontStyles.Italic, Foreground = MutedBrush };
        }
    }

    /// <summary>GitHub-style heading slug, so <c>#slope-limits</c> style links work.</summary>
    public static string Slug(string headingText)
    {
        var sb = new StringBuilder();
        var lastWasDash = false;
        foreach (var ch in (headingText ?? string.Empty).ToLower(CultureInfo.InvariantCulture))
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                lastWasDash = false;
            }
            else if (!lastWasDash && sb.Length > 0)
            {
                sb.Append('-');
                lastWasDash = true;
            }
        }
        return sb.ToString().Trim('-');
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
