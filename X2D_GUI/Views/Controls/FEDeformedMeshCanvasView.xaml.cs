using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models.FiniteElement;
using GeotechSlopeApp.ViewModels;
using GeotechSlopeApp.Visualization;

namespace GeotechSlopeApp.Views.Controls;

public partial class FEDeformedMeshCanvasView : UserControl
{
    private bool _agentLoggedDeformedRender;
    private Point _lastPanPoint;
    private bool _isPanning;
    private bool _isZoomBoxSelecting;
    private Point _zoomStart;
    private Rect _zoomRect;
    private const double MinZoomBoxSize = 10.0;
    private const double MinScale = 0.2;
    private const double MaxScale = 10.0;
    private double _renderScale = 1.0;
    private double _renderOffsetX, _renderOffsetY;
    private bool _axisTransformReady;

    private static readonly Brush DeformedStroke = new SolidColorBrush(Color.FromRgb(56, 189, 248));
    private static readonly Brush UndeformedStroke = Brushes.DimGray;

    public FEDeformedMeshCanvasView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && DataContext is ResultsViewModel vm)
                RenderDeformedMesh(vm);
        };
    }

    private void DeformedCanvas_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ZoomScaleTransform == null || ZoomTranslateTransform == null) return;
        var pos = e.GetPosition(ZoomPanBorder);
        double scale = e.Delta > 0 ? 1.15 : 1 / 1.15;
        double newScale = ZoomScaleTransform.ScaleX * scale;
        newScale = Math.Clamp(newScale, MinScale, MaxScale);
        double factor = newScale / ZoomScaleTransform.ScaleX;
        ZoomTranslateTransform.X = pos.X - (pos.X - ZoomTranslateTransform.X) * factor;
        ZoomTranslateTransform.Y = pos.Y - (pos.Y - ZoomTranslateTransform.Y) * factor;
        ZoomScaleTransform.ScaleX = newScale;
        ZoomScaleTransform.ScaleY = newScale;
        RefreshAxisOverlay();
        e.Handled = true;
    }

    private void DeformedCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _isZoomBoxSelecting = false;
            UpdateZoomRectVisual(Rect.Empty);
            ZoomPanBorder.ReleaseMouseCapture();
            ResetView();
            e.Handled = true;
            return;
        }

        if (e.ClickCount == 1)
        {
            _isZoomBoxSelecting = true;
            _zoomStart = e.GetPosition(ZoomPanBorder);
            _zoomRect = new Rect(_zoomStart, _zoomStart);
            UpdateZoomRectVisual(_zoomRect);
            ZoomPanBorder.CaptureMouse();
        }
    }

    private void DeformedCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            _isPanning = true;
            _lastPanPoint = e.GetPosition(ZoomPanBorder);
            ZoomPanBorder.CaptureMouse();
        }
    }

    private void DeformedCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning && ZoomTranslateTransform != null)
        {
            var pos = e.GetPosition(ZoomPanBorder);
            ZoomTranslateTransform.X += pos.X - _lastPanPoint.X;
            ZoomTranslateTransform.Y += pos.Y - _lastPanPoint.Y;
            _lastPanPoint = pos;
            RefreshAxisOverlay();
        }
        else if (_isZoomBoxSelecting && e.LeftButton == MouseButtonState.Pressed)
        {
            var current = e.GetPosition(ZoomPanBorder);
            _zoomRect = new Rect(_zoomStart, current);
            UpdateZoomRectVisual(_zoomRect);
        }
    }

    private void DeformedCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isZoomBoxSelecting)
        {
            _isZoomBoxSelecting = false;
            ZoomPanBorder.ReleaseMouseCapture();
            var rect = _zoomRect;
            UpdateZoomRectVisual(Rect.Empty);
            if (rect.Width >= MinZoomBoxSize && rect.Height >= MinZoomBoxSize)
            {
                ZoomToScreenRect(rect);
                RefreshAxisOverlay();
            }
        }
    }

    private void DeformedCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && _isPanning)
        {
            _isPanning = false;
            ZoomPanBorder.ReleaseMouseCapture();
            RefreshAxisOverlay();
        }
    }

    private void DeformedCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isPanning && Mouse.MiddleButton != MouseButtonState.Pressed)
        {
            _isPanning = false;
            ZoomPanBorder.ReleaseMouseCapture();
        }
        if (_isZoomBoxSelecting && Mouse.LeftButton != MouseButtonState.Pressed)
        {
            _isZoomBoxSelecting = false;
            UpdateZoomRectVisual(Rect.Empty);
            ZoomPanBorder.ReleaseMouseCapture();
        }
    }

    private void UpdateZoomRectVisual(Rect rect)
    {
        if (ZoomRect == null) return;
        if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0)
        {
            ZoomRect.Visibility = Visibility.Collapsed;
            return;
        }
        ZoomRect.Visibility = Visibility.Visible;
        ZoomRect.Width = rect.Width;
        ZoomRect.Height = rect.Height;
        Canvas.SetLeft(ZoomRect, rect.X);
        Canvas.SetTop(ZoomRect, rect.Y);
    }

    public void ResetView()
    {
        if (ZoomScaleTransform != null)
        {
            ZoomScaleTransform.ScaleX = 1.0;
            ZoomScaleTransform.ScaleY = 1.0;
        }
        if (ZoomTranslateTransform != null)
        {
            ZoomTranslateTransform.X = 0;
            ZoomTranslateTransform.Y = 0;
        }
        RefreshAxisOverlay();
    }

    private void ZoomToScreenRect(Rect rect)
    {
        if (ZoomScaleTransform == null || ZoomTranslateTransform == null
            || rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0)
            return;
        var viewportWidth = ZoomPanBorder.ActualWidth;
        var viewportHeight = ZoomPanBorder.ActualHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0) return;
        double currentScale = ZoomScaleTransform.ScaleX;
        double scaleFactor = Math.Min(viewportWidth / rect.Width, viewportHeight / rect.Height);
        double newScale = currentScale * scaleFactor;
        newScale = Math.Clamp(newScale, MinScale, MaxScale);
        var rectCenter = new Point(rect.X + rect.Width / 2.0, rect.Y + rect.Height / 2.0);
        var viewportCenter = new Point(viewportWidth / 2.0, viewportHeight / 2.0);
        double canvasRectCenterX = (rectCenter.X - ZoomTranslateTransform.X) / currentScale;
        double canvasRectCenterY = (rectCenter.Y - ZoomTranslateTransform.Y) / currentScale;
        ZoomScaleTransform.ScaleX = newScale;
        ZoomScaleTransform.ScaleY = newScale;
        ZoomTranslateTransform.X = viewportCenter.X - canvasRectCenterX * newScale;
        ZoomTranslateTransform.Y = viewportCenter.Y - canvasRectCenterY * newScale;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is ResultsViewModel viewModel)
            RenderDeformedMesh(viewModel);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ResultsViewModel viewModel)
        {
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
            RenderDeformedMesh(viewModel);
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResultsViewModel.DeformedMeshExaggeration) ||
            e.PropertyName == nameof(ResultsViewModel.ShowUndeformedOutline) ||
            e.PropertyName == nameof(ResultsViewModel.ShowMesh) ||
            e.PropertyName == nameof(ResultsViewModel.FENodes) ||
            e.PropertyName == nameof(ResultsViewModel.FEElements) ||
            e.PropertyName == nameof(ResultsViewModel.SelectedStageResult) ||
            e.PropertyName == nameof(ResultsViewModel.StagedAnalysisResult) ||
            e.PropertyName == nameof(ResultsViewModel.VisualizationMode) ||
            e.PropertyName == nameof(ResultsViewModel.ShowPhreaticOnDeformedMesh) ||
            e.PropertyName == nameof(ResultsViewModel.PhreaticSurfaceAxisPoints) ||
            e.PropertyName == nameof(ResultsViewModel.ShowAxisOfSymmetry))
        {
            if (sender is ResultsViewModel viewModel)
                RenderDeformedMesh(viewModel);
        }
    }

    private void RenderDeformedMesh(ResultsViewModel viewModel)
    {
        DeformedCanvas.Children.Clear();
        _axisTransformReady = false;
        FECanvasAxisOverlay.Clear(AxisOverlayCanvas);

        if (!viewModel.IsDeformedMeshMode)
            return;

        var nodes = viewModel.FENodes;
        var elements = viewModel.FEElements;
        if (nodes == null || elements == null || nodes.Count == 0 || elements.Count == 0)
            return;

        var (activeElements, inactiveElements) = PartitionElements(viewModel, elements);
        if (activeElements.Count == 0)
            return;

        double exaggeration = viewModel.DeformedMeshExaggeration;
        if (exaggeration < 1e-6)
            exaggeration = 1.0;

        var activeNodeIndices = new HashSet<int>();
        foreach (var element in activeElements)
        {
            foreach (var nodeId in element.NodeIds)
                activeNodeIndices.Add(nodeId);
        }

        var bounds = CalculateBounds(nodes, activeNodeIndices, exaggeration);
        var coordScale = CalculateScale(bounds);
        var offset = CalculateOffset(bounds, coordScale);
        _renderScale = coordScale;
        _renderOffsetX = offset.X;
        _renderOffsetY = offset.Y;
        _axisTransformReady = true;
        if (!_agentLoggedDeformedRender)
        {
            _agentLoggedDeformedRender = true;
            // #region agent log
            AgentDebugRuntimeLog.Write("pre-fix", "H3,H4,H5", "FEDeformedMeshCanvasView.xaml.cs:RenderDeformedMesh",
                "first deformed mesh render",
                new
                {
                    activeElementCount = activeElements.Count,
                    inactiveElementCount = inactiveElements.Count,
                    bounds = new { bounds.X, bounds.Y, bounds.Width, bounds.Height },
                    coordScale,
                    offset = new { offset.X, offset.Y },
                    visualization = AgentDebugRuntimeLog.SummarizeVisualization(nodes, elements, viewModel.VisualizationMode, exaggeration)
                });
            // #endregion
        }

        if (viewModel.ShowMesh)
        {
            if (viewModel.ShowUndeformedOutline)
            {
                FEMeshWireframeRenderer.AddWireframe(
                    DeformedCanvas, activeElements, nodes, coordScale, offset,
                    exaggeration: 0, UndeformedStroke, 0.7, dashed: true,
                    forceFullQuadraticPerimeter: false, viewModel.FeMeshSmithEmb2dQ8LocalOrder, ModelYToCanvas);
            }

            FEMeshWireframeRenderer.AddWireframe(
                DeformedCanvas, activeElements, nodes, coordScale, offset,
                exaggeration, DeformedStroke, 1.2, dashed: false,
                forceFullQuadraticPerimeter: true, viewModel.FeMeshSmithEmb2dQ8LocalOrder, ModelYToCanvas);

            if (inactiveElements.Count > 0)
            {
                FEMeshWireframeRenderer.AddWireframe(
                    DeformedCanvas, inactiveElements, nodes, coordScale, offset,
                    exaggeration: 0, Brushes.DimGray, 0.7, dashed: true,
                    forceFullQuadraticPerimeter: false, viewModel.FeMeshSmithEmb2dQ8LocalOrder, ModelYToCanvas);
            }

            if (viewModel.ShowPhreaticOnDeformedMesh && viewModel.PhreaticSurfaceAxisPoints.Count >= 2)
            {
                var phreatic = new Polyline
                {
                    Stroke = new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8)),
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 6, 4 },
                    Fill = Brushes.Transparent
                };
                foreach (var (ax, ay) in viewModel.PhreaticSurfaceAxisPoints)
                {
                    double cx = ax * coordScale + offset.X;
                    double cy = ModelYToCanvas(ay, coordScale, offset.Y);
                    phreatic.Points.Add(new Point(cx, cy));
                }
                DeformedCanvas.Children.Add(phreatic);
            }
        }

        RefreshAxisOverlay();
    }

    private void RefreshAxisOverlay()
    {
        if (!_axisTransformReady || AxisOverlayCanvas == null
            || ZoomScaleTransform == null || ZoomTranslateTransform == null)
        {
            FECanvasAxisOverlay.Clear(AxisOverlayCanvas);
            return;
        }

        var vw = ZoomPanBorder?.ActualWidth > 0 ? ZoomPanBorder.ActualWidth : DeformedCanvas.ActualWidth;
        var vh = ZoomPanBorder?.ActualHeight > 0 ? ZoomPanBorder.ActualHeight : DeformedCanvas.ActualHeight;
        FECanvasAxisOverlay.Redraw(
            AxisOverlayCanvas,
            vw,
            vh,
            new FECanvasAxisOverlay.ViewTransform(
                _renderScale,
                _renderOffsetX,
                _renderOffsetY,
                ZoomScaleTransform.ScaleX,
                ZoomTranslateTransform.X,
                ZoomTranslateTransform.Y),
            showAxisOfSymmetry: DataContext is ResultsViewModel results && results.ShowAxisOfSymmetry);
    }

    private static (List<ElementBase> active, List<ElementBase> inactive) PartitionElements(
        ResultsViewModel viewModel,
        IReadOnlyList<ElementBase> elements)
    {
        HashSet<int>? removedElementIds = null;
        if (viewModel.SelectedStageResult != null && viewModel.StagedAnalysisResult != null)
        {
            removedElementIds = new HashSet<int>();
            int selectedStageNumber = viewModel.SelectedStageResult.StageNumber;
            foreach (var stageResult in viewModel.StagedAnalysisResult.StageResults)
            {
                if (stageResult.StageNumber <= selectedStageNumber)
                {
                    foreach (var elementId in stageResult.RemovedElementIds)
                        removedElementIds.Add(elementId);
                }
            }
        }
        else if (viewModel.StagedAnalysisResult != null && viewModel.StagedAnalysisResult.StageResults.Count > 0)
        {
            removedElementIds = new HashSet<int>();
            foreach (var stageResult in viewModel.StagedAnalysisResult.StageResults)
            {
                foreach (var elementId in stageResult.RemovedElementIds)
                    removedElementIds.Add(elementId);
            }
        }

        var active = elements
            .Where(e => removedElementIds != null
                ? !removedElementIds.Contains(e.Id)
                : e.IsActive)
            .ToList();

        var inactive = removedElementIds != null
            ? elements.Where(e => removedElementIds.Contains(e.Id)).ToList()
            : elements.Where(e => !e.IsActive).ToList();

        return (active, inactive);
    }

    private static Rect CalculateBounds(
        IReadOnlyList<Node> nodes,
        HashSet<int> activeNodeIndices,
        double exaggeration)
    {
        if (activeNodeIndices.Count == 0)
            return new Rect(0, 0, 100, 100);

        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;

        foreach (int idx in activeNodeIndices)
        {
            if (idx < 0 || idx >= nodes.Count) continue;
            var node = nodes[idx];
            var (x0, y0) = FEMeshWireframeRenderer.NodePosition(node, 0);
            var (x1, y1) = FEMeshWireframeRenderer.NodePosition(node, exaggeration);
            minX = Math.Min(minX, Math.Min(x0, x1));
            maxX = Math.Max(maxX, Math.Max(x0, x1));
            minY = Math.Min(minY, Math.Min(y0, y1));
            maxY = Math.Max(maxY, Math.Max(y0, y1));
        }

        if (double.IsPositiveInfinity(minX))
            return new Rect(0, 0, 100, 100);

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private double CalculateScale(Rect bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return 1.0;

        double canvasWidth = DeformedCanvas.ActualWidth > 0 ? DeformedCanvas.ActualWidth : 800;
        double canvasHeight = DeformedCanvas.ActualHeight > 0 ? DeformedCanvas.ActualHeight : 600;

        double scaleX = (canvasWidth - 40) / bounds.Width;
        double scaleY = (canvasHeight - 40) / bounds.Height;

        return Math.Min(scaleX, scaleY);
    }

    private Point CalculateOffset(Rect bounds, double scale)
    {
        double canvasWidth = DeformedCanvas.ActualWidth > 0 ? DeformedCanvas.ActualWidth : 800;
        double canvasHeight = DeformedCanvas.ActualHeight > 0 ? DeformedCanvas.ActualHeight : 600;

        double scaledWidth = bounds.Width * scale;
        double scaledHeight = bounds.Height * scale;

        double offsetX = (canvasWidth - scaledWidth) / 2 - bounds.X * scale;
        double offsetY = (canvasHeight + scaledHeight) / 2 + bounds.Y * scale;

        return new Point(offsetX, offsetY);
    }

    private static double ModelYToCanvas(double modelY, double scale, double offsetY)
        => -modelY * scale + offsetY;

}
