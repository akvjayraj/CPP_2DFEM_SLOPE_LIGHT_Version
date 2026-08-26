using System.Globalization;
using System.Windows.Data;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.Converters;

public class MaterialTypeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is MaterialType materialType)
        {
            return materialType switch
            {
                MaterialType.Isotropic => "Isotropic",
                MaterialType.TransverselyIsotropic => "Transversely Isotropic",
                MaterialType.Orthotropic => "Orthotropic",
                MaterialType.DuncanChangHyperbolic => "Duncan-Chang Hyperbolic",
                MaterialType.NonlinearIsotropic => "Non-linear Isotropic",
                MaterialType.ViscoElastic => "Visco-Elastic",
                MaterialType.Plastic => "Plastic",
                _ => materialType.ToString()
            };
        }
        return value?.ToString() ?? "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string str)
        {
            return str switch
            {
                "Isotropic" => MaterialType.Isotropic,
                "Transversely Isotropic" => MaterialType.TransverselyIsotropic,
                "Orthotropic" => MaterialType.Orthotropic,
                "Duncan-Chang Hyperbolic" => MaterialType.DuncanChangHyperbolic,
                "Non-linear Isotropic" => MaterialType.NonlinearIsotropic,
                "Visco-Elastic" => MaterialType.ViscoElastic,
                "Plastic" => MaterialType.Plastic,
                _ => Binding.DoNothing
            };
        }
        return Binding.DoNothing;
    }
}
