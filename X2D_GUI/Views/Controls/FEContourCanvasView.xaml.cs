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
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace GeotechSlopeApp.Views.Controls;

public partial class FEContourCanvasView : UserControl
{
    private const int ContourLevels = 20;
    private Point _lastPanPoint;
    private bool _isPanning;
    private bool _isZoomBoxSelecting;
    private Point _zoomStart;
    private Rect _zoomRect;
    private const double MinZoomBoxSize = 10.0;
    private const double MinScale = 0.2;
    private const double MaxScale = 10.0;

    // Cached render transforms for coordinate conversion
    private double _renderScale = 1.0;
    private double _renderOffsetX, _renderOffsetY;
    private bool _axisTransformReady;

    // Line plot picking state
    private bool _lpP1Set, _lpP2Set;
    private double _lpWx1, _lpWy1, _lpWx2, _lpWy2;
    private const int LinePlotSamples = 200;

    public FEContourCanvasView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && DataContext is ResultsViewModel vm)
                RenderContours(vm);
        };
    }

    private void ContourCanvas_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
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

    private void ContourCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
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

        if (DataContext is ResultsViewModel vm && vm.IsLinePlotMode)
        {
            var wp = ScreenToWorld(e.GetPosition(ZoomPanBorder));

            if (!_lpP1Set || _lpP2Set)
            {
                // Start a new line: set P1, clear previous
                _lpWx1 = wp.X; _lpWy1 = wp.Y;
                _lpP1Set = true;
                _lpP2Set = false;
                vm.LinePlotModel = null;
                vm.LinePlotHint = "Click second point on mesh";
            }
            else
            {
                // P1 already set — set P2 and compute
                _lpWx2 = wp.X; _lpWy2 = wp.Y;
                _lpP2Set = true;
                vm.LinePlotHint = "Click to start new line";
                ComputeLinePlot(vm);
            }

            RenderContours(vm);
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

    private void ContourCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            _isPanning = true;
            _lastPanPoint = e.GetPosition(ZoomPanBorder);
            ZoomPanBorder.CaptureMouse();
        }
    }

    private void ContourCanvas_MouseMove(object sender, MouseEventArgs e)
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

    private void ContourCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
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

    private void ContourCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && _isPanning)
        {
            _isPanning = false;
            ZoomPanBorder.ReleaseMouseCapture();
            RefreshAxisOverlay();
        }
    }

    private void ContourCanvas_MouseLeave(object sender, MouseEventArgs e)
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
        if (ZoomScaleTransform == null || ZoomTranslateTransform == null || rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0)
            return;
        var viewportWidth = ZoomPanBorder.ActualWidth;
        var viewportHeight = ZoomPanBorder.ActualHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0) return;
        double currentScale = ZoomScaleTransform.ScaleX;
        double scaleFactor = Math.Min(viewportWidth / rect.Width, viewportHeight / rect.Height);
        double newScale = currentScale * scaleFactor;
        newScale = Math.Clamp(newScale, MinScale, MaxScale);
        double appliedFactor = newScale / currentScale;
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
            RenderContours(viewModel);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ResultsViewModel viewModel)
        {
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
            RenderContours(viewModel);
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResultsViewModel.SelectedOutputType) ||
            e.PropertyName == nameof(ResultsViewModel.ContourFillMode) ||
            e.PropertyName == nameof(ResultsViewModel.FeNodalDisplacements) ||
            e.PropertyName == nameof(ResultsViewModel.FeElementStresses) ||
            e.PropertyName == nameof(ResultsViewModel.JointResults) ||
            e.PropertyName == nameof(ResultsViewModel.FENodes) ||
            e.PropertyName == nameof(ResultsViewModel.FEElements) ||
            e.PropertyName == nameof(ResultsViewModel.SelectedStageInfo) ||
            e.PropertyName == nameof(ResultsViewModel.SelectedStageResult) ||
            e.PropertyName == nameof(ResultsViewModel.StagedAnalysisResult) ||
            e.PropertyName == nameof(ResultsViewModel.FeMeshSmithEmb2dQ8LocalOrder) ||
            e.PropertyName == nameof(ResultsViewModel.SeepageOriginalDomainOutline) ||
            e.PropertyName == nameof(ResultsViewModel.ShowAxisOfSymmetry))
        {
            if (sender is ResultsViewModel vm)
                RenderContours(vm);
        }
        else if (e.PropertyName == nameof(ResultsViewModel.IsLinePlotMode))
        {
            if (sender is ResultsViewModel vm)
            {
                if (!vm.IsLinePlotMode)
                {
                    _lpP1Set = false;
                    _lpP2Set = false;
                }
                ZoomPanBorder.Cursor = vm.IsLinePlotMode ? Cursors.Cross : Cursors.Arrow;
                RenderContours(vm);
            }
        }
    }

    private void RenderContours(ResultsViewModel viewModel)
    {
        ContourCanvas.Children.Clear();
        _axisTransformReady = false;
        FECanvasAxisOverlay.Clear(AxisOverlayCanvas);

        var nodes = viewModel.FENodes;
        var elements = viewModel.FEElements;
        var outputType = viewModel.SelectedOutputType;

        if (nodes == null || elements == null || nodes.Count == 0 || elements.Count == 0)
            return;

        var activeElements = FEContourScaleHelper.GetContourElements(viewModel, outputType);

        if (activeElements.Count == 0)
            return;

        var bounds = CalculateBounds(nodes, viewModel.SeepageOriginalDomainOutline);
        var scale = CalculateScale(bounds);
        var offset = CalculateOffset(bounds, scale);

        _renderScale = scale;
        _renderOffsetX = offset.X;
        _renderOffsetY = offset.Y;
        _axisTransformReady = true;

        DrawP73UnsaturatedRegion(viewModel, nodes, scale, offset, drawOutlineOnly: false);

        if (ResultsViewModel.IsJointOutput(outputType))
        {
            DrawFaintMeshOutline(activeElements, nodes, viewModel.FeMeshSmithEmb2dQ8LocalOrder, scale, offset);
            DrawJointOverlay(viewModel, nodes, outputType, scale, offset);
            if (viewModel.IsLinePlotMode)
                DrawLinePlotOverlay();
            RefreshAxisOverlay();
            return;
        }

        if (viewModel.UsesSmoothContourBands)
        {
            RenderSmoothContourBands(viewModel, nodes, activeElements, outputType, scale, offset);
            DrawP73UnsaturatedRegion(viewModel, nodes, scale, offset, drawOutlineOnly: true);
            if (viewModel.IsLinePlotMode)
                DrawLinePlotOverlay();
            RefreshAxisOverlay();
            return;
        }

        bool discontinuousElementFill = ResultsViewModel.IsElementScalarOutput(outputType);
        double minValue, maxValue;

        if (discontinuousElementFill)
        {
            (minValue, maxValue) = FEContourScaleHelper.ComputeColorRange(viewModel, outputType, activeElements);
        }
        else
        {
            var nodeValues = GetNodeValues(nodes, activeElements, outputType, viewModel);
            if (nodeValues == null || nodeValues.Count == 0)
                return;

            minValue = nodeValues.Values.Min();
            maxValue = nodeValues.Values.Max();
            if (Math.Abs(maxValue - minValue) < 1e-10)
            {
                minValue -= 1e-6;
                maxValue += 1e-6;
            }
        }

        bool smithQ8 = viewModel.FeMeshSmithEmb2dQ8LocalOrder;
        if (discontinuousElementFill)
        {
            foreach (var element in activeElements)
            {
                var cornerIndices = FEMeshOutlineHelper.GetContourCornerNodeIndices(element, smithQ8);
                if (cornerIndices.Count < 3) continue;

                double elementValue = viewModel.GetElementScalarValue(element, outputType, nodes);
                RenderPolygonContour(element, cornerIndices, nodes, elementValue, minValue, maxValue, scale, offset);
            }
        }
        else
        {
            var nodeValues = GetNodeValues(nodes, activeElements, outputType, viewModel);
            if (nodeValues == null || nodeValues.Count == 0)
            {
                RefreshAxisOverlay();
                return;
            }

            foreach (var element in activeElements)
            {
                var cornerIndices = FEMeshOutlineHelper.GetContourCornerNodeIndices(element, smithQ8);
                if (cornerIndices.Count < 3) continue;

                RenderPolygonContour(element, cornerIndices, nodes, nodeValues, minValue, maxValue, scale, offset);
            }
        }

        DrawP73UnsaturatedRegion(viewModel, nodes, scale, offset, drawOutlineOnly: true);
        if (viewModel.IsLinePlotMode)
            DrawLinePlotOverlay();
        RefreshAxisOverlay();
    }

    private void DrawP73UnsaturatedRegion(
        ResultsViewModel viewModel,
        IReadOnlyList<Node> solvedNodes,
        double scale,
        Point offset,
        bool drawOutlineOnly)
    {
        var original = viewModel.SeepageOriginalDomainOutline;
        if (original.Count < 3 || solvedNodes.Count == 0)
            return;

        double originalTop = original.Max(p => p.Y);
        var freeSurface = solvedNodes
            .GroupBy(n => Math.Round(n.X, 9))
            .Select(g => (X: g.Average(n => n.X), Y: g.Max(n => n.Y)))
            .OrderBy(p => p.X)
            .ToList();
        if (freeSurface.Count < 2 || freeSurface.All(p => originalTop - p.Y <= 1e-8))
            return;

        if (!drawOutlineOnly)
        {
            var omitted = new PointCollection
            {
                new(original.Min(p => p.X) * scale + offset.X,
                    ModelYToCanvas(originalTop, scale, offset.Y)),
                new(original.Max(p => p.X) * scale + offset.X,
                    ModelYToCanvas(originalTop, scale, offset.Y)),
            };
            foreach (var p in freeSurface.OrderByDescending(p => p.X))
            {
                omitted.Add(new Point(
                    p.X * scale + offset.X,
                    ModelYToCanvas(p.Y, scale, offset.Y)));
            }
            ContourCanvas.Children.Add(new Polygon
            {
                Points = omitted,
                Fill = new SolidColorBrush(Color.FromRgb(238, 232, 218)),
                Stroke = null
            });
            return;
        }

        var originalOutline = new PointCollection(original.Select(p =>
            new Point(p.X * scale + offset.X, ModelYToCanvas(p.Y, scale, offset.Y))));
        ContourCanvas.Children.Add(new Polygon
        {
            Points = originalOutline,
            Fill = Brushes.Transparent,
            Stroke = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 6, 3 }
        });

        var surfaceLine = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromRgb(2, 132, 199)),
            StrokeThickness = 2.0
        };
        foreach (var p in freeSurface)
            surfaceLine.Points.Add(new Point(
                p.X * scale + offset.X,
                ModelYToCanvas(p.Y, scale, offset.Y)));
        ContourCanvas.Children.Add(surfaceLine);

        var labelPoint = freeSurface
            .OrderByDescending(p => originalTop - p.Y)
            .First();
        var label = new TextBlock
        {
            Text = "Unsaturated region (not solved by p73)",
            Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            Background = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)),
            FontSize = 11,
            Padding = new Thickness(4, 2, 4, 2)
        };
        Canvas.SetLeft(label, labelPoint.X * scale + offset.X + 6);
        Canvas.SetTop(label, ModelYToCanvas(
            (originalTop + labelPoint.Y) / 2.0, scale, offset.Y) - 10);
        ContourCanvas.Children.Add(label);
    }

    private void RefreshAxisOverlay()
    {
        if (!_axisTransformReady || AxisOverlayCanvas == null
            || ZoomScaleTransform == null || ZoomTranslateTransform == null)
        {
            FECanvasAxisOverlay.Clear(AxisOverlayCanvas);
            return;
        }

        var vw = ZoomPanBorder?.ActualWidth > 0 ? ZoomPanBorder.ActualWidth : ContourCanvas.ActualWidth;
        var vh = ZoomPanBorder?.ActualHeight > 0 ? ZoomPanBorder.ActualHeight : ContourCanvas.ActualHeight;
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

    private void RenderSmoothContourBands(
        ResultsViewModel viewModel,
        IReadOnlyList<Node> nodes,
        IReadOnlyList<ElementBase> activeElements,
        string outputType,
        double scale,
        Point offset)
    {
        var nodeValues = FEContourScaleHelper.BuildNodalScalarField(viewModel, nodes, activeElements, outputType);
        if (nodeValues == null || nodeValues.Count == 0)
            return;

        double minValue = nodeValues.Values.Min();
        double maxValue = nodeValues.Values.Max();
        if (Math.Abs(maxValue - minValue) < 1e-10)
        {
            minValue -= 1e-6;
            maxValue += 1e-6;
        }

        int pixelWidth = ContourCanvas.ActualWidth > 1 ? (int)ContourCanvas.ActualWidth : 800;
        int pixelHeight = ContourCanvas.ActualHeight > 1 ? (int)ContourCanvas.ActualHeight : 600;

        var image = FESmoothContourRenderer.BuildContourImage(
            nodes,
            activeElements,
            nodeValues,
            minValue,
            maxValue,
            scale,
            offset,
            pixelWidth,
            pixelHeight,
            viewModel.FeMeshSmithEmb2dQ8LocalOrder,
            ContourLevels);

        if (image != null)
            ContourCanvas.Children.Add(image);
    }

    // =========================================================================
    //  Line plot: coordinate helpers
    // =========================================================================

    private Point ScreenToWorld(Point screenPt)
    {
        if (ZoomScaleTransform == null || ZoomTranslateTransform == null || _renderScale < 1e-10)
            return new Point(0, 0);
        double canvasX = (screenPt.X - ZoomTranslateTransform.X) / ZoomScaleTransform.ScaleX;
        double canvasY = (screenPt.Y - ZoomTranslateTransform.Y) / ZoomScaleTransform.ScaleY;
        double worldX = (canvasX - _renderOffsetX) / _renderScale;
        // Y is flipped on the canvas (model y-up vs WPF Canvas y-down) — see ModelYToCanvas
        // / CalculateOffset for the forward mapping. Invert that here so the line-plot
        // picker reads back the model coordinates the user is clicking on.
        double worldY = (_renderOffsetY - canvasY) / _renderScale;
        return new Point(worldX, worldY);
    }

    // =========================================================================
    //  Line plot: overlay drawing
    // =========================================================================

    private void DrawLinePlotOverlay()
    {
        if (!_lpP1Set) return;

        // Mirror the contour-fill mapping (ModelYToCanvas) — y-flip so the marker
        // lands at the same screen pixel as the contour element directly beneath it.
        double cx1 = _lpWx1 * _renderScale + _renderOffsetX;
        double cy1 = ModelYToCanvas(_lpWy1, _renderScale, _renderOffsetY);
        AddLinePlotMarker(cx1, cy1, "A");

        if (_lpP2Set)
        {
            double cx2 = _lpWx2 * _renderScale + _renderOffsetX;
            double cy2 = ModelYToCanvas(_lpWy2, _renderScale, _renderOffsetY);

            // Line between A and B
            ContourCanvas.Children.Add(new Line
            {
                X1 = cx1, Y1 = cy1, X2 = cx2, Y2 = cy2,
                Stroke = Brushes.White,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 6, 3 }
            });
            // Dark shadow for contrast over light regions
            ContourCanvas.Children.Add(new Line
            {
                X1 = cx1, Y1 = cy1, X2 = cx2, Y2 = cy2,
                Stroke = Brushes.Black,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 6, 3 },
                StrokeDashOffset = 3
            });

            AddLinePlotMarker(cx2, cy2, "B");
        }
    }

    private void AddLinePlotMarker(double cx, double cy, string label)
    {
        const double r = 6;

        // Outer dark ring for contrast
        ContourCanvas.Children.Add(new Ellipse
        {
            Width = 2 * r + 4, Height = 2 * r + 4,
            Fill = Brushes.Black,
            Opacity = 0.5
        }.WithCanvasPosition(cx - r - 2, cy - r - 2));

        ContourCanvas.Children.Add(new Ellipse
        {
            Width = 2 * r, Height = 2 * r,
            Fill = Brushes.White,
            Stroke = Brushes.Black,
            StrokeThickness = 1.5
        }.WithCanvasPosition(cx - r, cy - r));

        var tb = new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = System.Windows.FontWeights.Bold,
            Foreground = Brushes.Black
        };
        Canvas.SetLeft(tb, cx + r + 3);
        Canvas.SetTop(tb, cy - 8);
        ContourCanvas.Children.Add(tb);
    }

    // =========================================================================
    //  Line plot: barycentric interpolation + OxyPlot model
    // =========================================================================

    private void ComputeLinePlot(ResultsViewModel vm)
    {
        var nodes = vm.FENodes;
        var elements = vm.FEElements;
        if (nodes == null || elements == null || nodes.Count == 0) return;

        var removedIds = GetRemovedElementIds(vm);
        var activeElements = elements
            .Where(e => removedIds != null
                ? !removedIds.Contains(e.Id)
                : e.IsActive)
            .ToList();

        var nodeValues = GetNodeValues(nodes, activeElements, vm.SelectedOutputType, vm);
        if (nodeValues == null || nodeValues.Count == 0)
        {
            vm.LinePlotModel = null;
            vm.LinePlotHint = "No scalar data on mesh — run analysis and pick a contour output.";
            return;
        }

        double dx = _lpWx2 - _lpWx1;
        double dy = _lpWy2 - _lpWy1;
        double totalDist = Math.Sqrt(dx * dx + dy * dy);
        if (totalDist < 1e-10) return;

        bool smithQ8 = vm.FeMeshSmithEmb2dQ8LocalOrder;
        var plotPoints = new List<(double dist, double val)>(LinePlotSamples + 1);

        for (int i = 0; i <= LinePlotSamples; i++)
        {
            double t = (double)i / LinePlotSamples;
            double px = _lpWx1 + t * dx;
            double py = _lpWy1 + t * dy;

            foreach (var element in activeElements)
            {
                if (TrySampleElementAt(element, px, py, nodes, smithQ8, nodeValues, out var val))
                {
                    plotPoints.Add((t * totalDist, val));
                    break;
                }
            }
        }

        if (plotPoints.Count < 2)
        {
            vm.LinePlotModel = null;
            vm.LinePlotHint = "Line does not cross any elements — pick both points inside the mesh.";
            return;
        }

        string yUnit = ResultsViewModel.GetScalarUnit(vm.SelectedOutputType);
        var model = new PlotModel
        {
            Title = StripTrailingUnitLabel(vm.SelectedOutputType, yUnit),
            TitleFontSize = 12,
            Background = OxyColors.White,
            IsLegendVisible = false
        };

        double xMin = plotPoints.Min(p => p.dist);
        double xMax = plotPoints.Max(p => p.dist);
        var xAxis = new LinearAxis { Position = AxisPosition.Bottom, Title = "Distance (m)" };
        ConfigureLinePlotAxis(xAxis, xMin, xMax, preferIntegerLabels: true);
        model.Axes.Add(xAxis);

        double yMin = plotPoints.Min(p => p.val);
        double yMax = plotPoints.Max(p => p.val);
        var yAxis = new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = FormatLinePlotAxisTitle(yUnit)
        };
        ConfigureLinePlotAxis(yAxis, yMin, yMax, preferIntegerLabels: false);
        model.Axes.Add(yAxis);

        var series = new LineSeries
        {
            Color = OxyColor.FromRgb(180, 0, 0),
            StrokeThickness = 2,
            MarkerType = MarkerType.None,
            CanTrackerInterpolatePoints = true,
            TrackerFormatString = BuildLinePlotTrackerFormat(yUnit)
        };
        foreach (var (d, v) in plotPoints)
            series.Points.Add(new DataPoint(d, v));
        model.Series.Add(series);

        vm.LinePlotModel = model;
        vm.LinePlotHint = "Hover the chart for x/y values · click mesh to start a new line";
    }

    /// <summary>Unit-only y-axis label so kPa/mm/etc. appear once (tick labels stay numeric).</summary>
    private static string FormatLinePlotAxisTitle(string unit)
        => string.IsNullOrEmpty(unit) || unit == "-" ? string.Empty : unit;

    private static string BuildLinePlotTrackerFormat(string unit)
    {
        var yLabel = string.IsNullOrEmpty(unit) || unit == "-" ? "Value" : unit;
        return $"Distance: {{2:0.####}} m\n{yLabel}: {{4:0.####}}";
    }

    private static string StripTrailingUnitLabel(string label, string unit)
    {
        if (string.IsNullOrEmpty(unit) || unit == "-") return label;
        var suffix = $" ({unit})";
        if (label.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return label[..^suffix.Length].TrimEnd();
        suffix = $"({unit})";
        if (label.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return label[..^suffix.Length].TrimEnd();
        return label;
    }

    /// <summary>Explicit major/minor steps so OxyPlot labels intermediate values (e.g. 5, 10, 15).</summary>
    private static void ConfigureLinePlotAxis(
        LinearAxis axis,
        double min,
        double max,
        bool preferIntegerLabels)
    {
        if (max <= min)
        {
            double pad = Math.Max(Math.Abs(max), 1.0) * 0.05;
            min -= pad;
            max += pad;
        }
        else
        {
            double pad = 0.04 * (max - min);
            min -= pad;
            max += pad;
        }

        axis.Minimum = min;
        axis.Maximum = max;
        if (min >= 0 && min > -0.01 * Math.Max(max, 1.0))
            axis.Minimum = 0;

        double span = axis.Maximum - axis.Minimum;
        double major = NicePlotStep(span / 10.0);
        if (major <= 0) major = NicePlotStep(span / 5.0);
        if (major <= 0) major = span / 5.0;
        axis.MajorStep = major;
        axis.MinorStep = major / 5.0;
        axis.MajorGridlineStyle = LineStyle.Dot;
        axis.MinorGridlineStyle = LineStyle.Dot;
        axis.MinorGridlineColor = OxyColor.FromArgb(48, 128, 128, 128);
        axis.IntervalLength = 22;
        axis.StringFormat = preferIntegerLabels
            ? (major >= 1 ? "0" : "0.##")
            : (major >= 10 ? "0" : major >= 1 ? "0.#" : "0.##");
    }

    private static double NicePlotStep(double raw)
    {
        if (raw <= 0 || double.IsNaN(raw) || double.IsInfinity(raw)) return 1;
        double exp = Math.Floor(Math.Log10(raw));
        double basePow = Math.Pow(10, exp);
        double frac = raw / basePow;
        double nice = frac switch
        {
            <= 1.0 => 1.0,
            <= 2.0 => 2.0,
            <= 2.5 => 2.5,
            <= 5.0 => 5.0,
            _ => 10.0
        };
        return nice * basePow;
    }

    private static bool TrySampleElementAt(
        ElementBase element,
        double px,
        double py,
        IReadOnlyList<Node> nodes,
        bool smithQ8,
        Dictionary<int, double> nodeValues,
        out double value)
    {
        value = 0;
        var cornerLocals = FEMeshOutlineHelper.GetContourCornerNodeIndices(element, smithQ8);
        if (cornerLocals.Count == 3)
            return TrySampleTriangle(px, py, nodes, nodeValues, element, cornerLocals, out value);

        if (cornerLocals.Count >= 4)
        {
            if (TrySampleTriangle(px, py, nodes, nodeValues, element,
                    new[] { cornerLocals[0], cornerLocals[1], cornerLocals[2] }, out value))
                return true;
            return TrySampleTriangle(px, py, nodes, nodeValues, element,
                new[] { cornerLocals[0], cornerLocals[2], cornerLocals[3] }, out value);
        }

        return false;
    }

    private static bool TrySampleTriangle(
        double px,
        double py,
        IReadOnlyList<Node> nodes,
        Dictionary<int, double> nodeValues,
        ElementBase element,
        IReadOnlyList<int> cornerLocals,
        out double value)
    {
        value = 0;
        if (cornerLocals.Count < 3) return false;

        int i0 = element.NodeIds[cornerLocals[0]];
        int i1 = element.NodeIds[cornerLocals[1]];
        int i2 = element.NodeIds[cornerLocals[2]];
        if (i0 < 0 || i0 >= nodes.Count || i1 < 0 || i1 >= nodes.Count || i2 < 0 || i2 >= nodes.Count)
            return false;

        var n0 = nodes[i0];
        var n1 = nodes[i1];
        var n2 = nodes[i2];

        var (inside, l0, l1, l2) = BarycentricCoords(
            px, py, n0.X, n0.Y, n1.X, n1.Y, n2.X, n2.Y);
        if (!inside) return false;

        double v0 = nodeValues.GetValueOrDefault(i0, 0);
        double v1 = nodeValues.GetValueOrDefault(i1, 0);
        double v2 = nodeValues.GetValueOrDefault(i2, 0);
        value = l0 * v0 + l1 * v1 + l2 * v2;
        return true;
    }

    private static (bool inside, double l0, double l1, double l2) BarycentricCoords(
        double px, double py,
        double x0, double y0, double x1, double y1, double x2, double y2)
    {
        double denom = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2);
        if (Math.Abs(denom) < 1e-20) return (false, 0, 0, 0);
        double l0 = ((y1 - y2) * (px - x2) + (x2 - x1) * (py - y2)) / denom;
        double l1 = ((y2 - y0) * (px - x2) + (x0 - x2) * (py - y2)) / denom;
        double l2 = 1.0 - l0 - l1;
        return (l0 >= -1e-6 && l1 >= -1e-6 && l2 >= -1e-6, l0, l1, l2);
    }

    // =========================================================================
    //  Helpers (shared with ComputeLinePlot)
    // =========================================================================

    private static HashSet<int>? GetRemovedElementIds(ResultsViewModel viewModel)
    {
        if (viewModel.SelectedStageResult != null && viewModel.StagedAnalysisResult != null)
        {
            var ids = new HashSet<int>();
            int sel = viewModel.SelectedStageResult.StageNumber;
            foreach (var sr in viewModel.StagedAnalysisResult.StageResults)
                if (sr.StageNumber <= sel)
                    foreach (var id in sr.RemovedElementIds) ids.Add(id);
            return ids;
        }
        if (viewModel.StagedAnalysisResult?.StageResults?.Count > 0)
        {
            var ids = new HashSet<int>();
            foreach (var sr in viewModel.StagedAnalysisResult.StageResults)
                foreach (var id in sr.RemovedElementIds) ids.Add(id);
            return ids;
        }
        return null;
    }

    private Dictionary<int, double>? GetNodeValues(
        IReadOnlyList<Node> nodes,
        IReadOnlyList<ElementBase> elements,
        string outputType,
        ResultsViewModel viewModel)
        => FEContourScaleHelper.BuildNodalScalarField(viewModel, nodes, elements, outputType);

    /// <summary>
    /// Fills a single element with a flat colour from a direct element scalar (discontinuous fill).
    /// </summary>
    private void RenderPolygonContour(
        ElementBase element,
        IReadOnlyList<int> cornerLocalIndices,
        IReadOnlyList<Node> nodes,
        double elementValue,
        double minValue, double maxValue,
        double scale, Point offset)
    {
        if (cornerLocalIndices.Count < 3) return;

        var points = new PointCollection();
        foreach (int localIdx in cornerLocalIndices)
        {
            if (localIdx < 0 || localIdx >= element.NodeIds.Length) return;
            int nodeId = element.NodeIds[localIdx];
            if (nodeId < 0 || nodeId >= nodes.Count) return;
            var node = nodes[nodeId];
            points.Add(new Point(node.X * scale + offset.X,
                                 ModelYToCanvas(node.Y, scale, offset.Y)));
        }

        ContourCanvas.Children.Add(new Polygon
        {
            Points = points,
            Fill = FEContourColorMap.GetBrushForValue(elementValue, minValue, maxValue),
            Stroke = Brushes.Black,
            StrokeThickness = 0.75
        });
    }

    /// <summary>
    /// Fills a single element with a flat colour derived from averaging
    /// the nodal values at ALL of its nodes (corners + midsides).
    /// </summary>
    private void RenderPolygonContour(
        ElementBase element,
        IReadOnlyList<int> cornerLocalIndices,
        IReadOnlyList<Node> nodes,
        Dictionary<int, double> nodeValues,
        double minValue, double maxValue,
        double scale, Point offset)
    {
        if (cornerLocalIndices.Count < 3) return;

        var points = new PointCollection();
        foreach (int localIdx in cornerLocalIndices)
        {
            if (localIdx < 0 || localIdx >= element.NodeIds.Length) return;
            int nodeId = element.NodeIds[localIdx];
            if (nodeId < 0 || nodeId >= nodes.Count) return;
            var node = nodes[nodeId];
            points.Add(new Point(node.X * scale + offset.X,
                                 ModelYToCanvas(node.Y, scale, offset.Y)));
        }

        double valueSum = 0.0;
        int valueCount = 0;
        foreach (var nodeId in element.NodeIds)
        {
            if (nodeId < 0 || nodeId >= nodes.Count) continue;
            valueSum += nodeValues.GetValueOrDefault(nodeId, 0);
            valueCount++;
        }
        if (valueCount == 0) return;

        ContourCanvas.Children.Add(new Polygon
        {
            Points = points,
            Fill = FEContourColorMap.GetBrushForValue(valueSum / valueCount, minValue, maxValue),
            Stroke = Brushes.Black,
            StrokeThickness = 0.75
        });
    }

    private void DrawFaintMeshOutline(
        IReadOnlyList<ElementBase> elements,
        IReadOnlyList<Node> nodes,
        bool smithQ8,
        double scale,
        Point offset)
    {
        foreach (var element in elements)
        {
            var cornerIndices = FEMeshOutlineHelper.GetContourCornerNodeIndices(element, smithQ8);
            if (cornerIndices.Count < 3) continue;

            var points = new PointCollection();
            bool ok = true;
            foreach (int localIdx in cornerIndices)
            {
                if (localIdx < 0 || localIdx >= element.NodeIds.Length)
                {
                    ok = false;
                    break;
                }

                int nodeId = element.NodeIds[localIdx];
                if (nodeId < 0 || nodeId >= nodes.Count)
                {
                    ok = false;
                    break;
                }

                var node = nodes[nodeId];
                points.Add(new Point(node.X * scale + offset.X, ModelYToCanvas(node.Y, scale, offset.Y)));
            }

            if (!ok || points.Count < 3) continue;

            ContourCanvas.Children.Add(new Polygon
            {
                Points = points,
                Fill = new SolidColorBrush(Color.FromArgb(24, 160, 160, 160)),
                Stroke = new SolidColorBrush(Color.FromArgb(80, 80, 80, 80)),
                StrokeThickness = 0.6
            });
        }
    }

    private void DrawJointOverlay(
        ResultsViewModel viewModel,
        IReadOnlyList<Node> nodes,
        string outputType,
        double scale,
        Point offset)
    {
        var joints = viewModel.JointResults;
        if (joints == null || joints.Count == 0)
            return;

        var (minValue, maxValue) = FEContourScaleHelper.ComputeJointColorRange(viewModel, outputType);
        double thickness = Math.Max(4.0, 0.035 * Math.Min(ContourCanvas.ActualWidth, ContourCanvas.ActualHeight));

        foreach (var joint in joints)
        {
            var (x1, y1, x2, y2) = P610T3JointKinematics.SegmentEnds(joint, nodes);
            double value = viewModel.GetJointScalarValue(joint, outputType);
            ContourCanvas.Children.Add(new Line
            {
                X1 = x1 * scale + offset.X,
                Y1 = ModelYToCanvas(y1, scale, offset.Y),
                X2 = x2 * scale + offset.X,
                Y2 = ModelYToCanvas(y2, scale, offset.Y),
                Stroke = FEContourColorMap.GetBrushForValue(value, minValue, maxValue),
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            });
        }
    }

    private Rect CalculateBounds(
        IReadOnlyList<Node> nodes,
        IReadOnlyList<(double X, double Y)>? additionalOutline = null)
    {
        if (nodes.Count == 0) return new Rect(0, 0, 100, 100);

        var validNodes = nodes.Where(n =>
            !double.IsNaN(n.X) && !double.IsNaN(n.Y) &&
            !double.IsInfinity(n.X) && !double.IsInfinity(n.Y)).ToList();

        if (validNodes.Count == 0) return new Rect(0, 0, 100, 100);

        double minX = validNodes.Min(n => n.X);
        double maxX = validNodes.Max(n => n.X);
        double minY = validNodes.Min(n => n.Y);
        double maxY = validNodes.Max(n => n.Y);

        if (additionalOutline is { Count: > 0 })
        {
            var validOutline = additionalOutline.Where(p =>
                double.IsFinite(p.X) && double.IsFinite(p.Y)).ToList();
            if (validOutline.Count > 0)
            {
                minX = Math.Min(minX, validOutline.Min(p => p.X));
                maxX = Math.Max(maxX, validOutline.Max(p => p.X));
                minY = Math.Min(minY, validOutline.Min(p => p.Y));
                maxY = Math.Max(maxY, validOutline.Max(p => p.Y));
            }
        }

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private double CalculateScale(Rect bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return 1.0;
        double canvasWidth  = ContourCanvas.ActualWidth  > 0 ? ContourCanvas.ActualWidth  : 800;
        double canvasHeight = ContourCanvas.ActualHeight > 0 ? ContourCanvas.ActualHeight : 600;
        return Math.Min((canvasWidth - 40) / bounds.Width, (canvasHeight - 40) / bounds.Height);
    }

    private Point CalculateOffset(Rect bounds, double scale)
    {
        double canvasWidth  = ContourCanvas.ActualWidth  > 0 ? ContourCanvas.ActualWidth  : 800;
        double canvasHeight = ContourCanvas.ActualHeight > 0 ? ContourCanvas.ActualHeight : 600;
        double offsetX = (canvasWidth  - bounds.Width  * scale) / 2 - bounds.X * scale;
        // Y-flip: canvas_y = -model_y * scale + offsetY (see ModelYToCanvas). This offset
        // value makes the largest model-Y (e.g. the free ground surface) land at the
        // smallest canvas-Y (top of the screen), and the smallest model-Y (fixed base)
        // at the largest canvas-Y (bottom of the screen) — matching the convention used
        // by every standard FE post-processor (Plaxis, Abaqus/Viewer, RS2, ParaView).
        double offsetY = (canvasHeight + bounds.Height * scale) / 2 + bounds.Y * scale;
        return new Point(offsetX, offsetY);
    }

    /// <summary>
    /// Forward model-Y &rarr; canvas-Y mapping for the contour view. WPF Canvas has
    /// (0,0) in the top-left with Y growing downward, but the FE mesh uses the
    /// standard "Y up" convention with the free ground surface at the largest model-Y.
    /// Flipping the sign here keeps the surface visually at the top of the canvas and
    /// the fixed base at the bottom, the same way every FE post-processor renders it.
    /// </summary>
    private static double ModelYToCanvas(double modelY, double scale, double offsetY)
        => -modelY * scale + offsetY;

}

// Tiny extension to set Canvas.Left/Top fluently (keeps AddLinePlotMarker concise)
file static class CanvasExtensions
{
    internal static T WithCanvasPosition<T>(this T element, double left, double top) where T : UIElement
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        return element;
    }
}
