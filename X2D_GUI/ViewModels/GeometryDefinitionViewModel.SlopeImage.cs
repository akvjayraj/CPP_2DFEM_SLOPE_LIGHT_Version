using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GeotechSlopeApp.Models.Geometry;
using GeotechSlopeApp.Services;
using GeotechSlopeApp.Services.GeometryImport;
using GeotechSlopeApp.ViewModels.Dialogs;
using GeotechSlopeApp.Views.Dialogs;

namespace GeotechSlopeApp.ViewModels;

public partial class GeometryDefinitionViewModel
{
    private SlopeImageUnderlay? _slopeImageUnderlay;
    private ImageSource? _slopeImageBitmap;
    private Matrix _slopeImageCanvasTransform = Matrix.Identity;
    private double _slopeImageOpacityPercent = 65;

    /// <summary>Serializable underlay state (path + calibration). Null when none.</summary>
    public SlopeImageUnderlay? SlopeImageUnderlay
    {
        get => _slopeImageUnderlay;
        private set
        {
            if (ReferenceEquals(_slopeImageUnderlay, value)) return;
            _slopeImageUnderlay = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSlopeImageUnderlay));
            OnPropertyChanged(nameof(ShowSlopeImageUnderlay));
            OnPropertyChanged(nameof(SlopeImageIsVisible));
            OnPropertyChanged(nameof(SlopeImagePixelWidth));
            OnPropertyChanged(nameof(SlopeImagePixelHeight));
            RefreshSlopeImageCanvasPlacement();
        }
    }

    public bool HasSlopeImageUnderlay => _slopeImageUnderlay != null && _slopeImageBitmap != null;

    /// <summary>True when the underlay should paint on the canvas.</summary>
    public bool ShowSlopeImageUnderlay => HasSlopeImageUnderlay && SlopeImageIsVisible;

    public int SlopeImagePixelWidth => _slopeImageUnderlay?.ImagePixelWidth ?? 0;
    public int SlopeImagePixelHeight => _slopeImageUnderlay?.ImagePixelHeight ?? 0;

    public ImageSource? SlopeImageBitmap
    {
        get => _slopeImageBitmap;
        private set
        {
            if (ReferenceEquals(_slopeImageBitmap, value)) return;
            _slopeImageBitmap = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSlopeImageUnderlay));
            OnPropertyChanged(nameof(ShowSlopeImageUnderlay));
        }
    }

    public Matrix SlopeImageCanvasTransform
    {
        get => _slopeImageCanvasTransform;
        private set
        {
            if (_slopeImageCanvasTransform.Equals(value)) return;
            _slopeImageCanvasTransform = value;
            OnPropertyChanged();
        }
    }

    public bool SlopeImageIsVisible
    {
        get => _slopeImageUnderlay?.IsVisible ?? false;
        set
        {
            if (_slopeImageUnderlay == null) return;
            if (_slopeImageUnderlay.IsVisible == value) return;
            _slopeImageUnderlay.IsVisible = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSlopeImageUnderlay));
            OnPropertyChanged(nameof(ShowSlopeImageUnderlay));
        }
    }

    /// <summary>Opacity as 0–100 for UI binding.</summary>
    public double SlopeImageOpacityPercent
    {
        get => _slopeImageOpacityPercent;
        set
        {
            var clamped = Math.Clamp(value, 5, 100);
            if (Math.Abs(_slopeImageOpacityPercent - clamped) < 1e-9) return;
            _slopeImageOpacityPercent = clamped;
            if (_slopeImageUnderlay != null)
                _slopeImageUnderlay.Opacity = clamped / 100.0;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SlopeImageOpacity));
        }
    }

    public double SlopeImageOpacity => (_slopeImageUnderlay?.Opacity ?? _slopeImageOpacityPercent / 100.0);

    public ICommand ImportSlopeImageCommand { get; private set; } = null!;
    public ICommand RecalibrateSlopeImageCommand { get; private set; } = null!;
    public ICommand ClearSlopeImageCommand { get; private set; } = null!;
    public ICommand ToggleSlopeImageVisibilityCommand { get; private set; } = null!;

    private void InitializeSlopeImageCommands()
    {
        ImportSlopeImageCommand = new RelayCommand(ImportSlopeImage);
        RecalibrateSlopeImageCommand = new RelayCommand(RecalibrateSlopeImage, () => HasSlopeImageUnderlay);
        ClearSlopeImageCommand = new RelayCommand(ClearSlopeImageUnderlay, () => HasSlopeImageUnderlay);
        ToggleSlopeImageVisibilityCommand = new RelayCommand(
            () => SlopeImageIsVisible = !SlopeImageIsVisible,
            () => HasSlopeImageUnderlay);
    }

    private void ImportSlopeImage()
    {
        try
        {
            SlopeImageImportWizardViewModel wizardVm;
            if (HasSlopeImageUnderlay && _slopeImageUnderlay != null)
            {
                var path = _slopeImageUnderlay.SourcePath;
                BitmapSource bitmap = _slopeImageBitmap as BitmapSource
                    ?? throw new InvalidOperationException("Slope image bitmap is not a BitmapSource.");
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    bitmap = SlopeImageImportService.LoadBitmap(path);
                wizardVm = new SlopeImageImportWizardViewModel(path, bitmap, _slopeImageUnderlay);
            }
            else
            {
                wizardVm = new SlopeImageImportWizardViewModel();
            }

            using (wizardVm)
            {
                var dialog = new SlopeImageImportWizardDialog(wizardVm)
                {
                    Owner = Application.Current?.MainWindow
                };
                if (dialog.ShowDialog() != true)
                    return;

                if (wizardVm.ClearRequested)
                {
                    ClearSlopeImageUnderlay();
                    return;
                }

                if (wizardVm.ResultUnderlay == null || wizardVm.Bitmap == null)
                    return;

                RecordUndo(HasSlopeImageUnderlay ? "Recalibrate slope image underlay" : "Import slope image underlay");
                ApplySlopeImageUnderlay(wizardVm.ResultUnderlay, wizardVm.Bitmap, expandAxis: true);
            }
        }
        catch (Exception ex)
        {
            AppDialogs.Show(
                $"Could not import slope image:{Environment.NewLine}{ex.Message}",
                "Import Slope Image",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void RecalibrateSlopeImage() => ImportSlopeImage();

    public void ClearSlopeImageUnderlay()
    {
        if (_slopeImageUnderlay == null && _slopeImageBitmap == null) return;
        RecordUndo("Clear slope image underlay");
        ClearSlopeImageUnderlaySilent();
    }

    /// <summary>Clears underlay without recording undo (project load / blank document).</summary>
    public void ClearSlopeImageUnderlaySilent()
    {
        SlopeImageBitmap = null;
        SlopeImageUnderlay = null;
        SlopeImageCanvasTransform = Matrix.Identity;
        (RecalibrateSlopeImageCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ClearSlopeImageCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ToggleSlopeImageVisibilityCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    /// <summary>Applies underlay from import wizard or project load (no undo).</summary>
    public void ApplySlopeImageUnderlay(SlopeImageUnderlay underlay, ImageSource? bitmap, bool expandAxis)
    {
        if (underlay == null) throw new ArgumentNullException(nameof(underlay));

        bitmap ??= (!string.IsNullOrWhiteSpace(underlay.SourcePath) && File.Exists(underlay.SourcePath)
            ? SlopeImageImportService.LoadBitmap(underlay.SourcePath)
            : null);

        if (bitmap == null)
            throw new InvalidOperationException("Slope image bitmap could not be loaded.");

        if (bitmap is BitmapSource bs)
        {
            underlay.ImagePixelWidth = bs.PixelWidth;
            underlay.ImagePixelHeight = bs.PixelHeight;
        }

        _slopeImageOpacityPercent = Math.Clamp(underlay.Opacity * 100.0, 5, 100);
        SlopeImageBitmap = bitmap;
        SlopeImageUnderlay = underlay.Clone();
        SlopeImageUnderlay.SourcePath = underlay.SourcePath;
        OnPropertyChanged(nameof(SlopeImageOpacityPercent));
        OnPropertyChanged(nameof(SlopeImageOpacity));
        OnPropertyChanged(nameof(SlopeImageIsVisible));

        if (expandAxis && underlay.TryGetWorldBounds(out var minX, out var minY, out var maxX, out var maxY))
        {
            FitAxisBoundsToWorldExtents(minX, maxX, minY, maxY);
            RefreshGeometryPath();
            _projectSettings?.PullAxisFromGeometry();
        }

        RefreshSlopeImageCanvasPlacement();
        (RecalibrateSlopeImageCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ClearSlopeImageCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ToggleSlopeImageVisibilityCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    /// <summary>Called after axis changes so the underlay stays georeferenced.</summary>
    public void RefreshSlopeImageCanvasPlacement()
    {
        if (_slopeImageUnderlay == null)
        {
            SlopeImageCanvasTransform = Matrix.Identity;
            return;
        }

        if (_slopeImageUnderlay.TryBuildCanvasMatrix(ConvertAxisToCanvas, out var matrix))
            SlopeImageCanvasTransform = matrix;
        else
            SlopeImageCanvasTransform = Matrix.Identity;

        OnPropertyChanged(nameof(SlopeImageOpacity));
        OnPropertyChanged(nameof(HasSlopeImageUnderlay));
    }

    private void CopySlopeImageFrom(GeometryDefinitionViewModel source)
    {
        if (source._slopeImageUnderlay == null || source._slopeImageBitmap == null)
        {
            SlopeImageBitmap = null;
            SlopeImageUnderlay = null;
            return;
        }

        ApplySlopeImageUnderlay(source._slopeImageUnderlay.Clone(), source._slopeImageBitmap, expandAxis: false);
    }
}
