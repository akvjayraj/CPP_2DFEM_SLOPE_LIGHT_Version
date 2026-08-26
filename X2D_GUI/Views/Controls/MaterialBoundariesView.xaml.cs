using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Views.Controls;

public partial class MaterialBoundariesView : UserControl
{
    public MaterialBoundariesView()
    {
        InitializeComponent();
        SlopeCanvasView.SharedTransformChanged += OnSharedTransformChanged;
    }

    private void OnSharedTransformChanged(Matrix matrix, double zoomFactor)
    {
        BoundaryTransform.Matrix = matrix;
        if (DataContext is GeometryDefinitionViewModel viewModel)
        {
            viewModel.UpdateZoom(zoomFactor);
        }
    }

    private void BoundaryCanvas_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is GeometryDefinitionViewModel viewModel && sender is IInputElement inputElement)
        {
            var position = e.GetPosition(inputElement);
            viewModel.AddBoundaryVertex(position);
        }
    }

    private void BoundaryCanvas_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (DataContext is GeometryDefinitionViewModel viewModel && sender is IInputElement inputElement)
        {
            var position = e.GetPosition(inputElement);
            viewModel.UpdateGeometryCanvasCoordinates(position);
        }
    }

    private void BoundaryCanvas_OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (DataContext is GeometryDefinitionViewModel viewModel)
        {
            viewModel.UpdateGeometryCanvasCoordinates(new Point(double.NaN, double.NaN));
        }
    }
}
