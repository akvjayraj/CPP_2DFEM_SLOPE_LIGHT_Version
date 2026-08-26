using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GeotechSlopeApp.Views.Tabs;

public partial class GeometryVerticesPointPanel : UserControl
{
    private ViewModels.GeometryDefinitionViewModel? _subscribedVm;

    public GeometryVerticesPointPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
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

        UpdateColumnReadOnlyState(vm.IsGeometryVerticesEditMode);
        UpdateGeometryVerticesGridEditState(vm);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
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

        if (args.PropertyName == nameof(vm.IsGeometryVerticesEditMode))
        {
            UpdateColumnReadOnlyState(vm.IsGeometryVerticesEditMode);
            UpdateGeometryVerticesGridEditState(vm);
        }
        else if (args.PropertyName == nameof(vm.IsTunnelCircleDrawingMode))
        {
            if (vm.IsTunnelCircleDrawingMode)
            {
                if (vm.IsGeometryVerticesEditMode)
                {
                    vm.IsGeometryVerticesEditMode = false;
                }

                UpdateColumnReadOnlyState(false);
            }
            else
            {
                UpdateColumnReadOnlyState(vm.IsGeometryVerticesEditMode);
            }

            UpdateGeometryVerticesGridEditState(vm);
        }
    }

    private void UpdateGeometryVerticesGridEditState(ViewModels.GeometryDefinitionViewModel vm)
    {
        GeometryVerticesGrid.CanUserAddRows = vm.IsGeometryVerticesEditMode && !vm.IsTunnelCircleDrawingMode;
    }

    private void UpdateColumnReadOnlyState(bool isEditMode)
    {
        if (GeometryVerticesGrid.Columns.Count >= 5)
        {
            GeometryVerticesGrid.Columns[2].IsReadOnly = !isEditMode;
            GeometryVerticesGrid.Columns[3].IsReadOnly = !isEditMode;
        }
    }

    private void GeometryVerticesGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        if (DataContext is not ViewModels.GeometryDefinitionViewModel vm)
        {
            return;
        }

        if (!vm.IsGeometryVerticesEditMode || vm.IsTunnelCircleDrawingMode)
        {
            e.Cancel = true;
        }
    }

    private void GeometryVerticesGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid dataGrid || DataContext is not ViewModels.GeometryDefinitionViewModel vm)
        {
            return;
        }

        if (!vm.IsGeometryVerticesEditMode || vm.IsTunnelCircleDrawingMode)
        {
            return;
        }

        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var cell = FindParent<DataGridCell>(source);
        if (cell is null || cell.IsReadOnly || cell.Column is null)
        {
            return;
        }

        var row = FindParent<DataGridRow>(cell);
        if (row is not null)
        {
            row.IsSelected = true;
            row.Focus();
        }

        dataGrid.Focus();
        dataGrid.CurrentCell = new DataGridCellInfo(cell.DataContext, cell.Column);
        cell.Focus();
        if (!cell.IsEditing)
        {
            dataGrid.BeginEdit();
        }

        e.Handled = true;
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

    private void GeometryVerticesGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not DataGrid dataGrid || DataContext is not ViewModels.GeometryDefinitionViewModel vm)
        {
            return;
        }

        if (vm.IsTunnelCircleDrawingMode)
        {
            return;
        }

        if (e.Key == Key.Delete && vm.IsGeometryVerticesEditMode)
        {
            if (dataGrid.SelectedItem is ViewModels.GeometryDefinitionViewModel.GeometryVertexDisplay display)
            {
                var index = vm.GeometryVertexRows.IndexOf(display);
                if (index >= 0 && index < vm.GeometryVertices.Count)
                {
                    vm.RemoveGeometryVertexAtCommand.Execute(index);
                    e.Handled = true;
                }
            }
        }
    }

    private void GeometryVerticesGrid_AddingNewItem(object sender, AddingNewItemEventArgs e)
    {
        if (DataContext is not ViewModels.GeometryDefinitionViewModel vm)
        {
            return;
        }

        if (vm.IsTunnelCircleDrawingMode)
        {
            e.NewItem = null;
            return;
        }

        vm.AddGeometryVertexAtCommand.Execute(null);

        if (vm.GeometryVertexRows.Count > 0)
        {
            e.NewItem = vm.GeometryVertexRows[vm.GeometryVertexRows.Count - 1];
        }
    }

    private void GeometryVerticesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not ViewModels.GeometryDefinitionViewModel vm)
        {
            return;
        }

        if (GeometryVerticesGrid.SelectedItem is ViewModels.GeometryDefinitionViewModel.GeometryVertexDisplay selectedDisplay)
        {
            var index = vm.GeometryVertexRows.IndexOf(selectedDisplay);
            if (index >= 0)
            {
                vm.SetHighlightedVertices(new[] { index });
            }
        }
        else
        {
            vm.SetHighlightedVertices(Array.Empty<int>());
        }
    }
}
