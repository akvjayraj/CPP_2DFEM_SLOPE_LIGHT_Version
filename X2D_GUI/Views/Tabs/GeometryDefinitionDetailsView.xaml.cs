using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GeotechSlopeApp.Views.Tabs;

public partial class GeometryDefinitionDetailsView : UserControl
{
    public GeometryDefinitionDetailsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.GeometryDefinitionViewModel vm)
        {
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(vm.IsTunnelCirclesEditMode))
                {
                    if (EditTunnelCirclesButton != null)
                    {
                        EditTunnelCirclesButton.Content = vm.IsTunnelCirclesEditMode ? "Done" : "Edit";
                    }

                    UpdateTunnelCircleColumnsEditState(vm.IsTunnelCirclesEditMode);
                }
            };

            UpdateTunnelCircleColumnsEditState(vm.IsTunnelCirclesEditMode);
        }
    }

    private void UpdateTunnelCircleColumnsEditState(bool isEditMode)
    {
        if (TunnelCirclesGrid.Columns.Count >= 5)
        {
            TunnelCirclesGrid.Columns[1].IsReadOnly = !isEditMode;
            TunnelCirclesGrid.Columns[2].IsReadOnly = !isEditMode;
            TunnelCirclesGrid.Columns[3].IsReadOnly = !isEditMode;
        }
    }

    private void DeleteTunnelCircleButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.GeometryDefinitionViewModel vm)
        {
            return;
        }

        if (TunnelCirclesGrid.SelectedItem is not Models.TunnelExcavationCircleRow selectedRow)
        {
            return;
        }

        var index = vm.TunnelExcavationCircleRows.IndexOf(selectedRow);
        if (index >= 0)
        {
            vm.DeleteTunnelCircleCommand.Execute(index);
        }
    }

    private void TunnelCirclesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not ViewModels.GeometryDefinitionViewModel vm)
        {
            return;
        }

        if (TunnelCirclesGrid.SelectedItem is Models.TunnelExcavationCircleRow selectedRow)
        {
            vm.SetHighlightedTunnelCircle(selectedRow.CircleNumber);
        }
        else
        {
            vm.SetHighlightedTunnelCircle(null);
        }
    }
}
