using System.Globalization;
using System.Windows.Data;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.Converters;

public class FailureCriterionConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is FailureCriterion criterion)
        {
            return criterion switch
            {
                FailureCriterion.Elastic => "Elastic",
                FailureCriterion.ElastoPlastic => "Elasto-Plastic",
                FailureCriterion.MohrCoulomb => "Mohr-Coulomb",
                FailureCriterion.HoekBrown => "Hoek-Brown",
                FailureCriterion.DruckerPrager => "Drucker-Prager",
                FailureCriterion.GeneralizedHoekBrown => "Generalized Hoek-Brown",
                _ => criterion.ToString()
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
                "Elastic" => FailureCriterion.Elastic,
                "Elasto-Plastic" => FailureCriterion.ElastoPlastic,
                "Mohr-Coulomb" => FailureCriterion.MohrCoulomb,
                "Hoek-Brown" => FailureCriterion.HoekBrown,
                "Drucker-Prager" => FailureCriterion.DruckerPrager,
                "Generalized Hoek-Brown" => FailureCriterion.GeneralizedHoekBrown,
                _ => Binding.DoNothing
            };
        }
        return Binding.DoNothing;
    }
}
