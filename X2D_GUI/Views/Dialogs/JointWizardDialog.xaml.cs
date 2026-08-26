using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using GeotechSlopeApp.ViewModels.Dialogs;

namespace GeotechSlopeApp.Views.Dialogs;

public partial class JointWizardDialog : Window
{
    public JointWizardViewModel ViewModel { get; }
    public bool ApplyConfirmed { get; private set; }
    public bool ManualDrawConfirmed { get; private set; }

    public JointWizardDialog(JointWizardViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = ViewModel;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.ApplyRequested += OnApplyRequested;
        ViewModel.CancelRequested += OnCancelRequested;
        ViewModel.ManualDrawRequested += OnManualDrawRequested;
        ViewModel.DefinePropertiesRequested += OnDefinePropertiesRequested;
        Loaded += (_, _) => RefreshPreview();
        SizeChanged += (_, _) => RefreshPreview();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(JointWizardViewModel.PreviewGroundAxis)
            or nameof(JointWizardViewModel.PreviewJointsAxis)
            or nameof(JointWizardViewModel.PreviewRegionsAxis)
            or nameof(JointWizardViewModel.SelectedPropertySet))
            RefreshPreview();
    }

    private void OnApplyRequested(object? sender, EventArgs e)
    {
        ApplyConfirmed = true;
        DialogResult = true;
        Close();
    }

    private void OnManualDrawRequested(object? sender, EventArgs e)
    {
        ManualDrawConfirmed = true;
        DialogResult = true;
        Close();
    }

    private void OnCancelRequested(object? sender, EventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnDefinePropertiesRequested(object? sender, EventArgs e)
    {
        var edit = new DefineJointPropertiesViewModel(ViewModel.JointPropertySets);
        var dlg = new DefineJointPropertiesDialog(edit) { Owner = this };
        if (dlg.ShowDialog() == true)
            ViewModel.ApplyPropertySetEdits(edit.WorkingSets);
    }

    private void RefreshPreview()
    {
        var canvasW = PreviewCanvas.ActualWidth;
        var canvasH = PreviewCanvas.ActualHeight;
        if (canvasW < 4 || canvasH < 4)
            return;

        PreviewCanvas.Children.Clear();
        double xMin = double.MaxValue, xMax = double.MinValue;
        double yMin = double.MaxValue, yMax = double.MinValue;
        Accumulate(ViewModel.PreviewGroundAxis, ref xMin, ref xMax, ref yMin, ref yMax);
        foreach (var region in ViewModel.PreviewRegionsAxis)
            Accumulate(region, ref xMin, ref xMax, ref yMin, ref yMax);
        foreach (var joint in ViewModel.PreviewJointsAxis)
            Accumulate(joint, ref xMin, ref xMax, ref yMin, ref yMax);
        if (!(xMax > xMin) || !(yMax > yMin))
        {
            xMin = 0;
            xMax = 1;
            yMin = 0;
            yMax = 1;
        }

        const double padPx = 16;
        var availW = Math.Max(1, canvasW - 2 * padPx);
        var availH = Math.Max(1, canvasH - 2 * padPx);
        var s = Math.Min(availW / (xMax - xMin), availH / (yMax - yMin));
        var offsetX = padPx + (availW - s * (xMax - xMin)) * 0.5;
        var offsetY = padPx + (availH - s * (yMax - yMin)) * 0.5;

        if (ViewModel.PreviewGroundAxis.Length >= 2)
        {
            PreviewCanvas.Children.Add(new Polyline
            {
                Points = Project(ViewModel.PreviewGroundAxis, s, offsetX, offsetY, xMin, yMax),
                Stroke = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                StrokeThickness = 1.5
            });
        }

        foreach (var region in ViewModel.PreviewRegionsAxis)
        {
            if (region.Length < 2) continue;
            var pts = Project(region, s, offsetX, offsetY, xMin, yMax);
            if (region.Length >= 3)
                pts.Add(pts[0]);
            PreviewCanvas.Children.Add(new Polyline
            {
                Points = pts,
                Stroke = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                StrokeThickness = 1.1,
                StrokeDashArray = new DoubleCollection { 2, 2 }
            });
        }

        var jointColor = ViewModel.SelectedPropertySet?.Colour ?? Color.FromRgb(0xB4, 0x53, 0x09);
        var jointBrush = new SolidColorBrush(jointColor);
        if (jointBrush.CanFreeze) jointBrush.Freeze();
        foreach (var joint in ViewModel.PreviewJointsAxis)
        {
            if (joint.Length < 2) continue;
            PreviewCanvas.Children.Add(new Polyline
            {
                Points = Project(joint, s, offsetX, offsetY, xMin, yMax),
                Stroke = jointBrush,
                StrokeThickness = 2.2,
                StrokeDashArray = new DoubleCollection { 4, 2 }
            });
        }
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
        double xMin, double yMax)
    {
        var pts = new PointCollection(axisPts.Length);
        foreach (var p in axisPts)
            pts.Add(new Point(offsetX + (p.X - xMin) * scale, offsetY + (yMax - p.Y) * scale));
        return pts;
    }
}
