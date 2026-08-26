using System.Windows.Media;

namespace GeotechSlopeApp.Models.FieldStations;

/// <summary>Shared swatch palette for Field Stations unit colours (same 16 colours as Material Properties).</summary>
public static class FieldStationUnitColours
{
    public static IReadOnlyList<Color> Palette { get; } = new[]
    {
        Color.FromRgb(173, 216, 230),
        Color.FromRgb(210, 180, 140),
        Color.FromRgb(144, 238, 144),
        Color.FromRgb(205, 133, 63),
        Color.FromRgb(188, 143, 143),
        Color.FromRgb(112, 128, 144),
        Color.FromRgb(70, 130, 180),
        Color.FromRgb(189, 183, 107),
        Color.FromRgb(240, 128, 128),
        Color.FromRgb(147, 112, 219),
        Color.FromRgb(100, 149, 237),
        Color.FromRgb(255, 165, 0),
        Color.FromRgb(46, 139, 87),
        Color.FromRgb(220, 20, 60),
        Color.FromRgb(245, 245, 220),
        Color.FromRgb(64, 224, 208)
    };

    public static bool IsUnset(Color colour) => colour.A == 0;

    /// <summary>First palette swatch not already used; cycles when the palette is exhausted.</summary>
    public static Color NextUnused(IEnumerable<Color> used)
    {
        var usedList = used.Where(c => !IsUnset(c)).ToList();
        var usedSet = usedList.ToHashSet();
        foreach (var candidate in Palette)
        {
            if (!usedSet.Contains(candidate))
                return candidate;
        }

        return Palette[usedList.Count % Palette.Count];
    }
}
