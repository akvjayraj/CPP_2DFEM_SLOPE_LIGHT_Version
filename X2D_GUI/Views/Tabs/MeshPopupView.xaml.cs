using System.Windows;
using System.Windows.Controls;

namespace GeotechSlopeApp.Views.Tabs;

public partial class MeshPopupView : UserControl
{
    public MeshPopupView()
    {
        InitializeComponent();
    }

    private void ShowStandardMeshingHelp_Click(object sender, RoutedEventArgs e)
    {
        var instructions = "Standard Meshing Instructions\n\n" +
            "1. Configure mesh settings (Mesh Type, Element Type, etc.)\n" +
            "2. Click 'Discretize' to subdivide boundaries into discretizations (shown as red crosses)\n" +
            "3. Click 'Mesh' to generate the finite element mesh based on the discretization\n\n" +
            "Note: You must discretize before generating the mesh.";

        MessageBox.Show(
            instructions,
            "Standard Meshing Help",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
