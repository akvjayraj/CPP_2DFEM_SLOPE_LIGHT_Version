using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using GeotechSlopeApp.Models.Appearance;

namespace GeotechSlopeApp.Services.Appearance;

/// <summary>
/// Loads/saves Display options and writes DynamicResource brushes used by workspace chrome.
/// </summary>
public sealed class UiAppearanceService : INotifyPropertyChanged
{
    public const string WindowBackgroundBrushKey = "WindowBackgroundBrush";
    public const string WindowForegroundBrushKey = "WindowForegroundBrush";
    public const string SecondaryTextBrushKey = "SecondaryTextBrush";
    public const string AccentBrushKey = "AccentBrush";
    public const string ViewportPaperBrushKey = "ViewportPaperBrush";
    public const string GridLineBrushKey = "GridLineBrush";
    public const string PanelBackgroundBrushKey = "PanelBackgroundBrush";
    public const string BorderBrushKey = "ChromeBorderBrush";
    public const string ExplorerSelectionBrushKey = "ExplorerSelectionBrush";

    public const double MinimumContrastRatio = 4.5;

    private static readonly Lazy<UiAppearanceService> LazyInstance = new(() => new UiAppearanceService());
    public static UiAppearanceService Instance => LazyInstance.Value;

    private UiAppearanceSettings _settings = UiAppearancePalettes.Light();

    public UiAppearanceSettings Settings => _settings;

    public bool HasPoorContrast { get; private set; }
    public string ContrastWarning { get; private set; } = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Load()
    {
        try
        {
            if (File.Exists(StorePath))
            {
                var json = File.ReadAllText(StorePath);
                var loaded = JsonSerializer.Deserialize<UiAppearanceSettings>(json);
                if (loaded != null)
                    _settings = loaded;
            }
        }
        catch
        {
            _settings = UiAppearancePalettes.Light();
        }

        Apply();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(_settings, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        }
        catch
        {
            // Display options are non-fatal; keep the in-memory theme.
        }
    }

    public void ReplaceSettings(UiAppearanceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        Apply();
        Save();
        OnPropertyChanged(nameof(Settings));
    }

    public void ApplyPreset(UiAppearancePreset preset)
    {
        var layout = _settings.LayoutMode;
        var explorer = _settings.ExplorerWidth;
        var results = _settings.ResultsWidth;
        var log = _settings.SolverLogHeight;
        _settings = preset switch
        {
            UiAppearancePreset.Light => UiAppearancePalettes.Light(),
            UiAppearancePreset.Dark => UiAppearancePalettes.Dark(),
            UiAppearancePreset.Custom => _settings,
            _ => UiAppearancePalettes.ClassicLight()
        };
        _settings.Preset = preset;
        _settings.LayoutMode = layout;
        _settings.ExplorerWidth = explorer;
        _settings.ResultsWidth = results;
        _settings.SolverLogHeight = log;
        Apply();
        Save();
        OnPropertyChanged(nameof(Settings));
    }

    public void Apply()
    {
        UiAppearancePalettes.SyncPanelChrome(_settings);
        EvaluateContrast();
        var app = Application.Current;
        if (app?.Resources == null)
            return;

        SetBrush(app, WindowBackgroundBrushKey, _settings.WindowBackground, Color.FromRgb(248, 250, 252));
        SetBrush(app, WindowForegroundBrushKey, _settings.WindowForeground, Color.FromRgb(15, 23, 42));
        SetBrush(app, SecondaryTextBrushKey, _settings.SecondaryText, Color.FromRgb(100, 116, 139));
        SetBrush(app, AccentBrushKey, _settings.Accent, Color.FromRgb(79, 70, 229));
        SetBrush(app, ViewportPaperBrushKey, _settings.ViewportPaper, Color.FromRgb(248, 250, 252));
        SetBrush(app, GridLineBrushKey, _settings.Grid, Color.FromRgb(203, 213, 225));
        SetBrush(app, PanelBackgroundBrushKey, _settings.PanelBackground, Color.FromRgb(241, 245, 249));
        SetBrush(app, BorderBrushKey, _settings.Border, Color.FromRgb(203, 213, 225));
        var accent = UiAppearancePalettes.Parse(_settings.Accent, Color.FromRgb(79, 70, 229));
        var selection = _settings.Preset == UiAppearancePreset.Dark
            ? Color.FromRgb(30, 58, 95)
            : Color.FromArgb(0x33, accent.R, accent.G, accent.B);
        app.Resources[ExplorerSelectionBrushKey] = new SolidColorBrush(selection);
        RemapSystemColors(app.Resources);
    }

    /// <summary>
    /// Default ComboBox / popup chrome reads SystemColors. Keep those keys aligned with the
    /// active appearance palette so Dark fields stay dark and Classic Light stays light.
    /// </summary>
    public static void RemapSystemColors(ResourceDictionary resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        CopyAppBrush(resources, SystemColors.WindowBrushKey, ViewportPaperBrushKey);
        CopyAppBrush(resources, SystemColors.ControlBrushKey, ViewportPaperBrushKey);
        CopyAppBrush(resources, SystemColors.ControlLightBrushKey, ViewportPaperBrushKey);
        CopyAppBrush(resources, SystemColors.ControlLightLightBrushKey, ViewportPaperBrushKey);
        CopyAppBrush(resources, SystemColors.WindowTextBrushKey, WindowForegroundBrushKey);
        CopyAppBrush(resources, SystemColors.ControlTextBrushKey, WindowForegroundBrushKey);
        CopyAppBrush(resources, SystemColors.HighlightBrushKey, ExplorerSelectionBrushKey);
        CopyAppBrush(resources, SystemColors.HighlightTextBrushKey, WindowForegroundBrushKey);
        CopyAppBrush(resources, SystemColors.InactiveSelectionHighlightBrushKey, ExplorerSelectionBrushKey);
        CopyAppBrush(resources, SystemColors.InactiveSelectionHighlightTextBrushKey, WindowForegroundBrushKey);
    }

    private static void CopyAppBrush(ResourceDictionary resources, object systemKey, string themeKey)
    {
        if (resources[themeKey] is Brush brush)
            resources[systemKey] = brush;
    }

    public void EvaluateContrast()
    {
        var bg = UiAppearancePalettes.Parse(_settings.WindowBackground, Colors.White);
        var fg = UiAppearancePalettes.Parse(_settings.WindowForeground, Colors.Black);
        var ratio = UiAppearancePalettes.ContrastRatio(bg, fg);
        HasPoorContrast = ratio < MinimumContrastRatio;
        ContrastWarning = HasPoorContrast
            ? $"Text contrast is {ratio:0.0}:1 (need {MinimumContrastRatio:0.0}:1). Colors were kept; readability may be poor."
            : "";
        OnPropertyChanged(nameof(HasPoorContrast));
        OnPropertyChanged(nameof(ContrastWarning));
    }

    private static void SetBrush(Application app, string key, string hex, Color fallback)
    {
        var color = UiAppearancePalettes.Parse(hex, fallback);
        app.Resources[key] = new SolidColorBrush(color);
    }

    private static string StorePath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GeotechSlopeApp.Light");
            return Path.Combine(dir, "ui_appearance.json");
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
