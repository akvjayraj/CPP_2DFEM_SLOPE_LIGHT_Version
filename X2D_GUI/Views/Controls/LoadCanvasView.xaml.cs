using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Views.Controls;

public partial class LoadCanvasView : UserControl
{
    public LoadCanvasView()
    {
        InitializeComponent();
        SlopeCanvasView.SharedTransformChanged += OnSharedTransformChanged;
    }

    private void OnSharedTransformChanged(Matrix matrix, double zoomFactor)
    {
        LoadGeometryTransform.Matrix = matrix;
        if (DataContext is LoadConditionsViewModel viewModel)
        {
            viewModel.GeometryDefinition.UpdateViewTransform(matrix, zoomFactor);
        }
    }

    private Point ScreenToWorld(Point screenPoint)
    {
        var matrix = LoadGeometryTransform.Matrix;
        if (!matrix.HasInverse)
            return screenPoint;

        matrix.Invert();
        return matrix.Transform(screenPoint);
    }

    private void LoadCanvas_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is LoadConditionsViewModel viewModel && sender is IInputElement inputElement)
        {
            var position = ScreenToWorld(e.GetPosition(inputElement));
            viewModel.RegisterLoadPoint(position);
        }
    }

    private void LoadCanvas_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (DataContext is LoadConditionsViewModel viewModel && sender is IInputElement inputElement)
        {
            var position = ScreenToWorld(e.GetPosition(inputElement));
            viewModel.GeometryDefinition.UpdateGeometryCanvasCoordinates(position);
        }
    }

    private void LoadCanvas_OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (DataContext is LoadConditionsViewModel viewModel)
        {
            viewModel.GeometryDefinition.UpdateGeometryCanvasCoordinates(new Point(double.NaN, double.NaN));
        }
    }
}
