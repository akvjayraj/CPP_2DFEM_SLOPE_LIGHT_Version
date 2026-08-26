using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Reads Mohr–Coulomb-style material rows from Script Editor .dat content (p610, p64 compact,
/// p64 explicit) into <see cref="MaterialPropertiesViewModel.Materials"/>.
/// </summary>
public static class DatMaterialImporter
{
    /// <summary>
    /// Parse material property rows from <paramref name="datContent"/> into Project Materials.
    /// Returns true when at least one material type was imported.
    /// </summary>
    public static bool TryImportToProjectMaterials(string datContent, MaterialPropertiesViewModel materialsVm)
    {
        if (string.IsNullOrWhiteSpace(datContent) || materialsVm == null)
            return false;

        P610AnalysisOptionsHelper.TryParse(datContent, out var analysisOptions);

        // p610_t3 row-major property rows (φ,c,ψ,γ,E,ν,K0) — must run before p64 heuristics.
        if (P610T3DatContentBuilder.IsP610T3Content(datContent)
            && P610T3DatImporter.TryImportMaterialsOnly(datContent, materialsVm))
            return true;

        if (TryImportP64Compact(datContent, materialsVm, analysisOptions))
            return true;

        if (TryImportExplicitHeader(datContent, materialsVm, analysisOptions))
            return true;

        return false;
    }

    private static bool TryImportP64Compact(
        string datContent,
        MaterialPropertiesViewModel vm,
        P610AnalysisOptions? options)
    {
        var programId = FemCppProgramRouter.ResolveProgramId(datContent);
        if (!string.Equals(programId, "p64", StringComparison.OrdinalIgnoreCase))
            return false;

        var sanitized = CppAnalysisRunner.StripFortranFullLineComments(datContent);
        var tokens = Tokenise(sanitized);
        if (tokens.Count == 0)
            return false;

        // Optional leading program id token.
        if (tokens.Peek().Equals("p64", StringComparison.OrdinalIgnoreCase))
            tokens.Dequeue();

        if (LooksLikeExplicitMeshHeader(tokens))
            return false;

        // w1 s1 w2 h1 h2
        for (int i = 0; i < 5; i++)
            if (!TryDeqDbl(tokens, out _)) return false;

        // nx1 nx2 ny1 ny2
        for (int i = 0; i < 4; i++)
            if (!TryDeqInt(tokens, out _)) return false;

        if (!TryDeqInt(tokens, out int npTypes) || npTypes < 1 || npTypes > 64)
            return false;

        var materials = new List<(double phi, double c, double psi, double gamma, double e, double nu)>();
        for (int m = 0; m < npTypes; m++)
        {
            if (!TryReadMaterialLine(tokens, out var row))
                return false;
            materials.Add(row);
        }

        ApplyMaterials(datContent, vm, materials, options);
        return true;
    }

    /// <summary>Reads φ c ψ γ E ν and optionally skips a trailing K0 coefficient on the same line.</summary>
    private static bool TryReadMaterialLine(
        Queue<string> tokens,
        out (double phi, double c, double psi, double gamma, double e, double nu) row)
    {
        row = default;
        if (!TryDeqDbl(tokens, out double phi) ||
            !TryDeqDbl(tokens, out double c) ||
            !TryDeqDbl(tokens, out double psi) ||
            !TryDeqDbl(tokens, out double gamma) ||
            !TryDeqDbl(tokens, out double e) ||
            !TryDeqDbl(tokens, out double nu))
            return false;

        TrySkipOptionalK0(tokens);
        row = (phi, c, psi, gamma, e, nu);
        return true;
    }

    private static bool LooksLikeExplicitMeshHeader(Queue<string> tokens)
    {
        if (tokens.Count < 3) return false;
        if (!int.TryParse(tokens.ElementAt(0), NumberStyles.Integer, CultureInfo.InvariantCulture, out int nels))
            return false;
        if (!int.TryParse(tokens.ElementAt(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int nn))
            return false;
        if (!int.TryParse(tokens.ElementAt(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int npTypes))
            return false;
        return nels >= 1 && nn > nels && npTypes >= 1 && npTypes <= 64;
    }

    private static void TrySkipOptionalK0(Queue<string> tokens)
    {
        if (tokens.Count < 1) return;
        if (!double.TryParse(tokens.Peek(), NumberStyles.Float, CultureInfo.InvariantCulture, out double k0))
            return;
        if (k0 < 0 || k0 > 2.0) return;

        // Coordinates often start with x=0 and larger |y|; do not treat that as K0.
        if (tokens.Count >= 2
            && double.TryParse(tokens.ElementAt(1), NumberStyles.Float, CultureInfo.InvariantCulture, out double next)
            && (next > k0 + 0.5 || Math.Abs(next) > 2.0))
            return;

        tokens.Dequeue();
    }

    /// <summary>
    /// Reads p64 explicit Q8 material rows (φ c ψ γ E ν per type) without mutating a view model.
    /// </summary>
    public static bool TryReadP64ExplicitMaterialRows(
        string datContent,
        out IReadOnlyList<(double phi, double c, double psi, double gamma, double e, double nu)> rows)
    {
        rows = Array.Empty<(double, double, double, double, double, double)>();
        if (string.IsNullOrWhiteSpace(datContent))
            return false;

        P610AnalysisOptionsHelper.TryParse(datContent, out var options);
        var tokens = Tokenise(datContent);
        if (tokens.Count < 3)
            return false;

        if (!TryDeqInt(tokens, out int nels) ||
            !TryDeqInt(tokens, out int nn) ||
            !TryDeqInt(tokens, out int npTypes))
            return false;

        if (nels < 1 || nn < 4 || npTypes < 1 || npTypes > 64)
            return false;

        if (!IsP64LineMaterialLayout(options))
            return false;

        var materials = new List<(double phi, double c, double psi, double gamma, double e, double nu)>();
        for (int m = 0; m < npTypes; m++)
        {
            if (!TryReadMaterialLine(tokens, out var row))
                return false;
            materials.Add(row);
        }

        rows = materials;
        return true;
    }

    private static bool IsP64LineMaterialLayout(P610AnalysisOptions? options)
        => string.Equals(options?.MeshType, "Q8", StringComparison.OrdinalIgnoreCase)
           || string.Equals(options?.MeshSource, "explicit", StringComparison.OrdinalIgnoreCase);

    private static bool TryImportExplicitHeader(
        string datContent,
        MaterialPropertiesViewModel vm,
        P610AnalysisOptions? options)
    {
        var tokens = Tokenise(datContent);
        if (tokens.Count < 3)
            return false;

        if (!TryDeqInt(tokens, out int nels) ||
            !TryDeqInt(tokens, out int nn) ||
            !TryDeqInt(tokens, out int npTypes))
            return false;

        if (nels < 1 || nn < 4 || npTypes < 1 || npTypes > 64)
            return false;

        if (P610T3DatContentBuilder.IsP610T3Content(datContent))
            return false;

        // p64 explicit Q8 decks use one φ c ψ γ E ν line per type (not p610 row-major).
        if (!IsP64LineMaterialLayout(options))
            return false;

        var materials = new List<(double phi, double c, double psi, double gamma, double e, double nu)>();
        for (int m = 0; m < npTypes; m++)
        {
            if (!TryReadMaterialLine(tokens, out var row))
                return false;
            materials.Add(row);
        }

        ApplyMaterials(datContent, vm, materials, options);
        return true;
    }

    private static void ApplyMaterials(
        string datContent,
        MaterialPropertiesViewModel vm,
        IReadOnlyList<(double phi, double c, double psi, double gamma, double e, double nu)> rows,
        P610AnalysisOptions? options)
    {
        var failure = options?.FailureCriterion ?? FailureCriterion.MohrCoulomb;
        var materialType = options?.MaterialType ?? MaterialType.Isotropic;
        vm.SelectedFailureCriterion = failure;
        vm.SelectedMaterialType = materialType;

        var perMaterialCriteria = ParseMaterialModelOptions(datContent);

        vm.Materials.Clear();
        for (int m = 0; m < rows.Count; m++)
        {
            var r = rows[m];
            vm.Materials.Add(new MaterialModel
            {
                Id = m + 1,
                Name = $"Material {m + 1}",
                FrictionAngle = r.phi,
                Cohesion = r.c,
                DilationAngle = r.psi,
                UnitWeight = r.gamma,
                YoungsModulus = r.e,
                PoissonsRatio = r.nu,
                FailureCriterion = perMaterialCriteria.TryGetValue(m + 1, out var fc) ? fc : failure,
                MaterialType = materialType,
            });
        }

        vm.SelectedMaterialModel = vm.Materials.FirstOrDefault();
    }

    private static Dictionary<int, FailureCriterion> ParseMaterialModelOptions(string datContent)
    {
        var result = new Dictionary<int, FailureCriterion>();
        const string begin = "<BEGIN:MaterialModelOptions>";
        const string end = "<END:MaterialModelOptions>";
        bool inBlock = false;
        int? currentIndex = null;

        foreach (var raw in datContent.Split('\n'))
        {
            var t = raw.Trim();
            if (t.Contains(begin, StringComparison.OrdinalIgnoreCase)) { inBlock = true; continue; }
            if (t.Contains(end, StringComparison.OrdinalIgnoreCase)) break;
            if (!inBlock) continue;

            var line = raw;
            var bang = line.IndexOf('!');
            if (bang >= 0) line = line.Substring(bang + 1).Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("material_index", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx))
                    currentIndex = idx;
                continue;
            }

            if (line.StartsWith("failure_criterion", StringComparison.OrdinalIgnoreCase)
                && currentIndex.HasValue)
            {
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2
                    && Enum.TryParse(parts[1], ignoreCase: true, out FailureCriterion fc))
                    result[currentIndex.Value] = fc;
            }
        }

        return result;
    }

    private static Queue<string> Tokenise(string content)
    {
        var q = new Queue<string>();
        foreach (var raw in content.Split('\n'))
        {
            var line = raw;
            var bang = line.IndexOf('!');
            if (bang >= 0) line = line.Substring(0, bang);
            foreach (var tok in line.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                q.Enqueue(tok);
        }
        return q;
    }

    private static bool TryDeqInt(Queue<string> q, out int v)
    {
        v = 0;
        return q.Count > 0 && int.TryParse(q.Dequeue(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
    }

    private static bool TryDeqDbl(Queue<string> q, out double v)
    {
        v = 0;
        return q.Count > 0 && double.TryParse(q.Dequeue(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }
}
