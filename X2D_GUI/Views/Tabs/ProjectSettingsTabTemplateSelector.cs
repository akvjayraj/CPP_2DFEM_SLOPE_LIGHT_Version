using System.Windows;
using System.Windows.Controls;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.Views.Tabs;

public class ProjectSettingsTabTemplateSelector : DataTemplateSelector
{
    public DataTemplate? GeneralTemplate { get; set; }
    public DataTemplate? AnalysisTypeTemplate { get; set; }
    public DataTemplate? AppearanceTemplate { get; set; }
    public DataTemplate? GridAxesTemplate { get; set; }
    public DataTemplate? FieldStationsTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is not ProjectSettingsTabDescriptor tab)
            return base.SelectTemplate(item, container);

        return tab.Kind switch
        {
            ProjectSettingsTabKind.General => GeneralTemplate,
            ProjectSettingsTabKind.AnalysisType => AnalysisTypeTemplate,
            ProjectSettingsTabKind.Appearance => AppearanceTemplate,
            ProjectSettingsTabKind.GridAxes => GridAxesTemplate,
            ProjectSettingsTabKind.FieldStations => FieldStationsTemplate,
            _ => base.SelectTemplate(item, container)
        };
    }
}
