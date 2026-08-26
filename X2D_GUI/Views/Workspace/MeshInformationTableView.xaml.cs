using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GeotechSlopeApp.Services.Appearance;

namespace GeotechSlopeApp.Views.Workspace;

public partial class MeshInformationTableView : UserControl
{
    public MeshInformationTableView()
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
}
