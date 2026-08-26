using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using GeotechSlopeApp.Services.FieldStations;

namespace GeotechSlopeApp.ViewModels.Dialogs;

/// <summary>One-page Evidence Pack CSV column map (Station, X, Top/Bottom or Depth, Unit).</summary>
public sealed class FieldStationImportWizardViewModel : INotifyPropertyChanged
{
    private string _filePath = "";
    private string _fileName = "";
    private FieldStationColumnChoice? _stationColumn;
    private FieldStationColumnChoice? _xColumn;
    private FieldStationColumnChoice? _unitColumn;
    private FieldStationColumnChoice? _topYColumn;
    private FieldStationColumnChoice? _bottomYColumn;
    private FieldStationColumnChoice? _collarYColumn;
    private FieldStationColumnChoice? _depthColumn;
    private FieldStationColumnChoice? _noteColumn;
    private FieldStationColumnChoice? _labelColumn;
    private bool _useDepth;
    private string _statusText = "";

    public FieldStationImportWizardViewModel(string filePath, FieldStationCsvPreview preview)
    {
        FilePath = filePath;
        FileName = System.IO.Path.GetFileName(filePath);
        Preview = preview;
        NoneChoice = new FieldStationColumnChoice(-1, "(none)");
        ColumnChoices = new ObservableCollection<FieldStationColumnChoice> { NoneChoice };
        for (var i = 0; i < preview.Headers.Count; i++)
            ColumnChoices.Add(new FieldStationColumnChoice(i, preview.Headers[i]));

        var auto = FieldStationEvidencePackImporter.TryAutoMap(preview.Headers);
        StationColumn = Choice(auto.StationId);
        XColumn = Choice(auto.X);
        UnitColumn = Choice(auto.UnitCode);
        TopYColumn = Choice(auto.TopY);
        BottomYColumn = Choice(auto.BottomY);
        CollarYColumn = Choice(auto.CollarY);
        DepthColumn = Choice(auto.Depth);
        NoteColumn = Choice(auto.Note);
        LabelColumn = Choice(auto.Label);
        UseDepth = auto.UsesDepth && auto.TopY == null;
        StatusText = $"{preview.DataRowCount} data row(s). Map columns, then Import.";
        PreviewLines = preview.PreviewRows
            .Select(r => string.Join("  |  ", r))
            .ToList();
    }

    public IReadOnlyList<string> PreviewLines { get; }

    public string FilePath { get => _filePath; private set => Set(ref _filePath, value); }
    public string FileName { get => _fileName; private set => Set(ref _fileName, value); }
    public FieldStationCsvPreview Preview { get; }
    public FieldStationColumnChoice NoneChoice { get; }
    public ObservableCollection<FieldStationColumnChoice> ColumnChoices { get; }

    public FieldStationColumnChoice? StationColumn { get => _stationColumn; set => Set(ref _stationColumn, value); }
    public FieldStationColumnChoice? XColumn { get => _xColumn; set => Set(ref _xColumn, value); }
    public FieldStationColumnChoice? UnitColumn { get => _unitColumn; set => Set(ref _unitColumn, value); }
    public FieldStationColumnChoice? TopYColumn { get => _topYColumn; set => Set(ref _topYColumn, value); }
    public FieldStationColumnChoice? BottomYColumn { get => _bottomYColumn; set => Set(ref _bottomYColumn, value); }
    public FieldStationColumnChoice? CollarYColumn { get => _collarYColumn; set => Set(ref _collarYColumn, value); }
    public FieldStationColumnChoice? DepthColumn { get => _depthColumn; set => Set(ref _depthColumn, value); }
    public FieldStationColumnChoice? NoteColumn { get => _noteColumn; set => Set(ref _noteColumn, value); }
    public FieldStationColumnChoice? LabelColumn { get => _labelColumn; set => Set(ref _labelColumn, value); }

    public bool UseDepth
    {
        get => _useDepth;
        set
        {
            if (!Set(ref _useDepth, value)) return;
            OnPropertyChanged(nameof(UseElevations));
        }
    }

    public bool UseElevations
    {
        get => !_useDepth;
        set
        {
            if (value == !_useDepth) return;
            UseDepth = !value;
        }
    }

    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    public bool TryBuildMap(out FieldStationColumnMap map, out string error)
    {
        map = new FieldStationColumnMap();
        error = "";
        if (StationColumn == null || StationColumn.Index < 0)
        {
            error = "Select a Station Id column.";
            return false;
        }
        if (XColumn == null || XColumn.Index < 0)
        {
            error = "Select an X column.";
            return false;
        }
        if (UnitColumn == null || UnitColumn.Index < 0)
        {
            error = "Select a Unit code column.";
            return false;
        }

        map.StationId = StationColumn.Index;
        map.X = XColumn.Index;
        map.UnitCode = UnitColumn.Index;
        map.Note = Optional(NoteColumn);
        map.Label = Optional(LabelColumn);

        if (UseDepth)
        {
            if (CollarYColumn == null || CollarYColumn.Index < 0)
            {
                error = "Depth import needs a Collar Y column.";
                return false;
            }
            if (DepthColumn == null || DepthColumn.Index < 0)
            {
                error = "Depth import needs a Depth column.";
                return false;
            }
            map.CollarY = CollarYColumn.Index;
            map.Depth = DepthColumn.Index;
        }
        else
        {
            if (TopYColumn == null || TopYColumn.Index < 0 || BottomYColumn == null || BottomYColumn.Index < 0)
            {
                error = "Elevation import needs Top Y and Bottom Y columns.";
                return false;
            }
            map.TopY = TopYColumn.Index;
            map.BottomY = BottomYColumn.Index;
            map.CollarY = Optional(CollarYColumn);
        }

        return true;
    }

    private FieldStationColumnChoice Choice(int? index)
    {
        if (index is int i && i >= 0)
            return ColumnChoices.FirstOrDefault(c => c.Index == i) ?? NoneChoice;
        return NoneChoice;
    }

    private static int? Optional(FieldStationColumnChoice? choice) =>
        choice is { Index: >= 0 } ? choice.Index : null;

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record FieldStationColumnChoice(int Index, string Name)
{
    public override string ToString() => Name;
}
