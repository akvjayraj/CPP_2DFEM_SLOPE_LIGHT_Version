using System.Windows.Controls;
using System.Windows;

namespace GeotechSlopeApp.Views.Tabs;

public partial class Create1DMeshView : UserControl
{
    public Create1DMeshView()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MeshViewModel vm)
            vm.RefreshLengthFromGeometry();
    }
}
