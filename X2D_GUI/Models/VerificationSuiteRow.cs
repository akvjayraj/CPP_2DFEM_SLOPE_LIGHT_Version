using System;
using System.IO;

namespace GeotechSlopeApp.Models;

/// <summary>
/// One row of <c>Verification/_index.csv</c>, the master index that the smoke-test
/// orchestrators (<c>tools/build_all_*_packages.ps1</c>) write after every batch
/// run. Each row corresponds to a single <c>.dat</c> case under
/// <c>Verification/&lt;suite&gt;/&lt;folder&gt;/&lt;case&gt;.dat</c>.
/// </summary>
public sealed class VerificationSuiteRow
{
    public string Suite { get; init; } = "";
    public string Folder { get; init; } = "";
    public string Case { get; init; } = "";
    public string MeshType { get; init; } = "";
    public string FailureCriterion { get; init; } = "";
    public string MaterialType { get; init; } = "";

    /// <summary>Verdict literal as written in <c>_index.csv</c>: typically
    /// <c>PASS</c>, <c>PASS_WITH_INTERPRETATION</c>, <c>PASS_DIAGNOSTIC</c>,
    /// <c>REVIEW</c>, or <c>FAIL</c>.</summary>
    public string Verdict { get; init; } = "";
    public string Notes { get; init; } = "";

    /// <summary>Absolute path to the case folder on disk
    /// (<c>&lt;repoRoot&gt;/Verification/&lt;suite&gt;/&lt;folder&gt;</c>),
    /// resolved by the loader so the GUI can offer "Open folder" without
    /// re-walking the tree.</summary>
    public string CaseFolderPath { get; init; } = "";

    /// <summary>Absolute path to <c>&lt;case&gt;.dat</c> when it can be located,
    /// otherwise empty. Lambda-sweep / SRF rows in <c>_index.csv</c> intentionally
    /// leave the case column blank because the verdict is folder-level; in that
    /// case this field is empty and the GUI falls back to the folder path.</summary>
    public string DatFilePath { get; init; } = "";

    /// <summary>UI-friendly bucket that groups the raw verdict strings into three
    /// states the eye can scan in a list. Anything starting with <c>PASS</c> maps
    /// to <see cref="VerificationVerdictKind.Pass"/>; <c>PASS_WITH_INTERPRETATION</c>
    /// maps to <see cref="VerificationVerdictKind.Review"/>; everything else is
    /// treated as <see cref="VerificationVerdictKind.Fail"/>.</summary>
    public VerificationVerdictKind VerdictKind => ClassifyVerdict(Verdict);

    public string DisplayName =>
        string.IsNullOrEmpty(Case) ? Folder : Case;

    public string MeshLabel =>
        string.IsNullOrEmpty(MeshType) ? "—" : MeshType;

    public string CriterionLabel =>
        string.IsNullOrEmpty(FailureCriterion) ? "—" : FailureCriterion;

    public bool HasDatFile =>
        !string.IsNullOrEmpty(DatFilePath) && File.Exists(DatFilePath);

    public bool HasCaseFolder =>
        !string.IsNullOrEmpty(CaseFolderPath) && Directory.Exists(CaseFolderPath);

    public static VerificationVerdictKind ClassifyVerdict(string verdict)
    {
        if (string.IsNullOrWhiteSpace(verdict))
            return VerificationVerdictKind.Unknown;

        // PASS_WITH_INTERPRETATION calls out something the user should look at
        // (e.g. T3 stress concentration, plastic-flow under-relaxation) even
        // though the suite still classifies it as a pass.
        if (verdict.Equals("PASS_WITH_INTERPRETATION", StringComparison.OrdinalIgnoreCase))
            return VerificationVerdictKind.Review;

        if (verdict.StartsWith("PASS", StringComparison.OrdinalIgnoreCase))
            return VerificationVerdictKind.Pass;

        if (verdict.Equals("REVIEW", StringComparison.OrdinalIgnoreCase))
            return VerificationVerdictKind.Review;

        return VerificationVerdictKind.Fail;
    }
}

public enum VerificationVerdictKind
{
    Unknown,
    Pass,
    Review,
    Fail,
}
