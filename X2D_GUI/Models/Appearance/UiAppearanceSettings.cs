using System.Windows.Media;

namespace GeotechSlopeApp.Models.Appearance;

public enum UiLayoutMode
{
    ClassicTabs,
    AnalysisWorkspace
}

public enum UiAppearancePreset
{
    ClassicLight,
    Light,
    Dark,
    Custom
}

/// <summary>
/// User-level Display / Appearance options (RS2-style). Persisted under LocalAppData,
/// not only in the .xfem, so the shell choice survives the next launch.
/// </summary>
public sealed class UiAppearanceSettings
{
    public UiLayoutMode LayoutMode { get; set; } = UiLayoutMode.ClassicTabs;
    public UiAppearancePreset Preset { get; set; } = UiAppearancePreset.Light;

    public string WindowBackground { get; set; } = "#FFFFFFFF";
    public string WindowForeground { get; set; } = "#FF111827";
    public string SecondaryText { get; set; } = "#FF6B7280";
    public string Accent { get; set; } = "#FF2563EB";
    public string ViewportPaper { get; set; } = "#FFFAFAFA";
    public string Grid { get; set; } = "#FFE5E7EB";
    public string PanelBackground { get; set; } = "#FFF3F4F6";
    public string Border { get; set; } = "#FFD1D5DB";

    public double ExplorerWidth { get; set; } = 240;
    public double ResultsWidth { get; set; } = 280;
    public double SolverLogHeight { get; set; } = 140;

    public UiAppearanceSettings Clone() => new()
    {
        LayoutMode = LayoutMode,
        Preset = Preset,
        WindowBackground = WindowBackground,
        WindowForeground = WindowForeground,
        SecondaryText = SecondaryText,
        Accent = Accent,
        ViewportPaper = ViewportPaper,
        Grid = Grid,
        PanelBackground = PanelBackground,
        Border = Border,
        ExplorerWidth = ExplorerWidth,
        ResultsWidth = ResultsWidth,
        SolverLogHeight = SolverLogHeight
    };
}

public static class UiAppearancePalettes
{
    public static UiAppearanceSettings ClassicLight() => new()
    {
        Preset = UiAppearancePreset.ClassicLight,
        WindowBackground = "#FFF8FAFC",
        WindowForeground = "#FF0F172A",
        SecondaryText = "#FF64748B",
        Accent = "#FF4F46E5",
        ViewportPaper = "#FFF8FAFC",
        Grid = "#FFCBD5E1",
        PanelBackground = "#FFF1F5F9",
        Border = "#FFCBD5E1"
    };

    public static UiAppearanceSettings Light() => new()
    {
        Preset = UiAppearancePreset.Light,
        WindowBackground = "#FFFFFFFF",
        WindowForeground = "#FF111827",
        SecondaryText = "#FF6B7280",
        Accent = "#FF2563EB",
        ViewportPaper = "#FFFAFAFA",
        Grid = "#FFE5E7EB",
        PanelBackground = "#FFF3F4F6",
        Border = "#FFD1D5DB"
    };

    public static UiAppearanceSettings Dark() => new()
    {
        Preset = UiAppearancePreset.Dark,
        WindowBackground = "#FF0F172A",
        WindowForeground = "#FFE2E8F0",
        SecondaryText = "#FF94A3B8",
        Accent = "#FF818CF8",
        ViewportPaper = "#FF111827",
        Grid = "#FF334155",
        PanelBackground = "#FF1E293B",
        Border = "#FF334155"
    };

    public static Color Parse(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return fallback;
        try
        {
            var converted = ColorConverter.ConvertFromString(hex);
            return converted is Color c ? c : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    public static string ToHex(Color color) =>
        $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>
    /// The Appearance page edits one "Window / panel background" hex. Named presets also
    /// keep a slightly different explorer panel color; Custom uses the window hex for both.
    /// </summary>
    public static void SyncPanelChrome(UiAppearanceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var src = settings.Preset switch
        {
            UiAppearancePreset.Light => Light(),
            UiAppearancePreset.Dark => Dark(),
            UiAppearancePreset.Custom => null,
            _ => ClassicLight()
        };
        if (src == null)
        {
            settings.PanelBackground = settings.WindowBackground;
            return;
        }

        settings.PanelBackground = src.PanelBackground;
        settings.Border = src.Border;
    }

    /// <summary>WCAG relative-luminance contrast ratio (1–21).</summary>
    public static double ContrastRatio(Color a, Color b)
    {
        var l1 = RelativeLuminance(a);
        var l2 = RelativeLuminance(b);
        var lighter = Math.Max(l1, l2);
        var darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(Color c)
    {
        static double Lin(byte channel)
        {
            var s = channel / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }
}
