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

public partial class FEVectorCanvasView : UserControl
{
    // Mouse-driven zoom/pan/zoom-box state. Mirrors FEContourCanvasView so the two
    // visualisation subtabs behave identically: wheel = zoom, middle-button drag =
    // pan, left-drag = zoom-to-box, left-double-click = reset view.
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

    public FEVectorCanvasView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && DataContext is ResultsViewModel vm)
                RenderVectors(vm);
        };
    }

    // =========================================================================
    //  Mouse handlers — wheel zoom, middle-button pan, left-drag zoom-box
    // =========================================================================

    private void VectorCanvas_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ZoomScaleTransform == null || ZoomTranslateTransform == null) return;
        var pos = e.GetPosition(ZoomPanBorder);
        double scale = e.Delta > 0 ? 1.15 : 1 / 1.15;
        double newScale = ZoomScaleTransform.ScaleX * scale;
        newScale = Math.Clamp(newScale, MinScale, MaxScale);
        double factor = newScale / ZoomScaleTransform.ScaleX;
        // Keep the point under the cursor anchored across zoom levels.
        ZoomTranslateTransform.X = pos.X - (pos.X - ZoomTranslateTransform.X) * factor;
        ZoomTranslateTransform.Y = pos.Y - (pos.Y - ZoomTranslateTransform.Y) * factor;
        ZoomScaleTransform.ScaleX = newScale;
        ZoomScaleTransform.ScaleY = newScale;
        RefreshAxisOverlay();
        e.Handled = true;
    }

    private void VectorCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Left double-click resets the view, matching the contour canvas's UX.
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

    private void VectorCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            _isPanning = true;
            _lastPanPoint = e.GetPosition(ZoomPanBorder);
            ZoomPanBorder.CaptureMouse();
        }
    }

    private void VectorCanvas_MouseMove(object sender, MouseEventArgs e)
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

    private void VectorCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
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

    private void VectorCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && _isPanning)
        {
            _isPanning = false;
            ZoomPanBorder.ReleaseMouseCapture();
            RefreshAxisOverlay();
        }
    }

    private void VectorCanvas_MouseLeave(object sender, MouseEventArgs e)
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

    /// <summary>
    /// Resets the canvas's zoom/pan transform to the identity, putting the rendered
    /// mesh back at its original "fit-to-canvas" position. Called on left double-click
    /// so the user can recover from an aggressive zoom without re-running the analysis.
    /// </summary>
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
        {
            RenderVectors(viewModel);
        }
    }
    
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ResultsViewModel viewModel)
        {
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
            RenderVectors(viewModel);
        }
    }
    
    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResultsViewModel.VectorOutputType) ||
            e.PropertyName == nameof(ResultsViewModel.VectorDisplacementComponent) ||
            e.PropertyName == nameof(ResultsViewModel.VectorScale) ||
            e.PropertyName == nameof(ResultsViewModel.ShowMesh) ||
            e.PropertyName == nameof(ResultsViewModel.DeformedMeshExaggeration) ||
            e.PropertyName == nameof(ResultsViewModel.ShowUndeformedOutline) ||
            e.PropertyName == nameof(ResultsViewModel.FeNodalDisplacements) ||
            e.PropertyName == nameof(ResultsViewModel.FeElementStresses) ||
            e.PropertyName == nameof(ResultsViewModel.FENodes) ||
            e.PropertyName == nameof(ResultsViewModel.FEElements) ||
            e.PropertyName == nameof(ResultsViewModel.SelectedStageResult) ||
            e.PropertyName == nameof(ResultsViewModel.StagedAnalysisResult) ||
            e.PropertyName == nameof(ResultsViewModel.VisualizationMode) ||
            e.PropertyName == nameof(ResultsViewModel.ShowPseudoStaticForceVectors) ||
            e.PropertyName == nameof(ResultsViewModel.HasPseudoStaticResults) ||
            e.PropertyName == nameof(ResultsViewModel.FePseudoStaticNodalForces) ||
            e.PropertyName == nameof(ResultsViewModel.ShowAxisOfSymmetry))
        {
            if (sender is ResultsViewModel viewModel)
                RenderVectors(viewModel);
        }
    }
    
    private void RenderVectors(ResultsViewModel viewModel)
    {
        VectorCanvas.Children.Clear();
        _axisTransformReady = false;
        FECanvasAxisOverlay.Clear(AxisOverlayCanvas);

        if (!viewModel.IsVectorMode)
            return;
        
        var nodes = viewModel.FENodes;
        var elements = viewModel.FEElements;
        var vectorType = viewModel.VectorOutputType;
        // Guard against stale 0 scale from older projects (arrows would be invisible).
        var scale = Math.Max(0.1, viewModel.VectorScale);
        var showMesh = viewModel.ShowMesh;
        
        if (nodes == null || elements == null || nodes.Count == 0 || elements.Count == 0)
        {
            return;
        }
        
        // Get set of removed (excavated) element IDs
        HashSet<int>? removedElementIds = null;
        if (viewModel.SelectedStageResult != null && viewModel.StagedAnalysisResult != null)
        {
            // For staged analysis, collect all removed elements from all stages up to and including the selected stage
            // This ensures we only show elements that are active in the selected stage
            removedElementIds = new HashSet<int>();
            int selectedStageNumber = viewModel.SelectedStageResult.StageNumber;
            foreach (var stageResult in viewModel.StagedAnalysisResult.StageResults)
            {
                // Include removed elements from this stage and all previous stages
                if (stageResult.StageNumber <= selectedStageNumber)
                {
                    foreach (var elementId in stageResult.RemovedElementIds)
                    {
                        removedElementIds.Add(elementId);
                    }
                }
            }
        }
        else if (viewModel.StagedAnalysisResult != null && viewModel.StagedAnalysisResult.StageResults.Count > 0)
        {
            // If no stage is selected but staged results exist, collect all removed elements from all stages
            removedElementIds = new HashSet<int>();
            foreach (var stageResult in viewModel.StagedAnalysisResult.StageResults)
            {
                foreach (var elementId in stageResult.RemovedElementIds)
                {
                    removedElementIds.Add(elementId);
                }
            }
        }
        
        // Per-stage path (preferred): `removedElementIds` is the cumulative excavation
        // up to and including the selected stage, built from the .dat Layers block in
        // ResultsViewModel.LoadP610T3Result. When available it is the only correct signal
        // — the JSON's `active` flag reflects the cumulative final state across every
        // stage and would otherwise hide elements that are still solid in earlier stages.
        //
        // Fallback (single-stage / no .dat): use `IsActive` so existing wizard-driven
        // projects keep their rendering.
        var activeElements = elements
            .Where(e => removedElementIds != null
                ? !removedElementIds.Contains(e.Id)
                : e.IsActive)
            .ToList();

        // Excavated outline overlay — the elements that are void in the currently viewed
        // stage get a dashed grey outline so the user still sees the excavation boundary
        // without those elements contributing to stress-vector arrows.
        var inactiveElements = removedElementIds != null
            ? elements.Where(e => removedElementIds.Contains(e.Id)).ToList()
            : elements.Where(e => !e.IsActive).ToList();

        if (activeElements.Count == 0)
            return;

        double meshExag = Math.Max(1.0, viewModel.DeformedMeshExaggeration);
        var activeNodeIndices = new HashSet<int>();
        foreach (var element in activeElements)
        {
            foreach (var nodeId in element.NodeIds)
                activeNodeIndices.Add(nodeId);
        }

        var bounds = CalculateBounds(nodes, activeNodeIndices, meshExag);
        var coordScale = CalculateScale(bounds);
        var offset = CalculateOffset(bounds, coordScale);
        _renderScale = coordScale;
        _renderOffsetX = offset.X;
        _renderOffsetY = offset.Y;
        _axisTransformReady = true;

        if (showMesh)
        {
            if (viewModel.ShowUndeformedOutline)
            {
                FEMeshWireframeRenderer.AddWireframe(
                    VectorCanvas, activeElements, nodes, coordScale, offset,
                    exaggeration: 0, Brushes.LightGray, 0.5, dashed: true,
                    forceFullQuadraticPerimeter: false, viewModel.FeMeshSmithEmb2dQ8LocalOrder, ModelYToCanvas);
            }
            FEMeshWireframeRenderer.AddWireframe(
                VectorCanvas, activeElements, nodes, coordScale, offset,
                meshExag, Brushes.DimGray, 0.6, dashed: false,
                forceFullQuadraticPerimeter: true, viewModel.FeMeshSmithEmb2dQ8LocalOrder, ModelYToCanvas);
            if (inactiveElements.Count > 0)
            {
                FEMeshWireframeRenderer.AddWireframe(
                    VectorCanvas, inactiveElements, nodes, coordScale, offset,
                    exaggeration: 0, Brushes.DimGray, 0.7, dashed: true,
                    forceFullQuadraticPerimeter: false, viewModel.FeMeshSmithEmb2dQ8LocalOrder, ModelYToCanvas);
            }
        }
        
        // Build the set of node ids that are still part of at least one active element.
        // Without this filter, RenderDisplacementVectors would draw an arrow at every
        // node in the mesh — including nodes that sit on the boundary of (or inside)
        // the excavated cavity, even after every element they belonged to was removed.
        // Visually those orphaned nodes show up as arrows inside the void, which is
        // the bug the user spotted on the staged-tunnel run.
        var activeNodeIds = new HashSet<int>();
        foreach (var element in activeElements)
            foreach (var nodeId in element.NodeIds)
                activeNodeIds.Add(nodeId);

        // Pseudo-static may linger from a prior seismic run; fall back to displacements.
        if (vectorType == "Pseudo-static Force Vectors" && !viewModel.HasPseudoStaticResults)
            vectorType = "Displacement Vectors";

        if (vectorType == "Displacement Vectors")
        {
            RenderDisplacementVectors(nodes, activeNodeIds, coordScale, offset, scale,
                viewModel.VectorDisplacementComponent, meshExag);
            if (viewModel.ShowPseudoStaticForceVectors && viewModel.HasPseudoStaticResults)
                RenderPseudoStaticForceVectors(viewModel, nodes, activeNodeIds, coordScale, offset, scale, meshExag);
        }
        else if (vectorType == "Pseudo-static Force Vectors")
        {
            RenderPseudoStaticForceVectors(viewModel, nodes, activeNodeIds, coordScale, offset, scale, meshExag);
        }
        else if (vectorType == "Principal Stress Directions")
        {
            if (!RenderPrincipalStressVectors(nodes, activeElements, viewModel, coordScale, offset, scale))
                DrawStressDataUnavailableHint();
        }
        else if (vectorType == "Stress Vectors")
        {
            if (!RenderStressVectors(nodes, activeElements, viewModel, coordScale, offset, scale))
                DrawStressDataUnavailableHint();
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

        var vw = ZoomPanBorder?.ActualWidth > 0 ? ZoomPanBorder.ActualWidth : VectorCanvas.ActualWidth;
        var vh = ZoomPanBorder?.ActualHeight > 0 ? ZoomPanBorder.ActualHeight : VectorCanvas.ActualHeight;
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

    private void DrawStressDataUnavailableHint(string? message = null)
    {
        double w = VectorCanvas.ActualWidth > 0 ? VectorCanvas.ActualWidth : 800;
        double h = VectorCanvas.ActualHeight > 0 ? VectorCanvas.ActualHeight : 600;
        var tb = new TextBlock
        {
            Text = message ?? "No element stresses in this run.\nRebuild fem_cpp and re-run the p64 analysis.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DarkOrange,
            FontSize = 13,
            TextAlignment = TextAlignment.Center,
            MaxWidth = Math.Min(420, w * 0.85)
        };
        tb.Measure(new Size(tb.MaxWidth, double.PositiveInfinity));
        Canvas.SetLeft(tb, Math.Max(8, (w - tb.DesiredSize.Width) * 0.5));
        Canvas.SetTop(tb, Math.Max(8, (h - tb.DesiredSize.Height) * 0.5));
        VectorCanvas.Children.Add(tb);
    }
    
    private void RenderDisplacementVectors(
        IReadOnlyList<Node> nodes,
        HashSet<int> activeNodeIds,
        double coordScale,
        Point offset,
        double vectorScale,
        string displacementComponent,
        double meshExaggeration)
    {
        bool uxOnly = displacementComponent == "Horizontal (Ux only)";
        bool uyOnly = displacementComponent == "Vertical (Uy only)";

        // Collect the scalar magnitude for each active node under the selected component.
        // Using the 95th-percentile value as the reference rather than the global max
        // prevents boundary-constraint nodes (which carry large gravity-settlement Uy but
        // no meaningful failure-mechanism displacement) from compressing the arrow scale.
        var mags = new List<double>(nodes.Count);
        foreach (var node in nodes)
        {
            if (!activeNodeIds.Contains(node.Id)) continue;
            var d = node.Displacements;
            if (d == null || d.Length < 2) continue;
            double u = uxOnly ? d[0] : (uyOnly ? 0 : d[0]);
            double v = uyOnly ? d[1] : (uxOnly ? 0 : d[1]);
            mags.Add(Math.Sqrt(u * u + v * v));
        }

        if (mags.Count == 0) return;
        mags.Sort();

        // 95th-percentile cap: at most 5% of nodes draw full-length arrows.
        // This keeps boundary-gravity-settlement outliers from crushing the scale.
        int p95idx = Math.Max(0, (int)(mags.Count * 0.95) - 1);
        double refMag = mags[p95idx];
        if (refMag < 1e-10)
            refMag = mags[^1]; // fall back to max if 95th pct is essentially zero

        if (refMag < 1e-10)
            return;

        // Reference length = 8% of canvas width per unit of reference magnitude.
        double canvasWidth = VectorCanvas.ActualWidth > 0 ? VectorCanvas.ActualWidth : 800;
        double referenceLength = canvasWidth * 0.08;
        double arrowScale = referenceLength / refMag * vectorScale;

        foreach (var node in nodes)
        {
            if (!activeNodeIds.Contains(node.Id))
                continue;

            var d = node.Displacements;
            if (d == null || d.Length < 2) continue;
            double u = uxOnly ? d[0] : (uyOnly ? 0 : d[0]);
            double v = uyOnly ? d[1] : (uxOnly ? 0 : d[1]);
            double mag = Math.Sqrt(u * u + v * v);

            if (mag < 1e-10)
                continue;

            var basePos = FEMeshWireframeRenderer.NodePosition(node, meshExaggeration);
            double x = basePos.x * coordScale + offset.X;
            double y = ModelYToCanvas(basePos.y, coordScale, offset.Y);

            double dx = u * arrowScale;
            double dy = -v * arrowScale;

            DrawArrow(x, y, x + dx, y + dy, Brushes.Blue, 1.5);
        }
    }

    private void RenderPseudoStaticForceVectors(
        ResultsViewModel viewModel,
        IReadOnlyList<Node> nodes,
        HashSet<int> activeNodeIds,
        double coordScale,
        Point offset,
        double vectorScale,
        double meshExaggeration)
    {
        var forces = viewModel.FePseudoStaticNodalForces;
        if (forces == null || forces.Count == 0) return;

        var mags = new List<double>();
        foreach (var row in forces)
        {
            int nodeIdx = row.NodeId - 1;
            if (nodeIdx < 0 || nodeIdx >= nodes.Count || !activeNodeIds.Contains(nodes[nodeIdx].Id))
                continue;
            mags.Add(row.Magnitude);
        }
        if (mags.Count == 0) return;
        mags.Sort();
        int p95idx = Math.Max(0, (int)(mags.Count * 0.95) - 1);
        double refMag = mags[p95idx];
        if (refMag < 1e-10) refMag = mags[^1];
        if (refMag < 1e-10) return;

        double canvasWidth = VectorCanvas.ActualWidth > 0 ? VectorCanvas.ActualWidth : 800;
        double arrowScale = canvasWidth * 0.08 / refMag * vectorScale;
        var orange = new SolidColorBrush(Color.FromRgb(234, 88, 12));

        foreach (var row in forces)
        {
            int nodeIdx = row.NodeId - 1;
            if (nodeIdx < 0 || nodeIdx >= nodes.Count || !activeNodeIds.Contains(nodes[nodeIdx].Id))
                continue;
            if (row.Magnitude < 1e-10) continue;

            var node = nodes[nodeIdx];
            var basePos = FEMeshWireframeRenderer.NodePosition(node, meshExaggeration);
            double x = basePos.x * coordScale + offset.X;
            double y = ModelYToCanvas(basePos.y, coordScale, offset.Y);
            double dx = row.Fx * arrowScale;
            double dy = -row.Fy * arrowScale;
            DrawArrow(x, y, x + dx, y + dy, orange, 2.0);
        }
    }
    
    private bool RenderPrincipalStressVectors(
        IReadOnlyList<Node> nodes,
        IReadOnlyList<ElementBase> elements,
        ResultsViewModel viewModel,
        double coordScale,
        Point offset,
        double vectorScale)
    {
        // For each element, compute principal stress directions at centroid
        // and render at element centroid
        
        double maxStress = 0;
        var elementStresses = new Dictionary<int, ElementStress>();
        
        foreach (var stress in viewModel.FeElementStresses)
        {
            elementStresses[stress.ElementId] = stress;
            double max = Math.Max(Math.Abs(stress.PrincipalStressMax), Math.Abs(stress.PrincipalStressMin));
            if (max > maxStress)
                maxStress = max;
        }
        
        if (maxStress < 1e-10)
            return false;

        double canvasWidth = VectorCanvas.ActualWidth > 0 ? VectorCanvas.ActualWidth : 800;
        double referenceLength = canvasWidth * 0.05;
        double arrowScale = referenceLength / maxStress * vectorScale;
        int drawn = 0;

        foreach (var element in elements)
        {
            if (!elementStresses.ContainsKey(element.Id))
                continue;

            var stress = elementStresses[element.Id];
            
            // Compute element centroid
            double cx = 0, cy = 0;
            foreach (var nodeId in element.NodeIds)
            {
                if (nodeId >= 0 && nodeId < nodes.Count)
                {
                    cx += nodes[nodeId].X;
                    cy += nodes[nodeId].Y;
                }
            }
            cx /= element.NodeIds.Length;
            cy /= element.NodeIds.Length;
            
            double x = cx * coordScale + offset.X;
            double y = ModelYToCanvas(cy, coordScale, offset.Y);
            
            // Principal stress directions
            // For 2D: principal directions are at angle θ = 0.5 * atan2(2*τxy, σxx - σyy)
            double sigmaXX = stress.StressXX;
            double sigmaYY = stress.StressYY;
            double tauXY = stress.StressXY;
            
            double angle = 0.5 * Math.Atan2(2 * tauXY, sigmaXX - sigmaYY);
            
            // Draw max principal stress (red)
            double maxMag = Math.Abs(stress.PrincipalStressMax);
            double maxDx = Math.Cos(angle) * maxMag * arrowScale;
            // angle is the model-frame orientation; flip Y for the screen (mesh Y up).
            double maxDy = -Math.Sin(angle) * maxMag * arrowScale;
            if (DrawArrow(x, y, x + maxDx, y + maxDy, Brushes.Red, 2.0))
                drawn++;
            
            // Draw min principal stress (blue) - perpendicular
            double minMag = Math.Abs(stress.PrincipalStressMin);
            double minDx = Math.Cos(angle + Math.PI / 2) * minMag * arrowScale;
            double minDy = -Math.Sin(angle + Math.PI / 2) * minMag * arrowScale;
            if (DrawArrow(x, y, x + minDx, y + minDy, Brushes.Blue, 1.5))
                drawn++;
        }

        return drawn > 0;
    }

    private bool RenderStressVectors(
        IReadOnlyList<Node> nodes,
        IReadOnlyList<ElementBase> elements,
        ResultsViewModel viewModel,
        double coordScale,
        Point offset,
        double vectorScale)
    {
        // Render stress vectors at nodes (averaged from connected elements)
        var nodeStresses = new Dictionary<int, (double sigmaXX, double sigmaYY, double tauXY, int count)>();
        
        foreach (var element in elements)
        {
            var stress = viewModel.FeElementStresses.FirstOrDefault(s => s.ElementId == element.Id);
            if (stress == null)
                continue;
            
            foreach (var nodeId in element.NodeIds)
            {
                if (nodeId >= 0 && nodeId < nodes.Count)
                {
                    if (!nodeStresses.ContainsKey(nodeId))
                    {
                        nodeStresses[nodeId] = (0, 0, 0, 0);
                    }
                    
                    var current = nodeStresses[nodeId];
                    nodeStresses[nodeId] = (
                        current.sigmaXX + stress.StressXX,
                        current.sigmaYY + stress.StressYY,
                        current.tauXY + stress.StressXY,
                        current.count + 1
                    );
                }
            }
        }
        
        // Average and render
        double maxStress = 0;
        foreach (var kvp in nodeStresses)
        {
            var (sigmaXX, sigmaYY, tauXY, count) = kvp.Value;
            double avgXX = sigmaXX / count;
            double avgYY = sigmaYY / count;
            double avgXY = tauXY / count;
            double mag = Math.Sqrt(avgXX * avgXX + avgYY * avgYY + avgXY * avgXY);
            if (mag > maxStress)
                maxStress = mag;
        }
        
        if (maxStress < 1e-10)
            return false;

        double canvasWidth = VectorCanvas.ActualWidth > 0 ? VectorCanvas.ActualWidth : 800;
        double referenceLength = canvasWidth * 0.05;
        double arrowScale = referenceLength / maxStress * vectorScale;
        int drawn = 0;

        foreach (var kvp in nodeStresses)
        {
            int nodeId = kvp.Key;
            var (sigmaXX, sigmaYY, tauXY, count) = kvp.Value;
            
            double avgXX = sigmaXX / count;
            double avgYY = sigmaYY / count;
            
            var node = nodes[nodeId];
            double x = node.X * coordScale + offset.X;
            double y = ModelYToCanvas(node.Y, coordScale, offset.Y);
            
            // Render stress vector (using normal stress components). avgYY is the
            // model-frame normal stress; flip its sign for the canvas (mesh Y up).
            double dx = avgXX * arrowScale;
            double dy = -avgYY * arrowScale;
            
            if (DrawArrow(x, y, x + dx, y + dy, Brushes.Green, 1.5))
                drawn++;
        }

        return drawn > 0;
    }

    private bool DrawArrow(double x1, double y1, double x2, double y2, Brush color, double thickness)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-10)
            return false;

        var line = new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = color,
            StrokeThickness = thickness
        };
        VectorCanvas.Children.Add(line);
        
        // Arrowhead size
        double arrowLength = Math.Min(length * 0.2, 10);
        double arrowAngle = Math.Atan2(dy, dx);
        
        // Arrowhead points
        double arrowX1 = x2 - arrowLength * Math.Cos(arrowAngle - Math.PI / 6);
        double arrowY1 = y2 - arrowLength * Math.Sin(arrowAngle - Math.PI / 6);
        double arrowX2 = x2 - arrowLength * Math.Cos(arrowAngle + Math.PI / 6);
        double arrowY2 = y2 - arrowLength * Math.Sin(arrowAngle + Math.PI / 6);
        
        var arrowhead = new Polygon
        {
            Points = new PointCollection
            {
                new Point(x2, y2),
                new Point(arrowX1, arrowY1),
                new Point(arrowX2, arrowY2)
            },
            Fill = color,
            Stroke = color,
            StrokeThickness = thickness
        };
        VectorCanvas.Children.Add(arrowhead);
        return true;
    }
    
    private static Rect CalculateBounds(
        IReadOnlyList<Node> nodes,
        HashSet<int> activeNodeIndices,
        double meshExaggeration)
    {
        if (activeNodeIndices.Count == 0)
            return new Rect(0, 0, 100, 100);

        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;

        foreach (int idx in activeNodeIndices)
        {
            if (idx < 0 || idx >= nodes.Count) continue;
            var node = nodes[idx];
            var p0 = FEMeshWireframeRenderer.NodePosition(node, 0);
            var p1 = FEMeshWireframeRenderer.NodePosition(node, meshExaggeration);
            minX = Math.Min(minX, Math.Min(p0.x, p1.x));
            maxX = Math.Max(maxX, Math.Max(p0.x, p1.x));
            minY = Math.Min(minY, Math.Min(p0.y, p1.y));
            maxY = Math.Max(maxY, Math.Max(p0.y, p1.y));
        }

        if (double.IsPositiveInfinity(minX))
            return new Rect(0, 0, 100, 100);

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }
    
    private double CalculateScale(Rect bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return 1.0;
        
        double canvasWidth = VectorCanvas.ActualWidth > 0 ? VectorCanvas.ActualWidth : 800;
        double canvasHeight = VectorCanvas.ActualHeight > 0 ? VectorCanvas.ActualHeight : 600;
        
        double scaleX = (canvasWidth - 40) / bounds.Width;
        double scaleY = (canvasHeight - 40) / bounds.Height;
        
        return Math.Min(scaleX, scaleY);
    }
    
    private Point CalculateOffset(Rect bounds, double scale)
    {
        double canvasWidth = VectorCanvas.ActualWidth > 0 ? VectorCanvas.ActualWidth : 800;
        double canvasHeight = VectorCanvas.ActualHeight > 0 ? VectorCanvas.ActualHeight : 600;
        
        double scaledWidth = bounds.Width * scale;
        double scaledHeight = bounds.Height * scale;
        
        double offsetX = (canvasWidth - scaledWidth) / 2 - bounds.X * scale;
        // Y-flip: canvas_y = -model_y * scale + offsetY (see ModelYToCanvas). Surface
        // (largest model Y) lands at the top of the canvas, base at the bottom — the
        // standard orientation used by every FE post-processor.
        double offsetY = (canvasHeight + scaledHeight) / 2 + bounds.Y * scale;
        
        return new Point(offsetX, offsetY);
    }

    /// <summary>
    /// Forward model-Y &rarr; canvas-Y mapping for the vector view. WPF Canvas has
    /// (0,0) in the top-left with Y growing downward, but the FE mesh uses the
    /// standard "Y up" convention. Flipping the sign here keeps the surface visually
    /// at the top of the canvas and the fixed base at the bottom.
    /// </summary>
    private static double ModelYToCanvas(double modelY, double scale, double offsetY)
        => -modelY * scale + offsetY;
    
}
