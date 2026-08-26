using System.Windows;
using System.Windows.Controls;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Views.Tabs;

public partial class CreateTriangleMeshView : UserControl
{
    public CreateTriangleMeshView()
    {
        InitializeComponent();
    }

    private void CreateTriangleMeshView_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MeshViewModel vm)
            vm.RefreshTriangleMeshStructuredExtentsFromGeometry();
    }
}
