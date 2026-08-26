using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GeotechSlopeApp.Models.Support;

namespace GeotechSlopeApp.Backend;

/// <summary>Writes ! &lt;BEGIN:SupportElements&gt; for p610 2-node bar assembly.</summary>
public static class SupportDatWriter
{
    public const string BeginTag = "! <BEGIN:SupportElements>";
    public const string EndTag = "! <END:SupportElements>";
    public const string SectionName = "SupportElements";

    public static string BuildBlock(
        IReadOnlyList<FeSupportPropertySet> properties,
        IReadOnlyList<FeSupportElement> elements)
    {
        var sb = new StringBuilder();
        sb.AppendLine(BeginTag);
        var bars = elements
            .Select(e => (Element: e, Props: properties.FirstOrDefault(p => p.Id == e.PropertySetId)))
            .Where(x => x.Props != null && FeSupportTypeHelpers.IsBarElement(x.Props.Type) && x.Element.LengthM > 1e-9)
            .ToList();
        sb.AppendLine($"! include {(bars.Count > 0 ? "true" : "false")}");
        sb.AppendLine($"! nbar {bars.Count}");
        sb.AppendLine("! bars");
        foreach (var (element, props) in bars)
        {
            var ea = props!.AxialStiffnessKn;
            var spacing = Math.Max(0.05, props.OutOfPlaneSpacingM);
            var eaPerM = ea / spacing;
            sb.AppendLine(
                $"!   {Fmt(element.HeadX)}  {Fmt(element.HeadY)}  {Fmt(element.TailX)}  {Fmt(element.TailY)}  " +
                $"{Fmt(eaPerM)}  {Fmt(props.PrestressKn / spacing)}  {props.InstallStage}  " +
                $"{(props.ExcludeFromSrf ? 1 : 0)}  {FeSupportTypeHelpers.DatToken(props.Type)}");
        }
        sb.AppendLine(EndTag);
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    public static string MergeSection(string datContent, string block)
    {
        if (string.IsNullOrEmpty(datContent))
            return block;
        var pattern = $@"! <BEGIN:{SectionName}>[\s\S]*?! <END:{SectionName}>\s*";
        if (Regex.IsMatch(datContent, pattern, RegexOptions.IgnoreCase))
            return Regex.Replace(datContent, pattern, block.TrimEnd() + Environment.NewLine, RegexOptions.IgnoreCase);
        var insertAt = datContent.IndexOf("! <BEGIN:", StringComparison.OrdinalIgnoreCase);
        if (insertAt < 0)
            return datContent.TrimEnd() + Environment.NewLine + Environment.NewLine + block;
        return datContent.Insert(insertAt, block + Environment.NewLine);
    }

    public static string MergeFromViewModels(
        string datContent,
        IReadOnlyList<FeSupportPropertySet> properties,
        IReadOnlyList<FeSupportElement> elements)
        => MergeSection(datContent, BuildBlock(properties, elements));

    private static string Fmt(double v) => v.ToString("G10", CultureInfo.InvariantCulture);
}
