using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using GeotechSlopeApp.Models;
using Microsoft.Win32;

namespace GeotechSlopeApp.ViewModels.Dialogs;

public sealed class WaterPressureGridTypeOption
{
    public WaterPressureGridTypeOption(WaterPressureGridType type)
    {
        Type = type;
        DisplayName = type.GetDisplayName();
    }

    public WaterPressureGridType Type { get; }
    public string DisplayName { get; }
}

/// <summary>Modal editor for named Static water-pressure grids (deep-copy; OK commits).</summary>
public sealed class WaterPressureGridDialogViewModel : INotifyPropertyChanged
{
    private WaterPressureGrid? _selectedGrid;
    private WaterPressureGridPoint? _selectedPoint;
    private WaterPressureGridTypeOption? _selectedGridTypeOption;

    public WaterPressureGridDialogViewModel(
        IEnumerable<WaterPressureGrid> source,
        IReadOnlyList<WaterPressureGridType> allowedTypes,
        WaterPressureGridType preferredType,
        string? lengthUnitLabel = null)
    {
        if (allowedTypes.Count == 0)
            throw new ArgumentException("At least one grid type must be allowed.", nameof(allowedTypes));

        LengthUnitLabel = string.IsNullOrWhiteSpace(lengthUnitLabel)
            ? ProjectUnitSystem.LengthLabel(ProjectUnitSystem.Si)
            : lengthUnitLabel.Trim();

        AllowedGridTypes = new ObservableCollection<WaterPressureGridTypeOption>(
            allowedTypes.Select(t => new WaterPressureGridTypeOption(t)));

        foreach (var g in source)
        {
            if (!allowedTypes.Contains(g.GridType))
                continue;
            Grids.Add(g.Clone());
        }

        if (Grids.Count == 0)
        {
            var seedType = allowedTypes.Contains(preferredType) ? preferredType : allowedTypes[0];
            Grids.Add(CreateNewGrid("Default Grid", seedType));
        }

        AddGridCommand = new RelayCommand(AddGrid);
        DeleteGridCommand = new RelayCommand(DeleteGrid, () => SelectedGrid != null && Grids.Count > 1);
        AddPointCommand = new RelayCommand(AddPoint, () => SelectedGrid != null);
        RemovePointCommand = new RelayCommand(RemovePoint, () => SelectedGrid != null && SelectedPoint != null);
        ImportCommand = new RelayCommand(ImportCsv, () => SelectedGrid != null);
        ExportCommand = new RelayCommand(ExportCsv, () => SelectedGrid != null);

        SelectedGrid = Grids.FirstOrDefault(g => g.GridType == preferredType) ?? Grids[0];
    }

    public ObservableCollection<WaterPressureGrid> Grids { get; } = new();
    public ObservableCollection<WaterPressureGridTypeOption> AllowedGridTypes { get; }

    public WaterPressureGrid? SelectedGrid
    {
        get => _selectedGrid;
        set
        {
            if (ReferenceEquals(_selectedGrid, value)) return;
            if (_selectedGrid != null)
                _selectedGrid.PropertyChanged -= OnSelectedGridPropertyChanged;
            _selectedGrid = value;
            if (_selectedGrid != null)
                _selectedGrid.PropertyChanged += OnSelectedGridPropertyChanged;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ValueColumnHeader));
            SyncSelectedGridTypeOption();
            SelectedPoint = null;
            (DeleteGridCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (AddPointCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RemovePointCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ImportCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ExportCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public WaterPressureGridTypeOption? SelectedGridTypeOption
    {
        get => _selectedGridTypeOption;
        set
        {
            if (ReferenceEquals(_selectedGridTypeOption, value)) return;
            _selectedGridTypeOption = value;
            OnPropertyChanged();
            if (SelectedGrid != null && value != null && SelectedGrid.GridType != value.Type)
                SelectedGrid.GridType = value.Type;
        }
    }

    public WaterPressureGridPoint? SelectedPoint
    {
        get => _selectedPoint;
        set
        {
            _selectedPoint = value;
            OnPropertyChanged();
            (RemovePointCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public string ValueColumnHeader =>
        SelectedGrid?.ValueColumnHeader ?? WaterPressureGridType.TotalHead.GetValueColumnHeader();

    /// <summary>Project Settings length unit (m or ft) for coordinate column headers.</summary>
    public string LengthUnitLabel { get; }
    public string PointXHeader => $"X ({LengthUnitLabel})";
    public string PointYHeader => $"Y ({LengthUnitLabel})";

    public ICommand AddGridCommand { get; }
    public ICommand DeleteGridCommand { get; }
    public ICommand AddPointCommand { get; }
    public ICommand RemovePointCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand ExportCommand { get; }

    private void OnSelectedGridPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WaterPressureGrid.GridType)
            or nameof(WaterPressureGrid.ValueColumnHeader))
        {
            OnPropertyChanged(nameof(ValueColumnHeader));
            SyncSelectedGridTypeOption();
        }
    }

    private void SyncSelectedGridTypeOption()
    {
        if (SelectedGrid == null)
        {
            _selectedGridTypeOption = null;
            OnPropertyChanged(nameof(SelectedGridTypeOption));
            return;
        }

        var match = AllowedGridTypes.FirstOrDefault(o => o.Type == SelectedGrid.GridType);
        if (!ReferenceEquals(_selectedGridTypeOption, match))
        {
            _selectedGridTypeOption = match;
            OnPropertyChanged(nameof(SelectedGridTypeOption));
        }
    }

    private void AddGrid()
    {
        var type = SelectedGrid?.GridType ?? AllowedGridTypes[0].Type;
        var name = NextGridName();
        var grid = CreateNewGrid(name, type);
        Grids.Add(grid);
        SelectedGrid = grid;
        (DeleteGridCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void DeleteGrid()
    {
        if (SelectedGrid == null || Grids.Count <= 1)
            return;
        var idx = Grids.IndexOf(SelectedGrid);
        Grids.Remove(SelectedGrid);
        SelectedGrid = Grids[Math.Clamp(idx, 0, Grids.Count - 1)];
        (DeleteGridCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void AddPoint()
    {
        if (SelectedGrid == null) return;
        SelectedGrid.Points.Add(new WaterPressureGridPoint(SelectedGrid.Points.Count + 1));
        Renumber(SelectedGrid.Points);
        SelectedPoint = SelectedGrid.Points[^1];
    }

    private void RemovePoint()
    {
        if (SelectedGrid == null || SelectedPoint == null) return;
        SelectedGrid.Points.Remove(SelectedPoint);
        SelectedPoint = null;
        Renumber(SelectedGrid.Points);
        if (SelectedGrid.Points.Count == 0)
            EnsureStarterRows(SelectedGrid);
    }

    private void ImportCsv()
    {
        if (SelectedGrid == null) return;
        var dlg = new OpenFileDialog
        {
            Title = "Import water pressure grid points",
            Filter = "CSV / text files (*.csv;*.txt)|*.csv;*.txt|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            var lines = File.ReadAllLines(dlg.FileName);
            var parsed = ParsePoints(lines);
            SelectedGrid.Points.Clear();
            for (var i = 0; i < parsed.Count; i++)
            {
                var p = parsed[i];
                SelectedGrid.Points.Add(new WaterPressureGridPoint(i + 1, p.X, p.Y, p.Value));
            }
            if (SelectedGrid.Points.Count == 0)
                EnsureStarterRows(SelectedGrid);
            SelectedPoint = null;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not import points:\n{ex.Message}",
                "Import Water Pressure Grid",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ExportCsv()
    {
        if (SelectedGrid == null) return;
        var dlg = new SaveFileDialog
        {
            Title = "Export water pressure grid points",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = SanitizeFileName(SelectedGrid.Name) + ".csv",
            AddExtension = true,
            DefaultExt = ".csv"
        };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("X,Y,Value");
            foreach (var p in SelectedGrid.Points)
            {
                if (Math.Abs(p.X) < 1e-15 && Math.Abs(p.Y) < 1e-15 && Math.Abs(p.Value) < 1e-15)
                    continue;
                sb.Append(p.X.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(p.Y.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.AppendLine(p.Value.ToString(CultureInfo.InvariantCulture));
            }
            File.WriteAllText(dlg.FileName, sb.ToString());
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not export points:\n{ex.Message}",
                "Export Water Pressure Grid",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private string NextGridName()
    {
        var n = 1;
        while (Grids.Any(g => string.Equals(g.Name, $"Grid {n}", StringComparison.OrdinalIgnoreCase)))
            n++;
        return $"Grid {n}";
    }

    private static WaterPressureGrid CreateNewGrid(string name, WaterPressureGridType type)
    {
        var grid = new WaterPressureGrid(name, type);
        EnsureStarterRows(grid);
        return grid;
    }

    private static void EnsureStarterRows(WaterPressureGrid grid)
    {
        if (grid.Points.Count > 0) return;
        for (var i = 1; i <= 8; i++)
            grid.Points.Add(new WaterPressureGridPoint(i));
    }

    private static void Renumber(ObservableCollection<WaterPressureGridPoint> points)
    {
        for (var i = 0; i < points.Count; i++)
            points[i].Index = i + 1;
    }

    private static List<(double X, double Y, double Value)> ParsePoints(IEnumerable<string> lines)
    {
        var result = new List<(double X, double Y, double Value)>();
        var first = true;
        foreach (var raw in lines)
        {
            var line = raw?.Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split(new[] { ',', ';', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
                continue;

            if (first)
            {
                first = false;
                if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                    && !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.CurrentCulture, out _))
                {
                    // Header row (e.g. X,Y,Value)
                    continue;
                }
            }

            if (!TryParseDouble(parts[0], out var x)
                || !TryParseDouble(parts[1], out var y)
                || !TryParseDouble(parts[2], out var value))
            {
                continue;
            }

            result.Add((x, y, value));
        }

        return result;
    }

    private static bool TryParseDouble(string text, out double value)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
           || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = (name ?? "grid").Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var cleaned = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "grid" : cleaned;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
