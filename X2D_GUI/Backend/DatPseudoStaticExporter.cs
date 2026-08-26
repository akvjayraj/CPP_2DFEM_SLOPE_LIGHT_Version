using System;
using System.Text;
using System.Text.RegularExpressions;

namespace GeotechSlopeApp.Backend;

/// <summary>Patches k_h / k_v into Script Editor .dat AnalysisOptions (and legacy comment lines).</summary>
public static class DatPseudoStaticExporter
{
    private const string BeginTag = "! <BEGIN:AnalysisOptions>";
    private const string EndTag = "! <END:AnalysisOptions>";

    public static bool TryPatchPseudoStatic(
        string datContent,
        double k_h,
        double k_v,
        out string patched,
        out bool contentChanged)
    {
        patched = datContent ?? "";
        contentChanged = false;
        if (string.IsNullOrEmpty(patched))
            return false;

        var khText = PseudoStaticSeismicRunHelper.FormatCoefficient(k_h);
        var kvText = PseudoStaticSeismicRunHelper.FormatCoefficient(k_v);

        if (TryPatchInsideAnalysisOptions(patched, khText, kvText, out var next))
        {
            contentChanged = !string.Equals(next, patched, StringComparison.Ordinal);
            patched = next;
            return true;
        }

        var fallback = PatchOrInsertCommentLines(patched, khText, kvText);
        contentChanged = !string.Equals(fallback, patched, StringComparison.Ordinal);
        patched = fallback;
        return true;
    }

    private static bool TryPatchInsideAnalysisOptions(
        string content,
        string khText,
        string kvText,
        out string patched)
    {
        patched = content;
        int b = content.IndexOf(BeginTag, StringComparison.OrdinalIgnoreCase);
        if (b < 0) return false;
        int e = content.IndexOf(EndTag, b + BeginTag.Length, StringComparison.OrdinalIgnoreCase);
        if (e < 0) return false;

        int bodyStart = b + BeginTag.Length;
        if (bodyStart < content.Length && (content[bodyStart] == '\r' || content[bodyStart] == '\n'))
        {
            int nl = content.IndexOf('\n', bodyStart);
            bodyStart = nl < 0 ? content.Length : nl + 1;
            if (bodyStart < content.Length && content[bodyStart - 1] == '\r' && content[bodyStart] == '\n')
            { /* already advanced past \n */ }
        }

        string body = content.Substring(bodyStart, e - bodyStart);
        body = NormalizeCorruptedOptionLines(body);
        string updatedBody = UpsertOptionLine(body, "k_h", khText);
        updatedBody = UpsertOptionLine(updatedBody, "k_v", kvText);

        var sb = new StringBuilder();
        sb.Append(content.AsSpan(0, bodyStart));
        sb.Append(updatedBody);
        sb.Append(content.AsSpan(e));
        patched = sb.ToString();
        return true;
    }

    /// <summary>Repairs legacy exporter bug that produced "! ! k_h", inline "SRF! k_h", and bare END tags.</summary>
    private static string NormalizeCorruptedOptionLines(string block)
    {
        block = Regex.Replace(
            block,
            @"(?m)^\s*!\s*!\s*(k_h|k_v)\s+",
            "! $1 ",
            RegexOptions.IgnoreCase);
        // Repair $10.1-style regex corruption: inline "! k_h" / "! k_v" glued to prior tokens.
        block = Regex.Replace(block, @"([^\r\n])(!\s+k_h\s+)", "$1" + Environment.NewLine + "$2", RegexOptions.IgnoreCase);
        block = Regex.Replace(block, @"([^\r\n])(!\s+k_v\s+)", "$1" + Environment.NewLine + "$2", RegexOptions.IgnoreCase);
        if (!block.Contains(EndTag, StringComparison.OrdinalIgnoreCase)
            && block.Contains("<END:AnalysisOptions>", StringComparison.OrdinalIgnoreCase))
        {
            block = block.Replace(
                "<END:AnalysisOptions>",
                EndTag,
                StringComparison.OrdinalIgnoreCase);
        }

        return block;
    }

    private static string UpsertOptionLine(string block, string key, string value)
    {
        var pattern = new Regex(
            $@"(^\s*!\s*{Regex.Escape(key)}\s+).*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (pattern.IsMatch(block))
            return pattern.Replace(block, m => m.Groups[1].Value + value);

        string insert = $"! {key} {value}{Environment.NewLine}";
        return block.TrimEnd() + (block.Length > 0 && !block.EndsWith('\n') ? Environment.NewLine : "") + insert;
    }

    private static string PatchOrInsertCommentLines(string content, string khText, string kvText)
    {
        content = UpsertCommentLine(content, "k_h", khText);
        content = UpsertCommentLine(content, "k_v", kvText);
        if (!content.Contains("! k_h", StringComparison.OrdinalIgnoreCase))
            content = $"! k_h {khText}{Environment.NewLine}" + content;
        if (!content.Contains("! k_v", StringComparison.OrdinalIgnoreCase))
            content = $"! k_v {kvText}{Environment.NewLine}" + content;
        return content;
    }

    private static string UpsertCommentLine(string content, string key, string value)
    {
        var pattern = new Regex(
            $@"(^\s*!\s*{Regex.Escape(key)}\s+).*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (pattern.IsMatch(content))
            return pattern.Replace(content, m => m.Groups[1].Value + value);
        return content;
    }
}
