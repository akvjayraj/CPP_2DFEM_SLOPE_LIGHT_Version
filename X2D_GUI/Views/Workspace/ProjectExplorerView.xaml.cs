using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GeotechSlopeApp.Services.Appearance;
using GeotechSlopeApp.ViewModels.Workspace;

namespace GeotechSlopeApp.Views.Workspace;

public partial class ProjectExplorerView : UserControl
{
    public ProjectExplorerView()
    {
        InitializeComponent();
        Loaded += (_, _) => SyncSystemChrome();
        UiAppearanceService.Instance.PropertyChanged += OnAppearanceChanged;
        Unloaded += (_, _) => UiAppearanceService.Instance.PropertyChanged -= OnAppearanceChanged;
    }

    private void OnAppearanceChanged(object? sender, PropertyChangedEventArgs e) => SyncSystemChrome();

    /// <summary>
    /// Default TreeView/ListBox chrome uses SystemColors.WindowBrush (white). Keep those
    /// keys in lock-step with the active panel so Dark text never sits on a light viewport.
    /// </summary>
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

    private void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is ProjectExplorerViewModel vm && e.NewValue is ProjectExplorerNode node)
            vm.SelectedNode = node;
    }
}
