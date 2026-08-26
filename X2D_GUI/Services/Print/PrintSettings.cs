using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GeotechSlopeApp.Services.Print;

public enum PrintPageOrientation
{
    Portrait,
    Landscape
}

public enum PrintScaleMode
{
    FitToPage,
    ActualSize
}

/// <summary>Session print options (fit-to-page, margins, header/footer).</summary>
public sealed class PrintSettings
{
    public PrintPageOrientation Orientation { get; set; } = PrintPageOrientation.Landscape;
    public double MarginLeftMm { get; set; } = 10;
    public double MarginRightMm { get; set; } = 10;
    public double MarginTopMm { get; set; } = 10;
    public double MarginBottomMm { get; set; } = 10;
    public PrintScaleMode ScaleMode { get; set; } = PrintScaleMode.FitToPage;
    public bool ShowHeader { get; set; } = true;
    public bool ShowFooter { get; set; } = true;
    public string HeaderFormat { get; set; } = "{ProjectName} — {TabName}";
    public string FooterFormat { get; set; } = "{Date}";

    public PrintSettings Clone() => new()
    {
        Orientation = Orientation,
        MarginLeftMm = MarginLeftMm,
        MarginRightMm = MarginRightMm,
        MarginTopMm = MarginTopMm,
        MarginBottomMm = MarginBottomMm,
        ScaleMode = ScaleMode,
        ShowHeader = ShowHeader,
        ShowFooter = ShowFooter,
        HeaderFormat = HeaderFormat,
        FooterFormat = FooterFormat
    };
}

/// <summary>Persists print settings under LocalAppData.</summary>
public static class PrintSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GeotechSlopeApp",
            "print_settings.json");

    public static PrintSettings Load()
    {
        try
        {
            var path = SettingsPath;
            if (!File.Exists(path))
                return new PrintSettings();

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<PrintSettings>(json, JsonOptions) ?? new PrintSettings();
        }
        catch
        {
            return new PrintSettings();
        }
    }

    public static void Save(PrintSettings settings)
    {
        try
        {
            var path = SettingsPath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // Non-critical persistence.
        }
    }
}
