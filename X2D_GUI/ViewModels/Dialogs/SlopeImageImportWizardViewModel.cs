using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using GeotechSlopeApp.Models.Geometry;
using GeotechSlopeApp.Services.GeometryImport;

namespace GeotechSlopeApp.ViewModels.Dialogs;

/// <summary>Single-page georeference wizard for a slope image underlay.</summary>
public sealed class SlopeImageImportWizardViewModel : INotifyPropertyChanged, IDisposable
{
    private SlopeImageCalibrationMode _mode = SlopeImageCalibrationMode.TwoPoint;
    private int _calibrationClickStep;
    private Point? _pendingImagePoint1;
    private Point? _pendingImagePoint2;
    private string _sourcePath = "";
    private string _fileName = "(no image)";
    private BitmapSource? _bitmap;
    private int _imagePixelWidth;
    private int _imagePixelHeight;
    private string _point1WorldXText = "0";
    private string _point1WorldYText = "0";
    private string _point2WorldXText = "25";
    private string _point2WorldYText = "0";
    private string _boundsMinXText = "0";
    private string _boundsMinYText = "0";
    private string _boundsMaxXText = "25";
    private string _boundsMaxYText = "10";
    private double _opacityPercent = 65;
    private bool _showUnderlay = true;
    private string _statusText = "Browse for a PNG, JPG, BMP, or TIFF.";
    private string _validationSummary = "Select an image file to continue.";

    public SlopeImageImportWizardViewModel()
    {
        InitCommands();
    }

    public SlopeImageImportWizardViewModel(string sourcePath, BitmapSource bitmap, SlopeImageUnderlay? existing = null)
    {
        ApplyLoadedImage(sourcePath, bitmap, resetClicks: existing == null);
        if (existing != null)
        {
            IsEditingExisting = true;
            PrefillFrom(existing);
        }

        InitCommands();
        RebuildStatus();
        Validate();
    }

    public string SourcePath
    {
        get => _sourcePath;
        private set { _sourcePath = value ?? ""; OnPropertyChanged(); }
    }

    public string FileName
    {
        get => _fileName;
        private set { _fileName = value ?? "(no image)"; OnPropertyChanged(); }
    }

    public BitmapSource? Bitmap
    {
        get => _bitmap;
        private set
        {
            _bitmap = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasImage));
            OnPropertyChanged(nameof(ImageSizeText));
            OnPropertyChanged(nameof(CanImport));
        }
    }

    public int ImagePixelWidth
    {
        get => _imagePixelWidth;
        private set { _imagePixelWidth = value; OnPropertyChanged(); OnPropertyChanged(nameof(ImageSizeText)); }
    }

    public int ImagePixelHeight
    {
        get => _imagePixelHeight;
        private set { _imagePixelHeight = value; OnPropertyChanged(); OnPropertyChanged(nameof(ImageSizeText)); }
    }

    public bool HasImage => Bitmap != null && ImagePixelWidth > 0 && ImagePixelHeight > 0;

    public bool IsEditingExisting { get; }

    public bool ClearRequested { get; private set; }

    public string ConfirmButtonText => IsEditingExisting ? "Apply" : "Import";

    public string WindowTitle => IsEditingExisting ? "Slope Image" : "Import Slope Image";

    public SlopeImageUnderlay? ResultUnderlay { get; private set; }

    public SlopeImageCalibrationMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            _mode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsTwoPointMode));
            OnPropertyChanged(nameof(IsBoundsMode));
            RebuildStatus();
            Validate();
        }
    }

    public bool IsTwoPointMode
    {
        get => Mode == SlopeImageCalibrationMode.TwoPoint;
        set { if (value) Mode = SlopeImageCalibrationMode.TwoPoint; }
    }

    public bool IsBoundsMode
    {
        get => Mode == SlopeImageCalibrationMode.AxisAlignedBounds;
        set { if (value) Mode = SlopeImageCalibrationMode.AxisAlignedBounds; }
    }

    public Point? PendingImagePoint1
    {
        get => _pendingImagePoint1;
        private set { _pendingImagePoint1 = value; OnPropertyChanged(); OnPropertyChanged(nameof(Point1ImageText)); Validate(); }
    }

    public Point? PendingImagePoint2
    {
        get => _pendingImagePoint2;
        private set { _pendingImagePoint2 = value; OnPropertyChanged(); OnPropertyChanged(nameof(Point2ImageText)); Validate(); }
    }

    public string Point1ImageText => PendingImagePoint1 is { } p
        ? $"({p.X:0.#}, {p.Y:0.#}) px"
        : "(not set)";

    public string Point2ImageText => PendingImagePoint2 is { } p
        ? $"({p.X:0.#}, {p.Y:0.#}) px"
        : "(not set)";

    public string Point1WorldXText { get => _point1WorldXText; set { _point1WorldXText = value ?? ""; OnPropertyChanged(); Validate(); } }
    public string Point1WorldYText { get => _point1WorldYText; set { _point1WorldYText = value ?? ""; OnPropertyChanged(); Validate(); } }
    public string Point2WorldXText { get => _point2WorldXText; set { _point2WorldXText = value ?? ""; OnPropertyChanged(); Validate(); } }
    public string Point2WorldYText { get => _point2WorldYText; set { _point2WorldYText = value ?? ""; OnPropertyChanged(); Validate(); } }

    public string BoundsMinXText { get => _boundsMinXText; set { _boundsMinXText = value ?? ""; OnPropertyChanged(); Validate(); } }
    public string BoundsMinYText { get => _boundsMinYText; set { _boundsMinYText = value ?? ""; OnPropertyChanged(); Validate(); } }
    public string BoundsMaxXText { get => _boundsMaxXText; set { _boundsMaxXText = value ?? ""; OnPropertyChanged(); Validate(); } }
    public string BoundsMaxYText { get => _boundsMaxYText; set { _boundsMaxYText = value ?? ""; OnPropertyChanged(); Validate(); } }

    public double OpacityPercent
    {
        get => _opacityPercent;
        set
        {
            var v = Math.Clamp(value, 5, 100);
            if (Math.Abs(_opacityPercent - v) < 1e-9) return;
            _opacityPercent = v;
            OnPropertyChanged();
        }
    }

    public bool ShowUnderlay
    {
        get => _showUnderlay;
        set
        {
            if (_showUnderlay == value) return;
            _showUnderlay = value;
            OnPropertyChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value ?? ""; OnPropertyChanged(); }
    }

    public string ValidationSummary
    {
        get => _validationSummary;
        private set { _validationSummary = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(CanImport)); }
    }

    public bool CanImport => HasImage && string.IsNullOrEmpty(ValidationSummary);

    public string ImageSizeText => HasImage
        ? $"{ImagePixelWidth} × {ImagePixelHeight} px"
        : "No image selected";

    public ICommand BrowseCommand { get; private set; } = null!;
    public ICommand ClearUnderlayCommand { get; private set; } = null!;
    public ICommand ConfirmPoint1Command { get; private set; } = null!;
    public ICommand ConfirmPoint2Command { get; private set; } = null!;
    public ICommand ResetCalibrationClicksCommand { get; private set; } = null!;
    public ICommand UseTwoPointModeCommand { get; private set; } = null!;
    public ICommand UseBoundsModeCommand { get; private set; } = null!;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? CloseAccepted;

    public void HandlePreviewClick(Point imagePixel)
    {
        if (!HasImage || Mode != SlopeImageCalibrationMode.TwoPoint) return;

        if (_calibrationClickStep == 0 || PendingImagePoint1 == null)
        {
            PendingImagePoint1 = imagePixel;
            _calibrationClickStep = 1;
            StatusText = "Click the second calibration point on the image.";
            return;
        }

        PendingImagePoint2 = imagePixel;
        _calibrationClickStep = 2;
        StatusText = ConfirmHint;
        Validate();
    }

    public void ResetCalibrationClicks()
    {
        PendingImagePoint1 = null;
        PendingImagePoint2 = null;
        _calibrationClickStep = 0;
        RebuildStatus();
        Validate();
    }

    public bool TryBuildResult(out SlopeImageUnderlay underlay, out string error)
    {
        underlay = null!;
        error = "";
        if (!Validate(out error))
            return false;

        underlay = BuildUnderlay();
        ResultUnderlay = underlay;
        return true;
    }

    public void Dispose()
    {
        // Bitmap is frozen / owned by caller.
    }

    private void InitCommands()
    {
        BrowseCommand = new RelayCommand(Browse);
        ClearUnderlayCommand = new RelayCommand(RequestClear, () => IsEditingExisting);
        ConfirmPoint1Command = new RelayCommand(() => { /* values bind live */ }, () => IsTwoPointMode);
        ConfirmPoint2Command = new RelayCommand(() => { }, () => IsTwoPointMode);
        ResetCalibrationClicksCommand = new RelayCommand(ResetCalibrationClicks);
        UseTwoPointModeCommand = new RelayCommand(() => Mode = SlopeImageCalibrationMode.TwoPoint);
        UseBoundsModeCommand = new RelayCommand(() => Mode = SlopeImageCalibrationMode.AxisAlignedBounds);
    }

    private void Browse()
    {
        if (!SlopeImageImportService.TryPickImageFile(out var filePath) || string.IsNullOrWhiteSpace(filePath))
            return;

        try
        {
            ApplyLoadedImage(filePath, SlopeImageImportService.LoadBitmap(filePath), resetClicks: true);
            RebuildStatus();
            Validate();
        }
        catch (Exception ex)
        {
            ValidationSummary = ex.Message;
            MessageBox.Show(ex.Message, WindowTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RequestClear()
    {
        ClearRequested = true;
        CloseAccepted?.Invoke();
    }

    private void ApplyLoadedImage(string sourcePath, BitmapSource bitmap, bool resetClicks)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        SourcePath = sourcePath ?? "";
        FileName = string.IsNullOrWhiteSpace(sourcePath) ? "(image)" : Path.GetFileName(sourcePath);
        ImagePixelWidth = bitmap.PixelWidth;
        ImagePixelHeight = bitmap.PixelHeight;
        Bitmap = bitmap;
        if (resetClicks)
        {
            _pendingImagePoint1 = null;
            _pendingImagePoint2 = null;
            _calibrationClickStep = 0;
            OnPropertyChanged(nameof(PendingImagePoint1));
            OnPropertyChanged(nameof(PendingImagePoint2));
            OnPropertyChanged(nameof(Point1ImageText));
            OnPropertyChanged(nameof(Point2ImageText));
        }
    }

    private void PrefillFrom(SlopeImageUnderlay existing)
    {
        Mode = existing.Mode;
        OpacityPercent = Math.Clamp(existing.Opacity * 100.0, 5, 100);
        ShowUnderlay = existing.IsVisible;
        if (existing.Mode == SlopeImageCalibrationMode.TwoPoint)
        {
            PendingImagePoint1 = existing.Point1ImagePx;
            PendingImagePoint2 = existing.Point2ImagePx;
            _calibrationClickStep = 2;
            Point1WorldXText = existing.Point1World.X.ToString("G6", CultureInfo.InvariantCulture);
            Point1WorldYText = existing.Point1World.Y.ToString("G6", CultureInfo.InvariantCulture);
            Point2WorldXText = existing.Point2World.X.ToString("G6", CultureInfo.InvariantCulture);
            Point2WorldYText = existing.Point2World.Y.ToString("G6", CultureInfo.InvariantCulture);
        }
        else
        {
            BoundsMinXText = existing.WorldMinX.ToString("G6", CultureInfo.InvariantCulture);
            BoundsMinYText = existing.WorldMinY.ToString("G6", CultureInfo.InvariantCulture);
            BoundsMaxXText = existing.WorldMaxX.ToString("G6", CultureInfo.InvariantCulture);
            BoundsMaxYText = existing.WorldMaxY.ToString("G6", CultureInfo.InvariantCulture);
        }
    }

    private string ConfirmHint => IsEditingExisting
        ? "Enter world (X, Y) for both points, then Apply."
        : "Enter world (X, Y) for both points, then Import.";

    private void RebuildStatus()
    {
        if (!HasImage)
        {
            StatusText = "Browse for a PNG, JPG, BMP, or TIFF.";
            return;
        }

        StatusText = Mode == SlopeImageCalibrationMode.TwoPoint
            ? (PendingImagePoint2 != null
                ? ConfirmHint
                : PendingImagePoint1 != null
                    ? "Click the second calibration point on the image."
                    : "Click the first calibration point on the image.")
            : "Enter the world coordinates of the image lower-left and upper-right corners.";
    }

    private void Validate() => Validate(out _);

    private bool Validate(out string error)
    {
        error = "";
        if (!HasImage)
        {
            error = "Select an image file to continue.";
            ValidationSummary = error;
            return false;
        }

        try
        {
            var underlay = BuildUnderlay();
            if (!underlay.TryGetWorldBounds(out _, out _, out _, out _))
            {
                error = "Calibration does not produce a valid world extent.";
                ValidationSummary = error;
                return false;
            }

            // Round-trip check for two-point
            if (underlay.Mode == SlopeImageCalibrationMode.TwoPoint)
            {
                if (!underlay.TryImageToWorld(underlay.Point1ImagePx.X, underlay.Point1ImagePx.Y, out var w1)
                    || !underlay.TryImageToWorld(underlay.Point2ImagePx.X, underlay.Point2ImagePx.Y, out var w2))
                {
                    error = "Two-point calibration is invalid.";
                    ValidationSummary = error;
                    return false;
                }

                if (Distance(w1, underlay.Point1World) > 1e-3 || Distance(w2, underlay.Point2World) > 1e-3)
                {
                    error = "Calibration points do not map consistently. Choose distinct image points.";
                    ValidationSummary = error;
                    return false;
                }
            }

            ValidationSummary = "";
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            ValidationSummary = error;
            return false;
        }
    }

    private SlopeImageUnderlay BuildUnderlay()
    {
        var underlay = new SlopeImageUnderlay
        {
            SourcePath = SourcePath,
            ImagePixelWidth = ImagePixelWidth,
            ImagePixelHeight = ImagePixelHeight,
            Opacity = OpacityPercent / 100.0,
            IsVisible = ShowUnderlay,
            Mode = Mode
        };

        if (Mode == SlopeImageCalibrationMode.TwoPoint)
        {
            if (PendingImagePoint1 == null || PendingImagePoint2 == null)
                throw new InvalidOperationException("Click two calibration points on the image.");
            if (!TryParse(Point1WorldXText, out var x1) || !TryParse(Point1WorldYText, out var y1)
                || !TryParse(Point2WorldXText, out var x2) || !TryParse(Point2WorldYText, out var y2))
                throw new InvalidOperationException("World coordinates must be numbers.");
            if (Distance(PendingImagePoint1.Value, PendingImagePoint2.Value) < 1e-3)
                throw new InvalidOperationException("Calibration image points must be distinct.");
            if (Distance(new Point(x1, y1), new Point(x2, y2)) < 1e-9)
                throw new InvalidOperationException("Calibration world points must be distinct.");

            underlay.Point1ImagePx = PendingImagePoint1.Value;
            underlay.Point2ImagePx = PendingImagePoint2.Value;
            underlay.Point1World = new Point(x1, y1);
            underlay.Point2World = new Point(x2, y2);
        }
        else
        {
            if (!TryParse(BoundsMinXText, out var minX) || !TryParse(BoundsMinYText, out var minY)
                || !TryParse(BoundsMaxXText, out var maxX) || !TryParse(BoundsMaxYText, out var maxY))
                throw new InvalidOperationException("Bounds coordinates must be numbers.");
            if (Math.Abs(maxX - minX) < 1e-9 || Math.Abs(maxY - minY) < 1e-9)
                throw new InvalidOperationException("World bounds must span a non-zero width and height.");
            underlay.WorldMinX = Math.Min(minX, maxX);
            underlay.WorldMaxX = Math.Max(minX, maxX);
            underlay.WorldMinY = Math.Min(minY, maxY);
            underlay.WorldMaxY = Math.Max(minY, maxY);
        }

        return underlay;
    }

    private static bool TryParse(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
