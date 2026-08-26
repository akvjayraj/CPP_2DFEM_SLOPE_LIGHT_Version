using System.Globalization;
using System.Windows.Data;

namespace GeotechSlopeApp.Converters;

/// <summary>
/// Binds TextBox.Text to numeric model properties while allowing in-progress typing
/// (e.g. "19." before the user finishes "19.6"). Returns Binding.DoNothing when the
/// text is not yet a complete number so WPF does not revert the TextBox.
/// </summary>
public class NumericTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            double d  => d.ToString("G", CultureInfo.InvariantCulture),
            float f   => f.ToString("G", CultureInfo.InvariantCulture),
            decimal m => m.ToString("G", CultureInfo.InvariantCulture),
            int i     => i.ToString(CultureInfo.InvariantCulture),
            _         => "",
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = (value as string)?.Trim() ?? "";
        if (text.Length == 0)
            return 0.0;

        // "19." parses as 19.0 in .NET but must stay editable until fractional digits are typed.
        if (IsPartialNumericInput(text))
            return Binding.DoNothing;

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            return parsed;

        return Binding.DoNothing;
    }

    /// <summary>
    /// True when the user is mid-edit and the string should not be coerced to a number yet.
    /// </summary>
    internal static bool IsPartialNumericInput(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text.Trim();

        if (text is "-" or "+" or "." or "-." or "+.")
            return true;

        if (text.EndsWith(".") && !text.EndsWith(".."))
            return true;

        if (text.EndsWith("e", StringComparison.OrdinalIgnoreCase))
            return true;

        if (text.EndsWith("e-", StringComparison.OrdinalIgnoreCase)
            || text.EndsWith("e+", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
