using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GeotechSlopeApp.Services.Appearance;

namespace GeotechSlopeApp.Views.Tabs;

public partial class GeometrySurfaceExcavationPanel : UserControl
{
    public static readonly DependencyProperty CompactWorkspaceModeProperty =
        DependencyProperty.Register(
            nameof(CompactWorkspaceMode),
            typeof(bool),
            typeof(GeometrySurfaceExcavationPanel),
            new PropertyMetadata(false));

    public bool CompactWorkspaceMode
    {
        get => (bool)GetValue(CompactWorkspaceModeProperty);
        set => SetValue(CompactWorkspaceModeProperty, value);
    }

    private ViewModels.GeometryDefinitionViewModel? _subscribedVm;

    public GeometrySurfaceExcavationPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        UiAppearanceService.Instance.PropertyChanged += OnAppearanceChanged;
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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SyncSystemChrome();
        if (DataContext is not ViewModels.GeometryDefinitionViewModel vm)
        {
            return;
        }

        if (!ReferenceEquals(_subscribedVm, vm))
        {
            if (_subscribedVm is not null)
            {
                _subscribedVm.PropertyChanged -= OnGeometryVmPropertyChanged;
            }

            _subscribedVm = vm;
            vm.PropertyChanged += OnGeometryVmPropertyChanged;
        }

        RefreshSurfaceGridComboBoxes(vm);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        UiAppearanceService.Instance.PropertyChanged -= OnAppearanceChanged;
        if (_subscribedVm is not null)
        {
            _subscribedVm.PropertyChanged -= OnGeometryVmPropertyChanged;
            _subscribedVm = null;
        }
    }

    private void OnGeometryVmPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is not ViewModels.GeometryDefinitionViewModel vm)
        {
            return;
        }

        if (args.PropertyName == nameof(vm.AvailableLineNumbers))
        {
            RefreshSurfaceGridComboBoxes(vm);
        }
    }

    private void RefreshSurfaceGridComboBoxes(ViewModels.GeometryDefinitionViewModel vm)
    {
        if (SurfaceGrid.Columns.Count < 5)
        {
            return;
        }

        if (SurfaceGrid.Columns[2] is DataGridComboBoxColumn colorColumn
            && !ReferenceEquals(colorColumn.ItemsSource, vm.SurfaceColorOptions))
        {
            colorColumn.ItemsSource = vm.SurfaceColorOptions;
        }

        // Line 1–5 use DataGridTemplateColumn; ComboBox ItemsSource comes from SurfaceLineComboBoxStyle on the DataGrid DataContext.
    }

    private void AddSurfaceToCanvasButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || DataContext is not ViewModels.GeometryDefinitionViewModel vm)
        {
            return;
        }

        var row = FindParent<DataGridRow>(button);
        if (row?.Item is Models.GeometrySurface surface)
        {
            SurfaceGrid.SelectedItem = surface;
            vm.DisplaySurfaceOnCanvasCommand.Execute(surface);
        }
    }

    private void DeleteExcavationOperationButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.GeometryDefinitionViewModel vm)
        {
            return;
        }

        if (ExcavationOperationsGrid.SelectedItem is not Models.BooleanOperationRow selectedOperation)
        {
            return;
        }

        var index = vm.ExcavationOperations.IndexOf(selectedOperation);
        if (index >= 0)
        {
            vm.DeleteExcavationOperationCommand.Execute(index);
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
}
