using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using GeotechSlopeApp.Models.FiniteElement;
using GeotechSlopeApp.ViewModels;
using GeotechSlopeApp.Visualization;

namespace GeotechSlopeApp.Views.Controls;

/// <summary>
/// 1D bar/rod results: position along member vs scalar field as color strips and profile polyline.
/// </summary>
public partial class FEOneDContourCanvasView : UserControl
{
    private Point _lastPanPoint;
    private bool _isPanning;
    private bool _isZoomBoxSelecting;
    private Point _zoomStart;
    private Rect _zoomRect;
    private const double MinZoomBoxSize = 10.0;
    private const double MinScale = 0.2;
    private const double MaxScale = 10.0;

    public FEOneDContourCanvasView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && DataContext is ResultsViewModel vm)
                RenderOneD(vm);
        };
    }

    private void OneDContour_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
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
        e.Handled = true;
    }

    private void OneDContour_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
        {
            _isZoomBoxSelecting = true;
            _zoomStart = e.GetPosition(ZoomPanBorder);
            _zoomRect = new Rect(_zoomStart, _zoomStart);
            UpdateZoomRectVisual(_zoomRect);
            ZoomPanBorder.CaptureMouse();
        }
    }

    private void OneDContour_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            _isPanning = true;
            _lastPanPoint = e.GetPosition(ZoomPanBorder);
            ZoomPanBorder.CaptureMouse();
        }
    }

    private void OneDContour_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning && ZoomTranslateTransform != null)
        {
            var pos = e.GetPosition(ZoomPanBorder);
            ZoomTranslateTransform.X += pos.X - _lastPanPoint.X;
            ZoomTranslateTransform.Y += pos.Y - _lastPanPoint.Y;
            _lastPanPoint = pos;
        }
        else if (_isZoomBoxSelecting && e.LeftButton == MouseButtonState.Pressed)
        {
            var current = e.GetPosition(ZoomPanBorder);
            _zoomRect = new Rect(_zoomStart, current);
            UpdateZoomRectVisual(_zoomRect);
        }
    }

    private void OneDContour_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isZoomBoxSelecting)
        {
            _isZoomBoxSelecting = false;
            ZoomPanBorder.ReleaseMouseCapture();
            var rect = _zoomRect;
            UpdateZoomRectVisual(Rect.Empty);
            if (rect.Width >= MinZoomBoxSize && rect.Height >= MinZoomBoxSize)
                ZoomToScreenRect(rect);
        }
    }

    private void OneDContour_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && _isPanning)
        {
            _isPanning = false;
            ZoomPanBorder.ReleaseMouseCapture();
        }
    }

    private void OneDContour_MouseLeave(object sender, MouseEventArgs e)
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
            RenderOneD(viewModel);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ResultsViewModel viewModel)
        {
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
            RenderOneD(viewModel);
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResultsViewModel.SelectedOutputType) ||
            e.PropertyName == nameof(ResultsViewModel.FeNodalDisplacements) ||
            e.PropertyName == nameof(ResultsViewModel.FeElementStresses) ||
            e.PropertyName == nameof(ResultsViewModel.FENodes) ||
            e.PropertyName == nameof(ResultsViewModel.FEElements) ||
            e.PropertyName == nameof(ResultsViewModel.ProjectDimension))
        {
            if (sender is ResultsViewModel viewModel)
                RenderOneD(viewModel);
        }
    }

    private void RenderOneD(ResultsViewModel viewModel)
    {
        OneDContourCanvas.Children.Clear();

        var nodes = viewModel.FENodes;
        var elements = viewModel.FEElements;
        var outputType = viewModel.SelectedOutputType;

        if (nodes == null || elements == null || nodes.Count == 0 || elements.Count == 0)
            return;

        var order = GetOrderedNodeIndices(nodes, elements);
        if (order == null || order.Count < 2)
            return;

        if (!TryBuildSegments(nodes, elements, order, outputType, viewModel, out var segments, out double minV, out double maxV))
            return;

        if (Math.Abs(maxV - minV) < 1e-14)
        {
            minV -= 1e-6;
            maxV += 1e-6;
        }

        double sMin = segments[0].S0;
        double sMax = segments[^1].S1;
        if (Math.Abs(sMax - sMin) < 1e-14)
        {
            sMin -= 1e-6;
            sMax += 1e-6;
        }

        double cw = OneDContourCanvas.ActualWidth > 0 ? OneDContourCanvas.ActualWidth : 800;
        double ch = OneDContourCanvas.ActualHeight > 0 ? OneDContourCanvas.ActualHeight : 600;
        const double padL = 56, padR = 24, padT = 36, padB = 52;
        double plotW = Math.Max(80, cw - padL - padR);
        double plotH = Math.Max(80, ch - padT - padB);

        double MapS(double s) => padL + (s - sMin) / (sMax - sMin) * plotW;
        double MapV(double v) => padT + (1 - (v - minV) / (maxV - minV)) * plotH;

        // Baseline (zero / reference band bottom)
        double yBase = padT + plotH;

        foreach (var seg in segments)
        {
            double sx0 = MapS(seg.S0);
            double sx1 = MapS(seg.S1);
            if (sx1 < sx0)
                (sx0, sx1) = (sx1, sx0);

            double vForColor = seg.IsStressElement
                ? seg.ElementValue
                : 0.5 * (seg.V0 + seg.V1);

            var fill = FEContourColorMap.GetBrushForValue(vForColor, minV, maxV);

            if (seg.IsStressElement)
            {
                var rect = new Rectangle
                {
                    Width = Math.Max(1, sx1 - sx0),
                    Height = plotH,
                    Fill = fill,
                    Stroke = Brushes.Black,
                    StrokeThickness = 0.35
                };
                Canvas.SetLeft(rect, sx0);
                Canvas.SetTop(rect, padT);
                OneDContourCanvas.Children.Add(rect);
            }
            else
            {
                double y0 = MapV(seg.V0);
                double y1 = MapV(seg.V1);
                var poly = new Polygon
                {
                    Fill = fill,
                    Stroke = Brushes.Black,
                    StrokeThickness = 0.35,
                    Points = new PointCollection
                    {
                        new Point(sx0, yBase),
                        new Point(sx1, yBase),
                        new Point(sx1, y1),
                        new Point(sx0, y0)
                    }
                };
                OneDContourCanvas.Children.Add(poly);
            }
        }

        // Profile polyline (top of scalar field)
        var linePts = new PointCollection();
        foreach (var seg in segments)
        {
            double sx0 = MapS(seg.S0);
            double sx1 = MapS(seg.S1);
            double y0 = seg.IsStressElement ? padT + 0.5 * plotH : MapV(seg.V0);
            double y1 = seg.IsStressElement ? padT + 0.5 * plotH : MapV(seg.V1);
            if (linePts.Count == 0)
                linePts.Add(new Point(sx0, y0));
            linePts.Add(new Point(sx1, y1));
        }
        OneDContourCanvas.Children.Add(new Polyline
        {
            Points = linePts,
            Stroke = Brushes.Black,
            StrokeThickness = 1.5,
            Fill = Brushes.Transparent
        });

        FECanvasAxisOverlay.RedrawPlotFrame(
            OneDContourCanvas,
            padL,
            padT,
            plotW,
            plotH,
            sMin,
            sMax,
            minV,
            maxV,
            xAxisTitle: "Distance along member");
    }

    private readonly struct Segment1D
    {
        public double S0 { get; init; }
        public double S1 { get; init; }
        public double V0 { get; init; }
        public double V1 { get; init; }
        public bool IsStressElement { get; init; }
        public double ElementValue { get; init; }
    }

    /// <summary>Min/max scalar for the color legend when <see cref="ResultsViewModel.VisualizationMode"/> is 1D contour.</summary>
    public static bool TryGetScalarRange(ResultsViewModel vm, out double minV, out double maxV)
    {
        minV = maxV = 0;
        var nodes = vm.FENodes;
        var elements = vm.FEElements;
        if (nodes == null || elements == null || nodes.Count == 0 || elements.Count == 0)
            return false;
        var order = GetOrderedNodeIndices(nodes, elements);
        if (order == null || order.Count < 2)
            return false;
        return TryBuildSegments(nodes, elements, order, vm.SelectedOutputType, vm, out _, out minV, out maxV);
    }

    private static bool TryBuildSegments(
        IReadOnlyList<Node> nodes,
        IReadOnlyList<ElementBase> elements,
        IReadOnlyList<int> order,
        string outputType,
        ResultsViewModel viewModel,
        out List<Segment1D> segments,
        out double minV,
        out double maxV)
    {
        segments = new List<Segment1D>();
        minV = 0;
        maxV = 0;
        bool stress = ResultsViewModel.IsElementScalarOutput(outputType);
        var sample = new List<double>();
        double s = 0;

        for (int k = 0; k < order.Count - 1; k++)
        {
            int i0 = order[k];
            int i1 = order[k + 1];
            var n0 = SafeNode(nodes, i0);
            var n1 = SafeNode(nodes, i1);
            if (n0 == null || n1 == null)
                continue;
            var rod = FindRod(elements, i0, i1);
            if (rod == null)
                continue;
            double ds = Hypot(n1.X - n0.X, n1.Y - n0.Y);
            double s0 = s;
            double s1 = s + ds;

            if (stress)
            {
                double ev = viewModel.GetElementScalarValue(rod, outputType, nodes);
                sample.Add(ev);
                segments.Add(new Segment1D
                {
                    S0 = s0,
                    S1 = s1,
                    V0 = ev,
                    V1 = ev,
                    IsStressElement = true,
                    ElementValue = ev
                });
            }
            else
            {
                double v0 = viewModel.GetNodeScalarValue(n0, outputType);
                double v1 = viewModel.GetNodeScalarValue(n1, outputType);
                sample.Add(v0);
                sample.Add(v1);
                segments.Add(new Segment1D
                {
                    S0 = s0,
                    S1 = s1,
                    V0 = v0,
                    V1 = v1,
                    IsStressElement = false,
                    ElementValue = 0
                });
            }

            s = s1;
        }

        if (segments.Count == 0 || sample.Count == 0)
            return false;

        minV = sample.Min();
        maxV = sample.Max();
        return true;
    }

    private static Node? SafeNode(IReadOnlyList<Node> nodes, int indexOrId)
    {
        if (indexOrId >= 0 && indexOrId < nodes.Count)
            return nodes[indexOrId];
        return nodes.FirstOrDefault(n => n.Id == indexOrId);
    }

    private static LinearRodElement? FindRod(IReadOnlyList<ElementBase> elements, int a, int b)
    {
        foreach (var e in elements)
        {
            if (e is not LinearRodElement r || r.NodeIds.Length != 2)
                continue;
            int u = r.NodeIds[0], v = r.NodeIds[1];
            if ((u == a && v == b) || (u == b && v == a))
                return r;
        }
        return null;
    }

    private static double Hypot(double dx, double dy) => Math.Sqrt(dx * dx + dy * dy);

    /// <summary>Ordered node indices along a chain of 2-node rod elements.</summary>
    public static List<int>? GetOrderedNodeIndices(IReadOnlyList<Node> nodes, IReadOnlyList<ElementBase> elements)
    {
        var rods = elements.OfType<LinearRodElement>().ToList();
        if (rods.Count == 0)
            return null;

        var adj = new Dictionary<int, List<int>>();
        void AddEdge(int a, int b)
        {
            if (!adj.ContainsKey(a)) adj[a] = new List<int>();
            if (!adj.ContainsKey(b)) adj[b] = new List<int>();
            adj[a].Add(b);
            adj[b].Add(a);
        }

        foreach (var r in rods)
        {
            if (r.NodeIds.Length != 2) continue;
            AddEdge(r.NodeIds[0], r.NodeIds[1]);
        }

        if (adj.Count == 0)
            return null;

        int start = -1;
        foreach (var k in adj.Keys)
        {
            if (adj[k].Count == 1)
            {
                start = k;
                break;
            }
        }
        if (start < 0)
            start = adj.Keys.Min();

        var order = new List<int> { start };
        int prev = -1;
        int cur = start;
        var guard = 0;
        while (guard++ < nodes.Count + 5)
        {
            if (!adj.TryGetValue(cur, out var neigh) || neigh.Count == 0)
                break;
            int next = -1;
            foreach (var x in neigh)
            {
                if (x != prev)
                {
                    next = x;
                    break;
                }
            }
            if (next < 0)
                break;
            order.Add(next);
            prev = cur;
            cur = next;
        }

        return order.Count >= 2 ? order : null;
    }
}
