using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Views.Controls;

public partial class SlopeCanvasView : UserControl
{
    private bool _isDraggingCircleCenter;
    private bool _isPanning;
    private Vector _circleDragOffset;
    private Point _lastPanPoint;
    private Matrix _worldMatrix = Matrix.Identity;
    private double _zoomFactor = 1.0;
    private bool _isZoomBoxSelecting;
    private Point _zoomStart;
    private Rect _zoomRect;
    private Point? _lastCanvasPointer;
    private bool _isApplyingSharedTransform;
    private bool _isDrawingTunnelCircle;
    private bool _isDrawingGeometryCircle;
    private bool _isDraggingTunnelCircle;
    private int _draggedTunnelCircleIndex = -1;
    private Vector _tunnelCircleDragOffset;
    private bool _isBulkPointDeleteSelecting;
    private Point _bulkPointDeleteStart;
    private Rect _bulkPointDeleteRect;
    private bool _isBcImportSelecting;
    private Point _bcImportStart;
    private Rect _bcImportRect;
    private bool _isTransientNodeSelecting;
    private Point _transientNodeSelectStart;
    private Rect _transientNodeSelectRect;

    private static readonly HashSet<SlopeCanvasView> Instances = new();
    private static Matrix _sharedWorldMatrix = Matrix.Identity;
    private static double _sharedZoomFactor = 1.0;
    public static event Action<Matrix, double>? SharedTransformChanged;

    private const double MinScale = 0.05;
    private const double MaxScale = 20.0;
    private const double MinZoomBoxSize = 10.0;
    private const double MinBulkPointDeleteBoxSize = 8.0;
    private const double BulkPointDeleteClickTolerance = 12.0;
    private const double MinBcImportBoxSize = 8.0;
    private const double BcImportClickTolerance = 12.0;
    private const double TransientNodeSelectClickTolerance = 12.0;

    public SlopeCanvasView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Instances.Add(this);
    }

    public static readonly DependencyProperty IsGeometryEditableProperty =
        DependencyProperty.Register(
            nameof(IsGeometryEditable),
            typeof(bool),
            typeof(SlopeCanvasView),
            new PropertyMetadata(false));

    public static readonly DependencyProperty SlipSurfaceContextProperty =
        DependencyProperty.Register(
            nameof(SlipSurfaceContext),
            typeof(SlipSurfaceViewModel),
            typeof(SlopeCanvasView),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ZoomPanModeEnabledProperty =
        DependencyProperty.Register(
            nameof(ZoomPanModeEnabled),
            typeof(bool),
            typeof(SlopeCanvasView),
            new PropertyMetadata(true));

    public static readonly DependencyProperty ZoomAreaModeEnabledProperty =
        DependencyProperty.Register(
            nameof(ZoomAreaModeEnabled),
            typeof(bool),
            typeof(SlopeCanvasView),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsElementExcavationModeActiveProperty =
        DependencyProperty.Register(
            nameof(IsElementExcavationModeActive),
            typeof(bool),
            typeof(SlopeCanvasView),
            new PropertyMetadata(false));

    public static readonly DependencyProperty DoubleClickFitsGeometryProperty =
        DependencyProperty.Register(
            nameof(DoubleClickFitsGeometry),
            typeof(bool),
            typeof(SlopeCanvasView),
            new PropertyMetadata(false));

    public bool IsGeometryEditable
    {
        get => (bool)GetValue(IsGeometryEditableProperty);
        set => SetValue(IsGeometryEditableProperty, value);
    }

    public SlipSurfaceViewModel? SlipSurfaceContext
    {
        get => (SlipSurfaceViewModel?)GetValue(SlipSurfaceContextProperty);
        set => SetValue(SlipSurfaceContextProperty, value);
    }

    public bool ZoomPanModeEnabled
    {
        get => (bool)GetValue(ZoomPanModeEnabledProperty);
        set => SetValue(ZoomPanModeEnabledProperty, value);
    }

    /// <summary>When true, left-drag always starts a zoom-to-rectangle selection (toolbar Zoom to Area mode).</summary>
    public bool ZoomAreaModeEnabled
    {
        get => (bool)GetValue(ZoomAreaModeEnabledProperty);
        set => SetValue(ZoomAreaModeEnabledProperty, value);
    }

    /// <summary>
    /// When true, the canvas shows excavation-click cues (crosshair cursor + badge).
    /// Wired by Mesh tab to MeshTabCanvasContext.IsExcavateElementsMode.
    /// </summary>
    public bool IsElementExcavationModeActive
    {
        get => (bool)GetValue(IsElementExcavationModeActiveProperty);
        set => SetValue(IsElementExcavationModeActiveProperty, value);
    }

    /// <summary>
    /// When true, double-click refits axis bounds to geometry extents before resetting the view
    /// (Groundwater tab). Other canvases keep plain <see cref="ResetView"/> double-click.
    /// </summary>
    public bool DoubleClickFitsGeometry
    {
        get => (bool)GetValue(DoubleClickFitsGeometryProperty);
        set => SetValue(DoubleClickFitsGeometryProperty, value);
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // Re-add to Instances so PushSharedTransform can reach this canvas again
        // (OnUnloaded removes it; without this it stays absent after first tab switch).
        Instances.Add(this);

        // Defer calibration until after WPF's layout pass. Loaded fires before
        // Measure/Arrange complete, so ActualHeight is 0 and PinAxesToBorder /
        // ClampContentIntoView return early, leaving WorldTransform with a stale
        // _sharedWorldMatrix value that can push geometry off-screen.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            _isApplyingSharedTransform = true;
            _worldMatrix = _sharedWorldMatrix;
            _zoomFactor = _sharedZoomFactor;
            ApplyTransform();
            NotifyZoomUpdated();
            _isApplyingSharedTransform = false;
        });
    }

    private void GeometryCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isApplyingSharedTransform) return;
        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0) return;
        NotifyZoomUpdated();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        Instances.Remove(this);
    }

    private void ApplyTransform()
    {
        if (WorldTransform is not null)
        {
            WorldTransform.Matrix = _worldMatrix;
        }
    }

    private Point ScreenToWorld(Point screenPoint)
    {
        var inv = _worldMatrix;
        if (!inv.HasInverse)
        {
            return screenPoint;
        }

        inv.Invert();
        return inv.Transform(screenPoint);
    }

    private static GeometryDefinitionViewModel? GetGeometryViewModel(object? dataContext) =>
        dataContext as GeometryDefinitionViewModel ?? (dataContext as MeshTabCanvasWrapper)?.Geometry;

    private void BoxMethodCircleCenter_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (GetGeometryViewModel(DataContext) is not GeometryDefinitionViewModel viewModel)
        {
            return;
        }

        var center = viewModel.BoxMethodCircleCenter;
        var radius = viewModel.BoxMethodCircleRadius;

        if (double.IsNaN(center.X) ||
            double.IsNaN(center.Y) ||
            double.IsNaN(radius) ||
            radius <= 0)
        {
            return;
        }

        var position = ScreenToWorld(e.GetPosition(GeometryCanvas));
        _isDraggingCircleCenter = true;
        _circleDragOffset = center - position;
        GeometryCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void TunnelCircleCenter_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (GetGeometryViewModel(DataContext) is not GeometryDefinitionViewModel viewModel)
        {
            return;
        }

        if (!viewModel.IsTunnelCirclesEditMode)
        {
            return;
        }

        if (sender is not FrameworkElement element || element.DataContext is not Models.TunnelExcavationCircleRow circleRow)
        {
            return;
        }

        // Find the index of this circle
        var index = viewModel.TunnelExcavationCircleRows.IndexOf(circleRow);
        if (index < 0 || index >= viewModel.TunnelExcavationCircles.Count)
        {
            return;
        }

        var circle = viewModel.TunnelExcavationCircles[index];
        if (circle == null)
        {
            return;
        }

        var position = ScreenToWorld(e.GetPosition(GeometryCanvas));
        _isDraggingTunnelCircle = true;
        _draggedTunnelCircleIndex = index;
        _tunnelCircleDragOffset = circle.Center - position;
        GeometryCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void GeometryCanvas_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (GetGeometryViewModel(DataContext) is GeometryDefinitionViewModel viewModel && sender is IInputElement inputElement)
        {
            var positionCanvas = e.GetPosition(inputElement);
            _lastCanvasPointer = positionCanvas;
            var position = ScreenToWorld(positionCanvas);
            viewModel.UpdateGeometryCanvasCoordinates(position);

            if (viewModel.IsMeasureMode)
            {
                if (viewModel.HasMeasurementStart)
                    viewModel.FinishMeasurement(position);
                else
                    viewModel.StartMeasurement(position);
                e.Handled = true;
                return;
            }

            if (viewModel.IsDrawingGeologicalFeature)
            {
                viewModel.HandleGeologicalFeatureCanvasClick(position);
                e.Handled = true;
                return;
            }

            // Groundwater: double-click fits full geometry before placement/selection handlers run.
            if (DoubleClickFitsGeometry && e.ClickCount == 2)
            {
                viewModel.FitAxisBoundsToVertexExtents();
                ResetView();
                e.Handled = true;
                return;
            }

            // Mesh popup "Excavate Elements": click an active mesh element to excavate it.
            if (DataContext is MeshTabCanvasWrapper meshWrapper &&
                meshWrapper.IsExcavateElementsMode &&
                meshWrapper.TryExcavateElementAtPoint(position))
            {
                e.Handled = true;
                return;
            }

            // BC Import Mode Vertices/Nodes: drag-select boundary vertices or exterior mesh nodes.
            if (DataContext is MeshTabCanvasWrapper bcImportWrapper &&
                bcImportWrapper.IsBcManualImportMode &&
                bcImportWrapper.BoundaryConditions != null)
            {
                BeginBcImportSelection(bcImportWrapper.BoundaryConditions, position);
                e.Handled = true;
                return;
            }

            // Transient consolidation optional node selection: drag-box or click mesh nodes.
            if (viewModel.IsTransientNodeSelectionModeActive)
            {
                BeginTransientNodeSelection(viewModel, position);
                e.Handled = true;
                return;
            }

            if (IsGeometryEditable && viewModel.IsBulkPointDeleteMode)
            {
                BeginBulkPointDeleteSelection(viewModel, position);
                e.Handled = true;
                return;
            }

            // Don't start drawing if we're dragging a tunnel circle
            if (_isDraggingTunnelCircle)
            {
                e.Handled = true;
                return;
            }

            // Check if we're in draw-line mode (snap click to nearest vertex)
            if (viewModel.IsDrawLineMode)
            {
                viewModel.HandleDrawLineModeCanvasClick(position);
                e.Handled = true;
                return;
            }

            if (viewModel.IsGroundwaterPolylineDrawActive)
            {
                viewModel.HandleGroundwaterPolylineCanvasClick(position);
                e.Handled = true;
                return;
            }

            if (viewModel.TryHandleWaterHeadCanvasClick(position))
            {
                e.Handled = true;
                return;
            }

            if (viewModel.TryHandleTransientFixedPressureCanvasClick(position))
            {
                e.Handled = true;
                return;
            }

            if (viewModel.IsNonHorizontalLayerDrawMode)
            {
                viewModel.HandleNonHorizontalLayerCanvasClick(position);
                e.Handled = true;
                return;
            }

            // Check if we're in draw-arc mode (3 clicks: start → through → end)
            if (viewModel.IsDrawArcMode)
            {
                viewModel.HandleDrawArcModeCanvasClick(position);
                e.Handled = true;
                return;
            }

            // Draw circle for geometry line segments: center click, drag radius, release
            if (viewModel.IsDrawCircleMode)
            {
                if (!viewModel.HasGeometryCircleCenter)
                {
                    viewModel.StartGeometryCircleCenter(position);
                    _isDrawingGeometryCircle = true;
                    GeometryCanvas.CaptureMouse();
                    e.Handled = true;
                    return;
                }

                e.Handled = true;
                return;
            }

            // Check if we're in excavation mode
            if (viewModel.IsExcavationMode)
            {
                viewModel.ExcavateAtPoint(position);
                e.Handled = true;
                return;
            }

            // Check if we're in material-assign (paint-bucket) mode
            if (viewModel.IsMaterialAssignMode)
            {
                viewModel.AssignMaterialAtPoint(position);
                e.Handled = true;
                return;
            }

            // Check if we're in tunnel circle drawing mode
            if (viewModel.IsTunnelCircleDrawingMode)
            {
                // Always ensure clean state when in drawing mode
                _isZoomBoxSelecting = false;
                _isDraggingTunnelCircle = false;
                GeometryCanvas.ReleaseMouseCapture();
                UpdateZoomRectVisual(Rect.Empty);
                
                // If center is not set, start a new circle
                if (!viewModel.TunnelCirclePreviewCenter.HasValue)
                {
                    // Ensure clean state for new circle - reset all flags
                    _isDrawingTunnelCircle = false;
                    // First click: set center
                    viewModel.StartTunnelCircleDrawing(position);
                    _isDrawingTunnelCircle = true;
                    GeometryCanvas.CaptureMouse();
                    e.Handled = true;
                    return;
                }
                // If center is already set, we're continuing to draw the current circle
                // The mouse move handler will update the preview
                e.Handled = true;
                return;
            }

            // Don't add geometry vertex if we're in tunnel circle drawing mode or actively drawing
            if (viewModel.IsTunnelCircleDrawingMode || _isDrawingTunnelCircle || viewModel.IsDrawCircleMode || _isDrawingGeometryCircle)
            {
                e.Handled = true;
                return;
            }

            // Don't add geometry vertex if edit mode or new point mode is not enabled (unless defining surface polygon by click)
            if (!viewModel.IsGeometryVerticesEditMode && !viewModel.IsNewPointMode && !viewModel.IsSurfacePolygonPickMode)
            {
                // Make sure we don't start zoom box selection if we're not in edit mode
                // but also not handling the event, so return without handling
                return;
            }

            if (!IsGeometryEditable && !viewModel.IsSurfacePolygonPickMode)
            {
                return;
            }

            // Prevent zoom box selection from interfering
            _isZoomBoxSelecting = false;
            GeometryCanvas.ReleaseMouseCapture();
            UpdateZoomRectVisual(Rect.Empty);

            if (viewModel.IsSurfacePolygonPickMode)
            {
                viewModel.HandleSurfaceLinePickClick(position);
                e.Handled = true;
                return;
            }

            viewModel.AddGeometryVertex(position);
            e.Handled = true;
        }
    }

    private void BeginBulkPointDeleteSelection(GeometryDefinitionViewModel viewModel, Point canvasPoint)
    {
        _isZoomBoxSelecting = false;
        UpdateZoomRectVisual(Rect.Empty);

        _isBulkPointDeleteSelecting = true;
        _bulkPointDeleteStart = canvasPoint;
        _bulkPointDeleteRect = new Rect(_bulkPointDeleteStart, _bulkPointDeleteStart);
        viewModel.UpdateBulkPointDeleteSelection(_bulkPointDeleteRect);
        GeometryCanvas.CaptureMouse();
    }

    private void BeginBcImportSelection(BoundaryConditionsViewModel bcVm, Point canvasPoint)
    {
        _isZoomBoxSelecting = false;
        UpdateZoomRectVisual(Rect.Empty);

        _isBcImportSelecting = true;
        _bcImportStart = canvasPoint;
        _bcImportRect = new Rect(_bcImportStart, _bcImportStart);
        bcVm.UpdateBcImportSelection(_bcImportRect);
        GeometryCanvas.CaptureMouse();
    }


    private void BeginTransientNodeSelection(GeometryDefinitionViewModel viewModel, Point canvasPoint)
    {
        _isZoomBoxSelecting = false;
        UpdateZoomRectVisual(Rect.Empty);

        _isTransientNodeSelecting = true;
        _transientNodeSelectStart = canvasPoint;
        _transientNodeSelectRect = new Rect(_transientNodeSelectStart, _transientNodeSelectStart);
        viewModel.UpdateTransientNodeSelectionRect(_transientNodeSelectRect);
        GeometryCanvas.CaptureMouse();
    }
    private void GeometryCanvas_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (GetGeometryViewModel(DataContext) is GeometryDefinitionViewModel viewModel)
        {
            if (DataContext is MeshTabCanvasWrapper meshWrapper &&
                meshWrapper.IsExcavateElementsMode &&
                e.LeftButton == MouseButtonState.Pressed)
            {
                // In element-excavation mode, left-click should not start zoom-box drag.
                // Do not mark handled here; MouseLeftButtonDown performs the hit-test excavation.
                return;
            }

            if (DataContext is MeshTabCanvasWrapper bcModeWrapper &&
                bcModeWrapper.IsBcManualImportMode &&
                e.LeftButton == MouseButtonState.Pressed)
            {
                // Vertices/Nodes import mode owns left-drag for marquee selection.
                if (_isZoomBoxSelecting)
                {
                    _isZoomBoxSelecting = false;
                    GeometryCanvas.ReleaseMouseCapture();
                    UpdateZoomRectVisual(Rect.Empty);
                }
                return;
            }

            if (viewModel.IsTransientNodeSelectionModeActive && e.LeftButton == MouseButtonState.Pressed)
            {
                if (_isZoomBoxSelecting)
                {
                    _isZoomBoxSelecting = false;
                    GeometryCanvas.ReleaseMouseCapture();
                    UpdateZoomRectVisual(Rect.Empty);
                }
                return;
            }

            // Completely skip zoom/pan handling when in tunnel circle drawing mode
            // Only skip for middle/right button - left button is handled in GeometryCanvas_OnMouseLeftButtonDown
            if (viewModel.IsTunnelCircleDrawingMode && (e.MiddleButton == MouseButtonState.Pressed || e.RightButton == MouseButtonState.Pressed))
            {
                // Reset any zoom box selection state that might be lingering
                if (_isZoomBoxSelecting)
                {
                    _isZoomBoxSelecting = false;
                    GeometryCanvas.ReleaseMouseCapture();
                    UpdateZoomRectVisual(Rect.Empty);
                }
                return;
            }

            // Don't start zoom box selection when in geometry vertices edit mode or new point mode
            // This prevents the double-click issue where zoom box selection triggers
            // the left button down handler again — unless Zoom to Area mode is active.
            if (!ZoomAreaModeEnabled &&
                (viewModel.IsDrawArcMode || viewModel.IsDrawLineMode || viewModel.IsDrawCircleMode || viewModel.IsGeometryVerticesEditMode || viewModel.IsNewPointMode || viewModel.IsSurfacePolygonPickMode) && e.LeftButton == MouseButtonState.Pressed)
            {
                // Reset any existing zoom box selection state
                if (_isZoomBoxSelecting)
                {
                    _isZoomBoxSelecting = false;
                    GeometryCanvas.ReleaseMouseCapture();
                    UpdateZoomRectVisual(Rect.Empty);
                }
                e.Handled = true; // Mark as handled to prevent zoom box selection
                return;
            }

            // Don't start zoom box selection when in excavation mode
            // This allows excavation clicks to be handled properly
            if (viewModel.IsExcavationMode && e.LeftButton == MouseButtonState.Pressed)
            {
                return;
            }

            // Don't start zoom box selection when in material-assign mode
            if (viewModel.IsMaterialAssignMode && e.LeftButton == MouseButtonState.Pressed)
            {
                return;
            }
        }

        if (e.ClickCount == 2 && e.LeftButton == MouseButtonState.Pressed)
        {
            if (DoubleClickFitsGeometry
                && GetGeometryViewModel(DataContext) is GeometryDefinitionViewModel fitVm)
            {
                fitVm.FitAxisBoundsToVertexExtents();
            }
            ResetView();
            e.Handled = true;
            return;
        }

        if (ZoomPanModeEnabled && CanStartPanFromLeftButton() && e.LeftButton == MouseButtonState.Pressed)
        {
            BeginPan(e.GetPosition(GeometryCanvas));
            e.Handled = true;
            return;
        }

        if (ZoomPanModeEnabled && e.MiddleButton == MouseButtonState.Pressed)
        {
            BeginPan(e.GetPosition(GeometryCanvas));
            e.Handled = true;
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed && !ZoomPanModeEnabled)
        {
            _isZoomBoxSelecting = true;
            _zoomStart = e.GetPosition(GeometryCanvas);
            _zoomRect = new Rect(_zoomStart, _zoomStart);
            UpdateZoomRectVisual(_zoomRect);
            GeometryCanvas.CaptureMouse();
            e.Handled = true;
        }
    }

    private void GeometryCanvas_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (GetGeometryViewModel(DataContext) is GeometryDefinitionViewModel viewModel && sender is IInputElement inputElement)
        {
            if (_isDrawingGeometryCircle && !viewModel.IsDrawCircleMode)
            {
                _isDrawingGeometryCircle = false;
                GeometryCanvas.ReleaseMouseCapture();
            }

            var currentCanvas = e.GetPosition(inputElement);
            _lastCanvasPointer = currentCanvas;
            var hoverPosition = ScreenToWorld(currentCanvas);
            viewModel.UpdateGeometryCanvasCoordinates(hoverPosition);

            if (viewModel.IsMeasureMode && viewModel.HasMeasurementStart)
            {
                viewModel.UpdateMeasurement(hoverPosition);
                e.Handled = true;
                return;
            }

            if (_isBulkPointDeleteSelecting && viewModel.IsBulkPointDeleteMode && e.LeftButton == MouseButtonState.Pressed)
            {
                _bulkPointDeleteRect = new Rect(_bulkPointDeleteStart, hoverPosition);
                viewModel.UpdateBulkPointDeleteSelection(_bulkPointDeleteRect);
                e.Handled = true;
                return;
            }

            if (_isBcImportSelecting
                && DataContext is MeshTabCanvasWrapper bcMoveWrapper
                && bcMoveWrapper.IsBcManualImportMode
                && bcMoveWrapper.BoundaryConditions != null
                && e.LeftButton == MouseButtonState.Pressed)
            {
                _bcImportRect = new Rect(_bcImportStart, hoverPosition);
                bcMoveWrapper.BoundaryConditions.UpdateBcImportSelection(_bcImportRect);
                e.Handled = true;
                return;
            }

            if (_isTransientNodeSelecting
                && viewModel.IsTransientNodeSelectionModeActive
                && e.LeftButton == MouseButtonState.Pressed)
            {
                _transientNodeSelectRect = new Rect(_transientNodeSelectStart, hoverPosition);
                viewModel.UpdateTransientNodeSelectionRect(_transientNodeSelectRect);
                e.Handled = true;
                return;
            }

            // Update tunnel circle preview if drawing
            if (_isDrawingTunnelCircle && viewModel.IsTunnelCircleDrawingMode)
            {
                viewModel.UpdateTunnelCirclePreview(hoverPosition);
            }

            if (_isDrawingGeometryCircle && viewModel.IsDrawCircleMode)
                viewModel.UpdateGeometryCirclePreview(hoverPosition);

            // Update excavation rectangle preview if drawing
            if (viewModel.IsExcavationMode && viewModel.ExcavationRectangleStart.HasValue)
            {
                // Update the end point for rectangle preview (in canvas coordinates)
                viewModel.ExcavationRectangleEnd = currentCanvas;
            }
        }

        if (_isPanning)
        {
            var current = e.GetPosition(GeometryCanvas);
            var delta = current - _lastPanPoint;
            _lastPanPoint = current;
            Pan(delta.X, delta.Y);
            return;
        }

        if (_isZoomBoxSelecting && e.LeftButton == MouseButtonState.Pressed)
        {
            var current = e.GetPosition(GeometryCanvas);
            _zoomRect = new Rect(_zoomStart, current);
            UpdateZoomRectVisual(_zoomRect);
            return;
        }

        if (_isDraggingTunnelCircle && GetGeometryViewModel(DataContext) is GeometryDefinitionViewModel tunnelDragViewModel)
        {
            var position = ScreenToWorld(e.GetPosition(GeometryCanvas));
            var target = position + _tunnelCircleDragOffset;
            tunnelDragViewModel.MoveTunnelCircle(_draggedTunnelCircleIndex, target);
            tunnelDragViewModel.UpdateGeometryCanvasCoordinates(target);
            return;
        }

        if (!_isDraggingCircleCenter || GetGeometryViewModel(DataContext) is not GeometryDefinitionViewModel dragViewModel)
        {
            return;
        }

        var position2 = ScreenToWorld(e.GetPosition(GeometryCanvas));
        var target2 = position2 + _circleDragOffset;
        dragViewModel.MoveBoxMethodCircleCenter(target2);
        dragViewModel.UpdateGeometryCanvasCoordinates(target2);
    }

    private void GeometryCanvas_OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isBulkPointDeleteSelecting && e.ChangedButton == MouseButton.Left && GetGeometryViewModel(DataContext) is GeometryDefinitionViewModel bulkDeleteVm)
        {
            _isBulkPointDeleteSelecting = false;
            GeometryCanvas.ReleaseMouseCapture();

            var end = ScreenToWorld(e.GetPosition(GeometryCanvas));
            var rect = new Rect(_bulkPointDeleteStart, end);
            bulkDeleteVm.ClearBulkPointDeleteSelection();

            if (Math.Abs(rect.Width) >= MinBulkPointDeleteBoxSize && Math.Abs(rect.Height) >= MinBulkPointDeleteBoxSize)
            {
                bulkDeleteVm.DeleteGeometryVerticesInCanvasRect(rect);
            }
            else
            {
                bulkDeleteVm.DeleteNearestGeometryVertex(end, BulkPointDeleteClickTolerance);
            }

            e.Handled = true;
            return;
        }

        if (_isBcImportSelecting
            && e.ChangedButton == MouseButton.Left
            && DataContext is MeshTabCanvasWrapper bcUpWrapper
            && bcUpWrapper.BoundaryConditions != null)
        {
            _isBcImportSelecting = false;
            GeometryCanvas.ReleaseMouseCapture();

            var end = ScreenToWorld(e.GetPosition(GeometryCanvas));
            var rect = new Rect(_bcImportStart, end);
            bcUpWrapper.BoundaryConditions.CompleteBcImportSelection(rect, BcImportClickTolerance);
            e.Handled = true;
            return;
        }

        if (_isTransientNodeSelecting
            && e.ChangedButton == MouseButton.Left
            && GetGeometryViewModel(DataContext) is GeometryDefinitionViewModel transientNodeVm)
        {
            _isTransientNodeSelecting = false;
            GeometryCanvas.ReleaseMouseCapture();
            var end = ScreenToWorld(e.GetPosition(GeometryCanvas));
            var rect = new Rect(_transientNodeSelectStart, end);
            transientNodeVm.CompleteTransientNodeSelection(rect, TransientNodeSelectClickTolerance);
            e.Handled = true;
            return;
        }

        // Geometry definition circle (line segments)
        if (_isDrawingGeometryCircle && e.ChangedButton == MouseButton.Left && GetGeometryViewModel(DataContext) is GeometryDefinitionViewModel geoCircleVm)
        {
            _isDrawingGeometryCircle = false;
            GeometryCanvas.ReleaseMouseCapture();
            geoCircleVm.FinishGeometryCircleDrawing();
            e.Handled = true;
            return;
        }

        // Handle tunnel circle drawing completion
        if (_isDrawingTunnelCircle && e.ChangedButton == MouseButton.Left && GetGeometryViewModel(DataContext) is GeometryDefinitionViewModel viewModel)
        {
            _isDrawingTunnelCircle = false;
            GeometryCanvas.ReleaseMouseCapture();
            viewModel.FinishTunnelCircleDrawing();
            // The FinishTunnelCircleDrawing method already sets IsTunnelCircleDrawingMode to false
            // so we don't need to cancel here
            e.Handled = true;
            return;
        }

        if (_isPanning && (e.ChangedButton == MouseButton.Left || e.ChangedButton == MouseButton.Middle))
        {
            EndPan(e);
            return;
        }

        if (_isZoomBoxSelecting && e.ChangedButton == MouseButton.Left)
        {
            _isZoomBoxSelecting = false;
            GeometryCanvas.ReleaseMouseCapture();
            var rect = _zoomRect;
            UpdateZoomRectVisual(Rect.Empty);

            if (rect.Width >= MinZoomBoxSize && rect.Height >= MinZoomBoxSize)
            {
                ZoomToScreenRect(rect);
            }
            else if (!_isDrawingTunnelCircle && !_isDrawingGeometryCircle)
            {
                // Only trigger left button down if we're not drawing a tunnel circle
                // and not in geometry vertices edit mode or excavation mode (to prevent double vertex creation)
                if (GetGeometryViewModel(DataContext) is GeometryDefinitionViewModel vm)
                {
                    if (!vm.IsGeometryVerticesEditMode && !vm.IsExcavationMode)
                    {
                        GeometryCanvas_OnMouseLeftButtonDown(sender, e);
                    }
                }
            }

            e.Handled = true;
            return;
        }

        if (_isDraggingTunnelCircle)
        {
            _isDraggingTunnelCircle = false;
            _draggedTunnelCircleIndex = -1;
            GeometryCanvas.ReleaseMouseCapture();
            e.Handled = true;
            return;
        }

        if (!_isDraggingCircleCenter)
        {
            return;
        }

        _isDraggingCircleCenter = false;
        GeometryCanvas.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void GeometryCanvas_OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (GetGeometryViewModel(DataContext) is GeometryDefinitionViewModel viewModel)
        {
            viewModel.UpdateGeometryCanvasCoordinates(new Point(double.NaN, double.NaN));
        }

        _lastCanvasPointer = null;

        if (_isPanning && Mouse.LeftButton != MouseButtonState.Pressed && Mouse.MiddleButton != MouseButtonState.Pressed)
        {
            EndPan();
        }

        if (_isZoomBoxSelecting && Mouse.LeftButton != MouseButtonState.Pressed)
        {
            _isZoomBoxSelecting = false;
            UpdateZoomRectVisual(Rect.Empty);
            GeometryCanvas.ReleaseMouseCapture();
        }

        if (_isBulkPointDeleteSelecting && Mouse.LeftButton != MouseButtonState.Pressed)
        {
            _isBulkPointDeleteSelecting = false;
            GeometryCanvas.ReleaseMouseCapture();
            if (GetGeometryViewModel(DataContext) is GeometryDefinitionViewModel bulkDeleteVm)
                bulkDeleteVm.ClearBulkPointDeleteSelection();
        }

        if (_isBcImportSelecting && Mouse.LeftButton != MouseButtonState.Pressed)
        {
            _isBcImportSelecting = false;
            GeometryCanvas.ReleaseMouseCapture();
            if (DataContext is MeshTabCanvasWrapper leaveWrapper)
                leaveWrapper.BoundaryConditions?.ClearBcImportSelection();
        }

        if (_isDraggingTunnelCircle && Mouse.LeftButton != MouseButtonState.Pressed)
        {
            _isDraggingTunnelCircle = false;
            _draggedTunnelCircleIndex = -1;
            GeometryCanvas.ReleaseMouseCapture();
            return;
        }

        if (!_isDraggingCircleCenter || Mouse.LeftButton == MouseButtonState.Pressed)
        {
            return;
        }

        _isDraggingCircleCenter = false;
        GeometryCanvas.ReleaseMouseCapture();
    }

    private void GeometryCanvas_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var factor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
        ZoomAt(factor, e.GetPosition(GeometryCanvas));
        UpdateHoverCoordinates();

        e.Handled = true;
    }

    private void ZoomAt(double scaleFactor, Point center)
    {
        var currentScale = _worldMatrix.M11;
        var requested = currentScale * scaleFactor;
        var clamped = Math.Clamp(requested, MinScale, MaxScale);
        var appliedFactor = clamped / currentScale;

        _worldMatrix.ScaleAt(appliedFactor, appliedFactor, center.X, center.Y);
        _zoomFactor = clamped;
        // Do not PinAxes/Clamp here: both fought zoom-at-cursor and Clamp scaled the
        // view back down whenever content exceeded the viewport (auto zoom-out).
        ApplyTransform();
        PushSharedTransform();
        NotifyZoomUpdated();
    }

    private void Pan(double dx, double dy)
    {
        _worldMatrix.Translate(dx, dy);
        ApplyTransform();
        PushSharedTransform();
        NotifyZoomUpdated();
    }

    private bool CanStartPanFromLeftButton()
    {
        if (!ZoomPanModeEnabled)
            return false;

        if (DataContext is MeshTabCanvasWrapper { IsExcavateElementsMode: true })
            return false;

        return true;
    }

    private void BeginPan(Point start)
    {
        _isPanning = true;
        _lastPanPoint = start;
        _isZoomBoxSelecting = false;
        UpdateZoomRectVisual(Rect.Empty);
        GeometryCanvas.CaptureMouse();
    }

    private void EndPan(MouseButtonEventArgs? e = null)
    {
        if (!_isPanning)
            return;

        _isPanning = false;
        GeometryCanvas.ReleaseMouseCapture();
        if (e != null)
            e.Handled = true;
    }

    private void UpdateZoomRectVisual(Rect rect)
    {
        if (ZoomRect is null)
        {
            return;
        }

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

    private void ZoomToScreenRect(Rect rect)
    {
        if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var viewportWidth = GeometryCanvas.ActualWidth;
        var viewportHeight = GeometryCanvas.ActualHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            return;
        }

        // Scale so the selected rect fills the full canvas
        var scaleFactor = Math.Min(viewportWidth / rect.Width, viewportHeight / rect.Height);
        var currentScale = _worldMatrix.M11;
        var requested = currentScale * scaleFactor;
        var clamped = Math.Clamp(requested, MinScale, MaxScale);
        var appliedFactor = clamped / currentScale;

        var rectCenter = new Point(rect.X + rect.Width / 2.0, rect.Y + rect.Height / 2.0);
        var viewportCenter = new Point(viewportWidth / 2.0, viewportHeight / 2.0);

        // Scale around rect center so the selected area expands to fill the canvas
        _worldMatrix.ScaleAt(appliedFactor, appliedFactor, rectCenter.X, rectCenter.Y);
        _zoomFactor = clamped;

        // Pan so the selected rect is centered in the viewport
        var panDx = viewportCenter.X - rectCenter.X;
        var panDy = viewportCenter.Y - rectCenter.Y;
        _worldMatrix.Translate(panDx, panDy);

        // Skip PinAxesToBorder and ClampContentIntoView - we intentionally zoomed the rect to fill the canvas
        ApplyTransform();
        PushSharedTransform();
        NotifyZoomUpdated();
    }

    private Rect GetTransformedGeometryBounds()
    {
        return GetTransformedGeometryBounds(_worldMatrix);
    }

    private Rect GetTransformedGeometryBounds(Matrix basis)
    {
        if (GetGeometryViewModel(DataContext) is not GeometryDefinitionViewModel viewModel ||
            viewModel.GeometryPathPoints is null ||
            viewModel.GeometryPathPoints.Count == 0)
        {
            return Rect.Empty;
        }

        var points = viewModel.GeometryPathPoints;
        var first = basis.Transform(points[0]);
        var bounds = new Rect(first, first);
        for (int i = 1; i < points.Count; i++)
        {
            bounds.Union(basis.Transform(points[i]));
        }

        return bounds;
    }

    /// <summary>
    /// Pan-only: nudge content back if it has slid fully off-canvas.
    /// Never changes zoom — scaling here was the auto zoom-out bug.
    /// </summary>
    private void ClampContentIntoView()
    {
        var viewportWidth = GeometryCanvas.ActualWidth;
        var viewportHeight = GeometryCanvas.ActualHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0)
            return;

        var bounds = GetTransformedGeometryBounds(_worldMatrix);
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
            return;

        double dx = 0;
        double dy = 0;

        // Only correct when the whole model is outside the viewport (lost), not when
        // the user has intentionally zoomed so content is larger than the view.
        if (bounds.Right < 0)
            dx = -bounds.X;
        else if (bounds.X > viewportWidth)
            dx = viewportWidth - bounds.Right;

        if (bounds.Bottom < 0)
            dy = -bounds.Y;
        else if (bounds.Y > viewportHeight)
            dy = viewportHeight - bounds.Bottom;

        if (Math.Abs(dx) > double.Epsilon || Math.Abs(dy) > double.Epsilon)
            _worldMatrix.Translate(dx, dy);
    }

    /// <summary>Fit geometry into the viewport (Reset View / double-click only).</summary>
    private void FitContentIntoView()
    {
        var viewportWidth = GeometryCanvas.ActualWidth;
        var viewportHeight = GeometryCanvas.ActualHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0)
            return;

        var bounds = GetTransformedGeometryBounds(_worldMatrix);
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
            return;

        if (bounds.Width > viewportWidth || bounds.Height > viewportHeight)
        {
            var fitScale = Math.Min(viewportWidth / bounds.Width, viewportHeight / bounds.Height);
            var currentScale = _worldMatrix.M11;
            if (Math.Abs(currentScale) < 1e-12)
                return;
            var requested = currentScale * fitScale;
            var clamped = Math.Clamp(requested, MinScale, MaxScale);
            var applied = clamped / currentScale;
            var center = new Point(bounds.X + bounds.Width * 0.5, bounds.Y + bounds.Height * 0.5);
            _worldMatrix.ScaleAt(applied, applied, center.X, center.Y);
            _zoomFactor = clamped;
            bounds = GetTransformedGeometryBounds(_worldMatrix);
            if (bounds.IsEmpty)
                return;
        }

        double dx = 0;
        double dy = 0;
        if (bounds.X < 0)
            dx = -bounds.X;
        else if (bounds.Right > viewportWidth)
            dx = viewportWidth - bounds.Right;
        if (bounds.Y < 0)
            dy = -bounds.Y;
        else if (bounds.Bottom > viewportHeight)
            dy = viewportHeight - bounds.Bottom;
        if (Math.Abs(dx) > double.Epsilon || Math.Abs(dy) > double.Epsilon)
            _worldMatrix.Translate(dx, dy);
    }

    /// <summary>Zoom the canvas by a multiplicative factor, centred on the viewport mid-point.</summary>
    public void ZoomBy(double factor)
    {
        if (GeometryCanvas == null) return;
        var center = new Point(GeometryCanvas.ActualWidth / 2, GeometryCanvas.ActualHeight / 2);
        ZoomAt(factor, center);
    }

    public void ResetView()
    {
        _worldMatrix = Matrix.Identity;
        _zoomFactor = 1.0;
        PinAxesToBorder();
        FitContentIntoView();
        ApplyTransform();

        PushSharedTransform();
        NotifyZoomUpdated();
    }

    private void PinAxesToBorder()
    {
        var viewportHeight = GeometryCanvas.ActualHeight;
        if (viewportHeight <= 0)
        {
            return;
        }

        // Keep axes pinned to left/bottom by fixing transformed origin to (0, viewportHeight).
        var origin = _worldMatrix.Transform(new Point(0, viewportHeight));
        var dx = -origin.X;
        var dy = viewportHeight - origin.Y;

        if (Math.Abs(dx) > double.Epsilon || Math.Abs(dy) > double.Epsilon)
        {
            _worldMatrix.Translate(dx, dy);
        }
    }

    private void NotifyZoomUpdated()
    {
        if (GetGeometryViewModel(DataContext) is GeometryDefinitionViewModel viewModel)
        {
            viewModel.UpdateViewTransform(_worldMatrix, _zoomFactor);
            UpdateHoverCoordinates();
        }
    }

    private void PushSharedTransform()
    {
        if (_isApplyingSharedTransform)
        {
            return;
        }

        _sharedWorldMatrix = _worldMatrix;
        _sharedZoomFactor = _zoomFactor;

        SharedTransformChanged?.Invoke(_sharedWorldMatrix, _sharedZoomFactor);

        foreach (var canvas in Instances)
        {
            if (ReferenceEquals(canvas, this))
            {
                continue;
            }

            canvas.ApplySharedTransform(_worldMatrix, _zoomFactor);
        }
    }

    private void ApplySharedTransform(Matrix matrix, double zoomFactor)
    {
        _isApplyingSharedTransform = true;
        _worldMatrix = matrix;
        _zoomFactor = zoomFactor;
        ApplyTransform();
        NotifyZoomUpdated();
        _isApplyingSharedTransform = false;
    }

    private void UpdateHoverCoordinates()
    {
        if (_lastCanvasPointer is null || GetGeometryViewModel(DataContext) is not GeometryDefinitionViewModel viewModel)
        {
            return;
        }

        var world = ScreenToWorld(_lastCanvasPointer.Value);
        viewModel.UpdateGeometryCanvasCoordinates(world);
    }

    /// <summary>
    /// Captures the canvas exactly as shown on screen for print/export.
    /// Uses VisualBrush because RenderTargetBitmap on the outer control can clip
    /// Viewbox-scaled, pan/zoomed content on the right/bottom edges.
    /// </summary>
    public BitmapSource? CaptureForPrint()
    {
        UpdateLayout();
        if (ActualWidth <= 0 || ActualHeight <= 0)
            return null;

        var widthDip = ActualWidth;
        var heightDip = ActualHeight;
        var dpi = VisualTreeHelper.GetDpi(this);

        var drawingVisual = new DrawingVisual();
        using (var dc = drawingVisual.RenderOpen())
        {
            var brush = new VisualBrush(this)
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
                Viewbox = new Rect(0, 0, widthDip, heightDip),
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, widthDip, heightDip),
                ViewportUnits = BrushMappingMode.Absolute
            };
            dc.DrawRectangle(brush, null, new Rect(0, 0, widthDip, heightDip));
        }

        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(widthDip * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(heightDip * dpi.DpiScaleY)),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(drawingVisual);
        bitmap.Freeze();
        return bitmap;
    }

    private void GeometryCanvas_OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var geo = GetGeometryViewModel(DataContext);
        var showFinish = geo?.CanFinishCanvasTool == true;
        var showPanDone = ZoomPanModeEnabled;
        if (CanvasFinishMenuItem != null)
            CanvasFinishMenuItem.Visibility = showFinish ? Visibility.Visible : Visibility.Collapsed;
        if (CanvasPanDoneMenuItem != null)
            CanvasPanDoneMenuItem.Visibility = showPanDone ? Visibility.Visible : Visibility.Collapsed;
        if (!showFinish && !showPanDone)
            e.Handled = true;
    }

    private void CanvasFinish_Click(object sender, RoutedEventArgs e)
    {
        GetGeometryViewModel(DataContext)?.FinishActiveCanvasTool();
    }

    private void PanModeDone_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current?.MainWindow?.DataContext is MainWindowViewModel vm)
            vm.ZoomPanModeEnabled = false;
    }
}
