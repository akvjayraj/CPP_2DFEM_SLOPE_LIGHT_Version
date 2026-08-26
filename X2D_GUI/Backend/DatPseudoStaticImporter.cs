using System;
using System.Globalization;
using System.Text.RegularExpressions;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>Reads k_h / k_v from Script Editor .dat AnalysisOptions or comment lines.</summary>
public static class DatPseudoStaticImporter
{
    /// <summary>
    /// Script → GUI: when <c>! k_h</c> / <c>! k_v</c> are present, enable staged analysis,
    /// ensure a Seismic / Dynamic stage row exists, and sync coefficients onto that row.
    /// When absent, clear script pseudo-static coefficients and zero k_h/k_v on seismic stage
    /// rows only (staged analysis remains enabled if the user turned it on).
    /// </summary>
    public static bool TryImportToSeismicStage(
        string? datContent,
        FEAnalysisViewModel? fe,
        StagedConstructionViewModel? staged,
        LoadConditionsViewModel? loads,
        out bool valuesChanged)
    {
        valuesChanged = false;
        if (!TryImportFromDat(datContent, out var k_h, out var k_v))
        {
            if (loads != null && fe?.HasSeismicDynamicStage != true)
                loads.SeismicAnalysisEnabled = false;
            loads?.ClearScriptPseudoStaticCoeffs();
            if (fe != null)
            {
                PseudoStaticSeismicRunHelper.ClearSeismicStageFromScriptImport(fe, out var cleared);
                if (cleared)
                    valuesChanged = true;
            }
            return false;
        }

        if (fe != null)
        {
            PseudoStaticSeismicRunHelper.EnsureSeismicStageFromScriptImport(
                fe, k_h, k_v, out valuesChanged);
            loads?.ClearScriptPseudoStaticCoeffs();
            return true;
        }

        if (loads == null)
            return false;

        loads.SetScriptPseudoStaticCoefficients(k_h, k_v);
        valuesChanged = true;
        return true;
    }

    public static bool TryImportFromDat(string? datContent, out double k_h, out double k_v)
    {
        k_h = 0.0;
        k_v = 0.0;
        if (string.IsNullOrWhiteSpace(datContent))
            return false;

        bool found = false;
        if (TryReadOption(datContent, "k_h", out var kh))
        {
            k_h = kh;
            found = true;
        }
        if (TryReadOption(datContent, "k_v", out var kv))
        {
            k_v = kv;
            found = true;
        }
        return found;
    }

    private static bool TryReadOption(string content, string key, out double value)
    {
        value = 0.0;
        var m = Regex.Match(
            content,
            $@"^\s*!\s*{Regex.Escape(key)}\s+([^\r\n!]+)",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (!m.Success) return false;
        return double.TryParse(m.Groups[1].Value.Trim(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);
    }
}
