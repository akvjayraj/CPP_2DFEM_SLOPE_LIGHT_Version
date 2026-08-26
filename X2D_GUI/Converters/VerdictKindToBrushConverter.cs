using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.Converters;

/// <summary>
/// Maps a <see cref="VerificationVerdictKind"/> onto the foreground brush used
/// by the Verification Suite Browser column. Pass = green, Review = amber,
/// Fail = red, Unknown = slate. Mirrors the palette already used in
/// VerificationView.xaml's PassStatusStyle so the two grids feel consistent.
/// </summary>
public sealed class VerdictKindToBrushConverter : IValueConverter
{
    private static readonly Brush PassBrush = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69));
    private static readonly Brush ReviewBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06));
    private static readonly Brush FailBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
    private static readonly Brush UnknownBrush = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));

    static VerdictKindToBrushConverter()
    {
        PassBrush.Freeze();
        ReviewBrush.Freeze();
        FailBrush.Freeze();
        UnknownBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            VerificationVerdictKind.Pass => PassBrush,
            VerificationVerdictKind.Review => ReviewBrush,
            VerificationVerdictKind.Fail => FailBrush,
            _ => UnknownBrush,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
