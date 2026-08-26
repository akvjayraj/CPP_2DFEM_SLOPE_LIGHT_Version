using System.Windows;
using System.Windows.Controls;
using GeotechSlopeApp.ViewModels.Dialogs;

namespace GeotechSlopeApp.Views.Dialogs;

public sealed class AnalysisTypeCardIconSelector : DataTemplateSelector
{
    public DataTemplate? StaticTemplate { get; set; }
    public DataTemplate? SeepageTemplate { get; set; }
    public DataTemplate? ConsolidationTemplate { get; set; }
    public DataTemplate? DynamicTemplate { get; set; }
    public DataTemplate? ThermalTemplate { get; set; }
    public DataTemplate? CoupledTemplate { get; set; }
    public DataTemplate? SrfTemplate { get; set; }
    public DataTemplate? StagesTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is not AnalysisTypeCard card)
            return base.SelectTemplate(item, container);
        return card.Key switch
        {
            "Seepage" => SeepageTemplate,
            "Consolidation" => ConsolidationTemplate,
            "Dynamic" => DynamicTemplate,
            "Thermal" => ThermalTemplate,
            "Coupled" => CoupledTemplate,
            "Srf" => SrfTemplate,
            "Stages" => StagesTemplate,
            _ => StaticTemplate
        };
    }
}
