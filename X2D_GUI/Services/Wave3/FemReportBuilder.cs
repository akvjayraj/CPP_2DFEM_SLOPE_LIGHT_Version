using System.Text;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Models.Wave3;

namespace GeotechSlopeApp.Services.Wave3;

public sealed class FemReportSnapshot
{
    public FemReportOptions Options { get; init; } = new();
    public string ProjectName { get; init; } = "";
    public string Author { get; init; } = "";
    public string Units { get; init; } = "SI";
    public double? EstimatedSrf { get; init; }
    public IReadOnlyList<MaterialModel> Materials { get; init; } = Array.Empty<MaterialModel>();
    public string GroundwaterSummary { get; init; } = "";
    public string ValidationSummary { get; init; } = "";
    public IReadOnlyList<FemSensitivityRunner.ResultRow> SensitivityRows { get; init; }
        = Array.Empty<FemSensitivityRunner.ResultRow>();
    public FemProbabilisticResults? Probabilistic { get; init; }
    public FemBackAnalysisRunner.Result? BackAnalysis { get; init; }
    public IReadOnlyList<FemAnalysisCase> AnalysisCases { get; init; } = Array.Empty<FemAnalysisCase>();
}

public static class FemReportBuilder
{
    public static string BuildMarkdown(FemReportSnapshot snap)
    {
        var o = snap.Options;
        var sb = new StringBuilder();
        if (o.IncludeCoverPage)
        {
            sb.AppendLine($"# {o.ReportTitle}");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(o.Company)) sb.AppendLine($"**Company:** {o.Company}");
            if (!string.IsNullOrWhiteSpace(o.Client)) sb.AppendLine($"**Client:** {o.Client}");
            if (!string.IsNullOrWhiteSpace(o.Location)) sb.AppendLine($"**Location:** {o.Location}");
            if (!string.IsNullOrWhiteSpace(snap.ProjectName)) sb.AppendLine($"**Project:** {snap.ProjectName}");
            if (!string.IsNullOrWhiteSpace(o.DocumentNumber)) sb.AppendLine($"**Document:** {o.DocumentNumber} rev {o.Revision}");
            sb.AppendLine($"**Units:** {snap.Units}");
            if (!string.IsNullOrWhiteSpace(o.PreparedBy)) sb.AppendLine($"**Prepared by:** {o.PreparedBy}");
            if (!string.IsNullOrWhiteSpace(o.CheckedBy)) sb.AppendLine($"**Checked by:** {o.CheckedBy}");
            sb.AppendLine();
        }

        sb.AppendLine("## Strength reduction");
        sb.AppendLine(snap.EstimatedSrf is { } srf && double.IsFinite(srf)
            ? $"Estimated SRF / FoS = **{srf:F3}**."
            : "No SRF result is stored. Run FE Analysis before issuing this report.");
        sb.AppendLine();

        if (o.IncludeMaterials)
        {
            sb.AppendLine("## Materials");
            sb.AppendLine();
            sb.AppendLine("| Id | Name | c (kPa) | φ (°) | γ (kN/m³) | E (kPa) |");
            sb.AppendLine("|---:|---|---:|---:|---:|---:|");
            foreach (var m in snap.Materials)
            {
                sb.AppendLine($"| {m.Id} | {m.Name} | {m.Cohesion:G4} | {m.FrictionAngle:G4} | {m.UnitWeight:G4} | {m.YoungsModulus:G4} |");
            }
            sb.AppendLine();
        }

        if (o.IncludeGroundwater && !string.IsNullOrWhiteSpace(snap.GroundwaterSummary))
        {
            sb.AppendLine("## Groundwater");
            sb.AppendLine();
            sb.AppendLine(snap.GroundwaterSummary);
            sb.AppendLine();
        }

        if (o.IncludeValidation && !string.IsNullOrWhiteSpace(snap.ValidationSummary))
        {
            sb.AppendLine("## Pre-run validation");
            sb.AppendLine();
            sb.AppendLine(snap.ValidationSummary);
            sb.AppendLine();
        }

        if (o.IncludeSensitivity && snap.SensitivityRows.Count > 0)
        {
            sb.AppendLine("## Sensitivity");
            sb.AppendLine();
            sb.AppendLine("| Parameter | Value | SRF | Note |");
            sb.AppendLine("|---|---:|---:|---|");
            foreach (var r in snap.SensitivityRows)
                sb.AppendLine($"| {r.Parameter} | {r.Value:G4} | {r.FactorOfSafety:F3} | {r.Note} |");
            sb.AppendLine();
        }

        if (o.IncludeProbabilistic && snap.Probabilistic != null)
        {
            sb.AppendLine("## Probabilistic");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(snap.Probabilistic.FormatSummary());
            sb.AppendLine("```");
            sb.AppendLine();
        }

        if (o.IncludeBackAnalysis && snap.BackAnalysis != null)
        {
            var b = snap.BackAnalysis;
            sb.AppendLine("## Back analysis");
            sb.AppendLine();
            sb.AppendLine($"Converged: {b.Converged}. Factor F = {b.Factor:F4}. Baseline SRF = {b.BaselineFs:F3}, achieved = {b.AchievedFs:F3}.");
            sb.AppendLine();
        }

        if (snap.AnalysisCases.Count > 0)
        {
            sb.AppendLine("## Analysis cases");
            sb.AppendLine();
            sb.AppendLine("| Case | Active | k_h | k_v | Load factor | SRF | Status |");
            sb.AppendLine("|---|---|---:|---:|---:|---:|---|");
            foreach (var c in snap.AnalysisCases)
                sb.AppendLine($"| {c.Name} | {c.IsActive} | {c.SeismicKh:G3} | {c.SeismicKv:G3} | {c.LoadFactor:G3} | {c.LastFoSText} | {c.Status} |");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(o.Notes))
        {
            sb.AppendLine("## Notes");
            sb.AppendLine();
            sb.AppendLine(o.Notes);
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }
}
