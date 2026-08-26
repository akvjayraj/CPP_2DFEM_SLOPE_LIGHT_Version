using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using GeotechSlopeApp.ViewModels.Dialogs;

namespace GeotechSlopeApp.Views.Dialogs;

public partial class SlopeImageImportWizardDialog : Window
{
    public SlopeImageImportWizardDialog(SlopeImageImportWizardViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = ViewModel;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.CloseAccepted += OnCloseAccepted;
        Loaded += (_, _) => RefreshMarkers();
        SizeChanged += (_, _) => RefreshMarkers();
        Closed += (_, _) =>
        {
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            ViewModel.CloseAccepted -= OnCloseAccepted;
        };
    }

    public SlopeImageImportWizardViewModel ViewModel { get; }

    private void OnCloseAccepted()
    {
        DialogResult = true;
        Close();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SlopeImageImportWizardViewModel.PendingImagePoint1)
            or nameof(SlopeImageImportWizardViewModel.PendingImagePoint2)
            or nameof(SlopeImageImportWizardViewModel.Mode)
            or nameof(SlopeImageImportWizardViewModel.Bitmap)
            or nameof(SlopeImageImportWizardViewModel.ImagePixelWidth))
            RefreshMarkers();
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.TryBuildResult(out _, out var error))
        {
            MessageBox.Show(this, error, ViewModel.WindowTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void PreviewImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!ViewModel.IsTwoPointMode) return;
        if (!TryMapClickToImagePixel(e.GetPosition(PreviewImage), out var imagePx))
            return;
        ViewModel.HandlePreviewClick(imagePx);
        RefreshMarkers();
        e.Handled = true;
    }

    private bool TryMapClickToImagePixel(Point posInImageControl, out Point imagePixel)
    {
        imagePixel = default;
        var w = PreviewImage.ActualWidth;
        var h = PreviewImage.ActualHeight;
        if (w < 1 || h < 1) return false;

        var iw = ViewModel.ImagePixelWidth;
        var ih = ViewModel.ImagePixelHeight;
        if (iw < 1 || ih < 1) return false;

        // Uniform stretch: letterbox inside the Image control.
        var scale = Math.Min(w / iw, h / ih);
        var drawnW = iw * scale;
        var drawnH = ih * scale;
        var ox = (w - drawnW) * 0.5;
        var oy = (h - drawnH) * 0.5;

        var x = (posInImageControl.X - ox) / scale;
        var y = (posInImageControl.Y - oy) / scale;
        if (x < -0.5 || y < -0.5 || x > iw + 0.5 || y > ih + 0.5)
            return false;

        imagePixel = new Point(
            Math.Clamp(x, 0, iw),
            Math.Clamp(y, 0, ih));
        return true;
    }

    private void RefreshMarkers()
    {
        MarkerLayer.Children.Clear();
        MarkerLayer.Width = PreviewImage.ActualWidth;
        MarkerLayer.Height = PreviewImage.ActualHeight;

        var w = PreviewImage.ActualWidth;
        var h = PreviewImage.ActualHeight;
        if (w < 1 || h < 1) return;

        var iw = ViewModel.ImagePixelWidth;
        var ih = ViewModel.ImagePixelHeight;
        if (iw < 1 || ih < 1) return;
        var scale = Math.Min(w / iw, h / ih);
        var drawnW = iw * scale;
        var drawnH = ih * scale;
        var ox = (w - drawnW) * 0.5;
        var oy = (h - drawnH) * 0.5;

        void AddMarker(Point? imagePx, Brush fill, string label)
        {
            if (imagePx == null) return;
            var cx = ox + imagePx.Value.X * scale;
            var cy = oy + imagePx.Value.Y * scale;
            var ellipse = new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = fill,
                Stroke = Brushes.White,
                StrokeThickness = 1.5
            };
            Canvas.SetLeft(ellipse, cx - 6);
            Canvas.SetTop(ellipse, cy - 6);
            MarkerLayer.Children.Add(ellipse);
            var text = new TextBlock
            {
                Text = label,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12
            };
            Canvas.SetLeft(text, cx + 8);
            Canvas.SetTop(text, cy - 10);
            MarkerLayer.Children.Add(text);
        }

        AddMarker(ViewModel.PendingImagePoint1, new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8)), "1");
        AddMarker(ViewModel.PendingImagePoint2, new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)), "2");
    }
}
