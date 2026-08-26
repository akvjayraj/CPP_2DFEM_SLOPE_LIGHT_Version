using System;
using System.IO;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Detects Smith &amp; Griffiths geotechnical program ids routed by <c>fem_cpp.exe</c>
/// (first token in .dat, mirrors <c>library/solver/common.cpp</c>).
/// </summary>
public static class FemCppProgramRouter
{
    /// <summary>
    /// Returns resolved program id (e.g. <c>p64</c>) or null when not a fem_cpp geotech deck.
    /// </summary>
    public static string? ResolveProgramId(string datContent)
    {
        if (string.IsNullOrWhiteSpace(datContent))
            return null;

        var solverTag = ReadSolverTag(datContent);
        if (!string.IsNullOrEmpty(solverTag))
        {
            var fromTag = ResolveToken(solverTag);
            if (fromTag != null)
                return fromTag;
        }

        if (P610AnalysisOptionsHelper.HasAnalysisOptionsBlock(datContent)
            && P610AnalysisOptionsHelper.TryParse(datContent, out var ao)
            && P610AnalysisOptionsHelper.IsP64ExplicitDeck(ao))
        {
            var slopeRunner = P610AnalysisOptionsHelper.ResolveSlopeSrfRunner(ao, null);
            if (string.Equals(slopeRunner, "p64", StringComparison.OrdinalIgnoreCase))
                return "p64";
            return null;
        }

        var sanitized = CppAnalysisRunner.StripFortranFullLineComments(datContent);
        var first = ReadFirstToken(sanitized);
        if (string.IsNullOrEmpty(first))
            return null;

        var resolved = ResolveToken(first);
        if (resolved != null)
            return resolved;

        if (first != null && LooksLikeNumeric(first) && LooksLikeP64EmbankmentHeader(sanitized))
            return "p64";

        return null;
    }

    /// <summary>
    /// Ensures the .dat has a program id line for fem_cpp when loading raw Fortran-style p64 geometry.
    /// </summary>
    public static string EnsureProgramHeader(string datContent, string programId)
    {
        if (string.IsNullOrWhiteSpace(datContent))
            return datContent;

        var sanitized = CppAnalysisRunner.StripFortranFullLineComments(datContent);
        var first = ReadFirstToken(sanitized);
        if (!string.IsNullOrEmpty(ResolveToken(first ?? "")))
            return datContent;

        // Generalized decks (explicit mesh, AnalysisOptions) start with nels/nn or geometry
        // numbers after comments are stripped — fem_cpp needs a first data-line program id.
        if (!string.IsNullOrEmpty(programId) && ResolveToken(programId) != null)
            return programId + Environment.NewLine + datContent;

        if (programId == "p64" && first != null && LooksLikeNumeric(first) && LooksLikeP64EmbankmentHeader(sanitized))
            return "p64" + Environment.NewLine + datContent;

        return datContent;
    }

    private static string? ReadSolverTag(string content)
    {
        using var reader = new StringReader(content);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.Trim();
            const string prefix = "! <solver:";
            if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            var end = trimmed.IndexOf('>');
            if (end > prefix.Length)
                return trimmed.Substring(prefix.Length, end - prefix.Length).Trim();
        }
        return null;
    }

    private static string? ReadFirstToken(string content)
    {
        using var reader = new StringReader(content);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (line.Length == 0) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
                return parts[0];
        }
        return null;
    }

    private static bool LooksLikeNumeric(string token)
    {
        return double.TryParse(token, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _);
    }

  private static bool LooksLikeP64EmbankmentHeader(string content)
    {
        using var reader = new StringReader(content);
        string? line;
        int nonEmpty = 0;
        while ((line = reader.ReadLine()) != null && nonEmpty < 3)
        {
            line = line.Trim();
            if (line.Length == 0) continue;
            nonEmpty++;
            if (nonEmpty == 1)
            {
                var p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 5) return false;
            }
            else if (nonEmpty == 2)
            {
                var p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 4) return false;
            }
        }
        return nonEmpty >= 2;
    }

    private static string? ResolveToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        var t = token.Trim();
        if (t.Equals("slope_vp_8quad", StringComparison.OrdinalIgnoreCase)
            || t.Equals("slope_stability_p64", StringComparison.OrdinalIgnoreCase)
            || t.Equals("plane_strain_slope_mc", StringComparison.OrdinalIgnoreCase)
            || t.Equals("slope_q8_srf", StringComparison.OrdinalIgnoreCase)
            || t.Equals("slope_Q8_SRF", StringComparison.OrdinalIgnoreCase))
            return "p64";

        if (t.Equals("earth_pressure_mc", StringComparison.OrdinalIgnoreCase)
            || t.Equals("retaining_wall_mc", StringComparison.OrdinalIgnoreCase)
            || t.Equals("geotech_earth_pressure", StringComparison.OrdinalIgnoreCase))
            return "p65";

        if (t.Equals("embankment_layers_mc", StringComparison.OrdinalIgnoreCase)
            || t.Equals("staged_fill_mc", StringComparison.OrdinalIgnoreCase)
            || t.Equals("backfill_layers_mc", StringComparison.OrdinalIgnoreCase))
            return "p69";

        if (t.Equals("excavation_layers_mc", StringComparison.OrdinalIgnoreCase)
            || t.Equals("staged_excavation_mc", StringComparison.OrdinalIgnoreCase))
            return "p610";

        // Steady seepage (S&G Chapter 7)
        if (t.Equals("seepage_2d", StringComparison.OrdinalIgnoreCase)
            || t.Equals("steady_seepage_4node", StringComparison.OrdinalIgnoreCase)
            || t.Equals("seepage_plane_axisym", StringComparison.OrdinalIgnoreCase))
            return "p72";
        if (t.Equals("seepage_free_surface", StringComparison.OrdinalIgnoreCase)
            || t.Equals("flow_free_surface", StringComparison.OrdinalIgnoreCase))
            return "p73";
        if (t.Equals("seepage_general", StringComparison.OrdinalIgnoreCase)
            || t.Equals("seepage_general_2d", StringComparison.OrdinalIgnoreCase)
            || t.Equals("seepage_2d_3d", StringComparison.OrdinalIgnoreCase))
            return "p74";
        if (t.Equals("p72", StringComparison.OrdinalIgnoreCase)
            || t.Equals("p73", StringComparison.OrdinalIgnoreCase)
            || t.Equals("p74", StringComparison.OrdinalIgnoreCase))
            return t.ToLowerInvariant();

        if (t.Equals("p64", StringComparison.OrdinalIgnoreCase)
            || t.Equals("p65", StringComparison.OrdinalIgnoreCase)
            || t.Equals("p66", StringComparison.OrdinalIgnoreCase)
            || t.Equals("p67", StringComparison.OrdinalIgnoreCase)
            || t.Equals("p68", StringComparison.OrdinalIgnoreCase)
            || t.Equals("p69", StringComparison.OrdinalIgnoreCase)
            || t.Equals("p610", StringComparison.OrdinalIgnoreCase)
            || t.Equals("p611", StringComparison.OrdinalIgnoreCase)
            || t.Equals("p612", StringComparison.OrdinalIgnoreCase)
            || t.Equals("p613", StringComparison.OrdinalIgnoreCase))
            return t.ToLowerInvariant();

        return null;
    }
}
