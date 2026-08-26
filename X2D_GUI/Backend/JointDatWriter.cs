using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GeotechSlopeApp.Models.Geometry;

namespace GeotechSlopeApp.Backend;

/// <summary>Exports drawable geological features as a Joints block for the solver / mesh.</summary>
public static class JointDatWriter
{
    public const string BeginTag = "! <BEGIN:Joints>";
    public const string EndTag = "! <END:Joints>";
    public const string SectionName = "Joints";

    public static string BuildBlock(IEnumerable<GeologicalFeature> features)
        => BuildBlock(features, null, null);

    public static string BuildBlock(
        IEnumerable<GeologicalFeature> features,
        IEnumerable<FeJointPropertySet>? propertySets)
        => BuildBlock(features, propertySets, null);

    public static string BuildBlock(
        IEnumerable<GeologicalFeature> features,
        IEnumerable<FeJointPropertySet>? propertySets,
        IEnumerable<FeJointInterfaceElement>? interfaces)
    {
        var list = features.Where(f => f.IsEnabled && f.Vertices.Count >= 2).ToList();
        var ifaces = (interfaces ?? Enumerable.Empty<FeJointInterfaceElement>()).ToList();
        var sets = (propertySets ?? Enumerable.Empty<FeJointPropertySet>())
            .GroupBy(s => s.Id)
            .ToDictionary(g => g.Key, g => g.First());
        var sb = new StringBuilder();
        sb.AppendLine(BeginTag);
        sb.AppendLine($"! include {(list.Count > 0 || ifaces.Count > 0 ? "true" : "false")}");
        sb.AppendLine($"! njoint {list.Count}");
        sb.AppendLine($"! ninterface {ifaces.Count}");
        foreach (var f in list)
        {
            sets.TryGetValue(f.PropertySetId ?? int.MinValue, out var set);
            sb.AppendLine($"! joint {f.Kind.ToString().ToLowerInvariant()} {Quote(f.Name)}");
            if (f.PropertySetId is int pid)
                sb.AppendLine($"!   property {pid}");
            if (f.MaterialId is int mid)
                sb.AppendLine($"!   material {mid}");
            if (f.KnKpa is double kn)
                sb.AppendLine($"!   kn {Fmt(kn)}");
            if (f.KsKpa is double ks)
                sb.AppendLine($"!   ks {Fmt(ks)}");
            if (f.JointCohesionKpa is double c)
                sb.AppendLine($"!   cohesion {Fmt(c)}");
            if (f.JointFrictionDeg is double phi)
                sb.AppendLine($"!   friction {Fmt(phi)}");
            if (f.JointTensionCutoffKpa is double t)
                sb.AppendLine($"!   tension {Fmt(t)}");
            sb.AppendLine($"!   ends {FormatEndCondition(f.EndCondition)}");
            sb.AppendLine($"!   install_stage {Math.Max(1, f.InstallStage)}");
            if (set != null)
            {
                if (set.UseResidualStrength)
                {
                    sb.AppendLine($"!   residual_tensile {Fmt(set.ResidualTensileStrengthKpa)}");
                    sb.AppendLine($"!   residual_cohesion {Fmt(set.ResidualCohesionKpa)}");
                    sb.AppendLine($"!   residual_friction {Fmt(set.ResidualFrictionDeg)}");
                }
                sb.AppendLine($"!   gw_pore {(set.UseGroundwaterPorePressure ? "true" : "false")}");
                if (set.UseAdditionalPressure)
                    sb.AppendLine($"!   additional_u {Fmt(set.AdditionalPressureKpa)}");
                if (set.UseStagedProperties)
                    sb.AppendLine($"!   slip_stage {Math.Max(1, set.AllowSlipFromStage)}");
            }
            sb.AppendLine($"!   nvert {f.Vertices.Count}");
            foreach (var (x, y) in f.Vertices)
                sb.AppendLine($"!     {Fmt(x)}  {Fmt(y)}");
        }
        sb.AppendLine("! interfaces");
        foreach (var it in ifaces)
        {
            sb.AppendLine(
                $"!   {it.N1} {it.N2} {it.N3} {it.N4}  {Fmt(it.Kn)} {Fmt(it.Ks)} {Fmt(it.Cohesion)} {Fmt(it.FrictionDeg)} {Fmt(it.Tension)}  {it.Ends} {Math.Max(1, it.InstallStage)} {Fmt(it.ResidualCohesion)} {Fmt(it.ResidualFrictionDeg)}");
        }
        sb.AppendLine(EndTag);
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string FormatEndCondition(FeJointEndCondition ends) => ends switch
    {
        FeJointEndCondition.BothOpen => "both_open",
        FeJointEndCondition.FirstClosedLastOpen => "first_closed_last_open",
        FeJointEndCondition.FirstOpenLastClosed => "first_open_last_closed",
        _ => "both_closed"
    };

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

    private static string Fmt(double v) => v.ToString("G10", CultureInfo.InvariantCulture);
    private static string Quote(string? name) => "\"" + (name ?? "").Replace("\"", "'") + "\"";
}
