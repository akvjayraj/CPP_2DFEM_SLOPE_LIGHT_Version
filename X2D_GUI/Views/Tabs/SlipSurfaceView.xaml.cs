using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GeotechSlopeApp.Views.Tabs;

public partial class SlipSurfaceView : UserControl
{
    private bool _isSliceScrollSync;
    private ScrollViewer? _sliceInfoScrollViewer;
    private bool _isSliceScrollSync_HBased;
    private ScrollViewer? _sliceInfoScrollViewer_HBased;

    public SlipSurfaceView()
    {
        InitializeComponent();
    }

    private void SliceInfoGrid_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGrid grid)
        {
            return;
        }

        var scrollViewer = FindScrollViewer(grid);
        if (scrollViewer is null)
        {
            return;
        }

        if (!ReferenceEquals(_sliceInfoScrollViewer, scrollViewer))
        {
            if (_sliceInfoScrollViewer is not null)
            {
                _sliceInfoScrollViewer.ScrollChanged -= SliceInfoScrollViewer_OnScrollChanged;
            }

            _sliceInfoScrollViewer = scrollViewer;
            _sliceInfoScrollViewer.ScrollChanged += SliceInfoScrollViewer_OnScrollChanged;
        }

        UpdateSlider(SliceInfoHorizontalSlider, scrollViewer.ExtentWidth, scrollViewer.ViewportWidth, scrollViewer.HorizontalOffset);
        UpdateSlider(SliceInfoVerticalSlider, scrollViewer.ExtentHeight, scrollViewer.ViewportHeight, scrollViewer.VerticalOffset);
    }

    private void SliceInfoScrollViewer_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isSliceScrollSync)
        {
            return;
        }

        _isSliceScrollSync = true;
        UpdateSlider(SliceInfoHorizontalSlider, e.ExtentWidth, e.ViewportWidth, e.HorizontalOffset);
        UpdateSlider(SliceInfoVerticalSlider, e.ExtentHeight, e.ViewportHeight, e.VerticalOffset);
        _isSliceScrollSync = false;
    }

    private void SliceInfoHorizontalSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isSliceScrollSync || _sliceInfoScrollViewer is null)
        {
            return;
        }

        _isSliceScrollSync = true;
        var clampedOffset = Math.Clamp(e.NewValue, SliceInfoHorizontalSlider.Minimum, SliceInfoHorizontalSlider.Maximum);
        _sliceInfoScrollViewer.ScrollToHorizontalOffset(clampedOffset);
        _isSliceScrollSync = false;
    }

    private void SliceInfoVerticalSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isSliceScrollSync || _sliceInfoScrollViewer is null)
        {
            return;
        }

        _isSliceScrollSync = true;
        var clampedOffset = Math.Clamp(e.NewValue, SliceInfoVerticalSlider.Minimum, SliceInfoVerticalSlider.Maximum);
        _sliceInfoScrollViewer.ScrollToVerticalOffset(clampedOffset);
        _isSliceScrollSync = false;
    }

    private static void UpdateSlider(Slider? slider, double extent, double viewport, double offset)
    {
        if (slider is null)
        {
            return;
        }

        var max = Math.Max(0.0, extent - viewport);
        slider.Maximum = max;
        slider.SmallChange = viewport > 0 ? Math.Max(1.0, viewport / 10.0) : 1.0;
        slider.LargeChange = viewport > 0 ? Math.Max(1.0, viewport / 2.0) : 5.0;
        slider.IsEnabled = max > 0.0;
        var clampedOffset = double.IsNaN(offset) ? 0.0 : Math.Clamp(offset, slider.Minimum, max);
        slider.Value = clampedOffset;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject? root)
    {
        if (root is null)
        {
            return null;
        }

        if (root is ScrollViewer viewer)
        {
            return viewer;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var result = FindScrollViewer(child);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private void SliceInfoGrid_HBased_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGrid grid)
        {
            return;
        }

        var scrollViewer = FindScrollViewer(grid);
        if (scrollViewer is null)
        {
            return;
        }

        if (!ReferenceEquals(_sliceInfoScrollViewer_HBased, scrollViewer))
        {
            if (_sliceInfoScrollViewer_HBased is not null)
            {
                _sliceInfoScrollViewer_HBased.ScrollChanged -= SliceInfoScrollViewer_HBased_OnScrollChanged;
            }

            _sliceInfoScrollViewer_HBased = scrollViewer;
            _sliceInfoScrollViewer_HBased.ScrollChanged += SliceInfoScrollViewer_HBased_OnScrollChanged;
        }

        UpdateSlider(SliceInfoHorizontalSlider_HBased, scrollViewer.ExtentWidth, scrollViewer.ViewportWidth, scrollViewer.HorizontalOffset);
        UpdateSlider(SliceInfoVerticalSlider_HBased, scrollViewer.ExtentHeight, scrollViewer.ViewportHeight, scrollViewer.VerticalOffset);
    }

    private void SliceInfoScrollViewer_HBased_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isSliceScrollSync_HBased)
        {
            return;
        }

        _isSliceScrollSync_HBased = true;
        UpdateSlider(SliceInfoHorizontalSlider_HBased, e.ExtentWidth, e.ViewportWidth, e.HorizontalOffset);
        UpdateSlider(SliceInfoVerticalSlider_HBased, e.ExtentHeight, e.ViewportHeight, e.VerticalOffset);
        _isSliceScrollSync_HBased = false;
    }

    private void SliceInfoHorizontalSlider_HBased_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isSliceScrollSync_HBased || _sliceInfoScrollViewer_HBased is null)
        {
            return;
        }

        _isSliceScrollSync_HBased = true;
        var clampedOffset = Math.Clamp(e.NewValue, SliceInfoHorizontalSlider_HBased.Minimum, SliceInfoHorizontalSlider_HBased.Maximum);
        _sliceInfoScrollViewer_HBased.ScrollToHorizontalOffset(clampedOffset);
        _isSliceScrollSync_HBased = false;
    }

    private void SliceInfoVerticalSlider_HBased_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isSliceScrollSync_HBased || _sliceInfoScrollViewer_HBased is null)
        {
            return;
        }

        _isSliceScrollSync_HBased = true;
        var clampedOffset = Math.Clamp(e.NewValue, SliceInfoVerticalSlider_HBased.Minimum, SliceInfoVerticalSlider_HBased.Maximum);
        _sliceInfoScrollViewer_HBased.ScrollToVerticalOffset(clampedOffset);
        _isSliceScrollSync_HBased = false;
    }
}
