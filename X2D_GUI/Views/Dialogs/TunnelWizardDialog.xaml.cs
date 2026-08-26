using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Views.Dialogs;

/// <summary>
/// Hosts <see cref="TunnelWizardViewModel"/> in a six-step modal dialog and projects the live
/// preview polygons (axis space) onto the right-hand <c>PreviewCanvas</c>. The applier itself
/// runs in the parent (MainWindow) once the user clicks Apply, so this code-behind is purely
/// presentation-level.
/// </summary>
public partial class TunnelWizardDialog : Window
{
    public TunnelWizardViewModel ViewModel { get; }

    /// <summary>True iff the user clicked Apply (and the apply succeeded — set by the parent).</summary>
    public bool ApplyConfirmed { get; private set; }

    public TunnelWizardDialog(TunnelWizardViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = ViewModel;

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.ApplyRequested  += OnApplyRequested;
        ViewModel.CancelRequested += OnCancelRequested;

        Loaded   += (_, _) => RefreshPreview();
        SizeChanged += (_, _) => RefreshPreview();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TunnelWizardViewModel.PreviewTunnelAxis) ||
            e.PropertyName == nameof(TunnelWizardViewModel.PreviewDomainAxis))
        {
            RefreshPreview();
        }
    }

    private void OnApplyRequested(object? sender, EventArgs e)
    {
        ApplyConfirmed = true;
        DialogResult = true;
        Close();
    }

    private void OnCancelRequested(object? sender, EventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void AutoSizeDomain_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.AutoSizeDomain();
    }

    /// <summary>
    /// Re-projects the wizard's axis-space preview arrays onto the canvas using a uniform-scale
    /// fit-to-canvas transform with a small padding. Centred so circle / horseshoe presets sit
    /// in the middle of the preview pane regardless of aspect ratio.
    /// </summary>
    private void RefreshPreview()
    {
        var canvasW = PreviewCanvas.ActualWidth;
        var canvasH = PreviewCanvas.ActualHeight;
        if (canvasW < 4 || canvasH < 4) return;

        var domain = ViewModel.PreviewDomainAxis;
        var tunnel = ViewModel.PreviewTunnelAxis;
        if (domain.Length < 2 && tunnel.Length < 2) return;

        // Compute the axis bbox spanning both polylines (so the tunnel always stays in view
        // even before the user opens step 3).
        double xMin = double.MaxValue, xMax = double.MinValue;
        double yMin = double.MaxValue, yMax = double.MinValue;
        Accumulate(domain, ref xMin, ref xMax, ref yMin, ref yMax);
        Accumulate(tunnel, ref xMin, ref xMax, ref yMin, ref yMax);
        if (!(xMax > xMin) || !(yMax > yMin)) return;

        const double padPx = 16;
        double availW = Math.Max(1, canvasW - 2 * padPx);
        double availH = Math.Max(1, canvasH - 2 * padPx);
        double sx = availW / (xMax - xMin);
        double sy = availH / (yMax - yMin);
        double s  = Math.Min(sx, sy);
        double offsetX = padPx + (availW - s * (xMax - xMin)) * 0.5;
        double offsetY = padPx + (availH - s * (yMax - yMin)) * 0.5;

        DomainPolyline.Points = Project(domain, s, offsetX, offsetY, xMin, yMax, canvasH);
        TunnelPolyline.Points = Project(tunnel, s, offsetX, offsetY, xMin, yMax, canvasH);
    }

    private static void Accumulate(Point[] pts, ref double xMin, ref double xMax, ref double yMin, ref double yMax)
    {
        foreach (var p in pts)
        {
            if (p.X < xMin) xMin = p.X;
            if (p.X > xMax) xMax = p.X;
            if (p.Y < yMin) yMin = p.Y;
            if (p.Y > yMax) yMax = p.Y;
        }
    }

    private static PointCollection Project(Point[] axisPts, double scale, double offsetX, double offsetY,
                                           double xMin, double yMax, double canvasH)
    {
        var pts = new PointCollection(axisPts.Length);
        foreach (var p in axisPts)
        {
            double cx = offsetX + (p.X - xMin) * scale;
            // Y axis is up in math coords, down on the canvas → flip.
            double cy = offsetY + (yMax - p.Y) * scale;
            pts.Add(new Point(cx, cy));
        }
        return pts;
    }
}
