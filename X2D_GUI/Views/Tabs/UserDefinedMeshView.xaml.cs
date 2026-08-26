using System.Windows.Controls;

namespace GeotechSlopeApp.Views.Tabs;

public partial class UserDefinedMeshView : UserControl
{
    public UserDefinedMeshView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is ViewModels.UserDefinedMeshViewModel vm)
                vm.RefreshRectangularExtentsFromGeometry();
        };
    }
}
