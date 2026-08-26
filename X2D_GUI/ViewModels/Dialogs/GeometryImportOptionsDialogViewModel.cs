using System.ComponentModel;
using System.Runtime.CompilerServices;
using GeotechSlopeApp.Services.GeometryImport;

namespace GeotechSlopeApp.ViewModels.Dialogs;

public sealed class GeometryImportOptionsDialogViewModel : INotifyPropertyChanged
{
    private bool _importPoints = true;
    private bool _importLines = true;
    private bool _importSurfaces = true;
    private bool _replaceExisting = true;
    private string _mergeToleranceText = "0.0001";
    private string _arcSegmentCountText = "16";

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool ImportPoints
    {
        get => _importPoints;
        set
        {
            if (_importPoints == value) return;
            _importPoints = value;
            NotifyImportTypeChanged();
        }
    }

    public bool ImportLines
    {
        get => _importLines;
        set
        {
            if (_importLines == value) return;
            _importLines = value;
            NotifyImportTypeChanged();
        }
    }

    public bool ImportSurfaces
    {
        get => _importSurfaces;
        set
        {
            if (_importSurfaces == value) return;
            _importSurfaces = value;
            NotifyImportTypeChanged();
        }
    }

    public bool ReplaceExisting
    {
        get => _replaceExisting;
        set
        {
            if (_replaceExisting == value) return;
            _replaceExisting = value;
            OnPropertyChanged();
        }
    }

    public string MergeToleranceText
    {
        get => _mergeToleranceText;
        set
        {
            if (_mergeToleranceText == value) return;
            _mergeToleranceText = value ?? "";
            OnPropertyChanged();
        }
    }

    public string ArcSegmentCountText
    {
        get => _arcSegmentCountText;
        set
        {
            if (_arcSegmentCountText == value) return;
            _arcSegmentCountText = value ?? "";
            OnPropertyChanged();
        }
    }

    public bool HasAnyImportType => ImportPoints || ImportLines || ImportSurfaces;

    public GeometryImportOptions ToOptions()
    {
        var merge = double.TryParse(MergeToleranceText, out var tol) ? tol : 1e-4;
        var arcSeg = int.TryParse(ArcSegmentCountText, out var n) ? Math.Max(4, n) : 16;
        return new GeometryImportOptions
        {
            ImportPoints = ImportPoints,
            ImportLines = ImportLines,
            ImportSurfaces = ImportSurfaces,
            ReplaceExisting = ReplaceExisting,
            MergeTolerance = merge,
            ArcSegmentCount = arcSeg
        };
    }

    private void NotifyImportTypeChanged()
    {
        OnPropertyChanged(nameof(ImportPoints));
        OnPropertyChanged(nameof(ImportLines));
        OnPropertyChanged(nameof(ImportSurfaces));
        OnPropertyChanged(nameof(HasAnyImportType));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
