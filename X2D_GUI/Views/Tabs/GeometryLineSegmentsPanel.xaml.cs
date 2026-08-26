using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GeotechSlopeApp.Services.Appearance;

namespace GeotechSlopeApp.Views.Tabs;

public partial class GeometryLineSegmentsPanel : UserControl
{
    public GeometryLineSegmentsPanel()
    {
        InitializeComponent();
        Loaded += (_, _) => SyncSystemChrome();
        UiAppearanceService.Instance.PropertyChanged += OnAppearanceChanged;
        Unloaded += (_, _) => UiAppearanceService.Instance.PropertyChanged -= OnAppearanceChanged;
    }

    private void OnAppearanceChanged(object? sender, PropertyChangedEventArgs e) => SyncSystemChrome();

    private void SyncSystemChrome()
    {
        CopyBrush(SystemColors.WindowBrushKey, "PanelBackgroundBrush");
        CopyBrush(SystemColors.ControlBrushKey, "PanelBackgroundBrush");
        CopyBrush(SystemColors.WindowTextBrushKey, "WindowForegroundBrush");
        CopyBrush(SystemColors.ControlTextBrushKey, "WindowForegroundBrush");
        CopyBrush(SystemColors.HighlightBrushKey, "ExplorerSelectionBrush");
        CopyBrush(SystemColors.HighlightTextBrushKey, "WindowForegroundBrush");
        CopyBrush(SystemColors.InactiveSelectionHighlightBrushKey, "ExplorerSelectionBrush");
        CopyBrush(SystemColors.InactiveSelectionHighlightTextBrushKey, "WindowForegroundBrush");
    }

    private void CopyBrush(object systemKey, string resourceKey)
    {
        if (TryFindResource(resourceKey) is Brush brush)
            Resources[systemKey] = brush;
    }

    private void ApplyTagButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || DataContext is not ViewModels.GeometryDefinitionViewModel vm)
        {
            return;
        }

        if (button.Parent is Panel panel)
        {
            var textBox = panel.Children.OfType<TextBox>().FirstOrDefault();
            textBox?.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }

        var segment = button.DataContext as Models.GeometryLineSegment
            ?? FindParent<DataGridRow>(button)?.Item as Models.GeometryLineSegment;
        if (segment != null)
        {
            segment.IsApplied = true;
            vm.SetHighlightedLineSegment(segment.LineNumber);
            LineSegmentsGrid.SelectedItem = segment;
        }
    }

    private static T? FindParent<T>(DependencyObject? child)
        where T : DependencyObject
    {
        var current = child;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void LineSegmentsGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.Item is not Models.GeometryLineSegment seg)
        {
            return;
        }

        var header = e.Column.Header?.ToString();
        if (header is not "From Point" and not "To Point")
        {
            return;
        }

        if (seg.IsApplied || seg.IsCircle)
        {
            e.Cancel = true;
        }
    }

    private void LineSegmentsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not ViewModels.GeometryDefinitionViewModel vm)
        {
            return;
        }

        if (LineSegmentsGrid.SelectedItem is Models.GeometryLineSegment selectedSegment)
        {
            vm.SetHighlightedLineSegment(selectedSegment.LineNumber);
        }
        else
        {
            vm.SetHighlightedLineSegment(null);
        }
    }
}
