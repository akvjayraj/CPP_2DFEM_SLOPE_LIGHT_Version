using System.IO;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Models.FieldStations;
using GeotechSlopeApp.Services;
using GeotechSlopeApp.Services.FieldStations;
using GeotechSlopeApp.ViewModels.Dialogs;
using GeotechSlopeApp.Views.Dialogs;
using Microsoft.Win32;

namespace GeotechSlopeApp.ViewModels;

public sealed record FieldStationMaterialChoice(int Id, string Name);

public partial class ProjectSettingsViewModel
{
    private FieldStation? _selectedFieldStation;
    private FieldUnitMaterialMapRow? _selectedFieldUnitMapRow;
    private string _fieldStationStatusText = "0 stations · 0 units";
    private bool _canCommitFieldStations;
    private bool _canCreateSoilBlock;
    private bool _fieldStationHooksAttached;
    private double _fieldStationSoilBlockMargin = 5;
    private double _fieldStationReferenceElevation;
    private bool _fieldStationsActive;
    private bool _plotBoreholes;
    private bool _extendSoilRegionsToBoundary;

    public FieldStationSet FieldStations { get; } = new();

    public double FieldStationSoilBlockMargin
    {
        get => _fieldStationSoilBlockMargin;
        set
        {
            if (Math.Abs(_fieldStationSoilBlockMargin - value) < 1e-12) return;
            _fieldStationSoilBlockMargin = value;
            OnPropertyChanged();
        }
    }

    public double FieldStationReferenceElevation
    {
        get => _fieldStationReferenceElevation;
        set
        {
            if (Math.Abs(_fieldStationReferenceElevation - value) < 1e-12) return;
            _fieldStationReferenceElevation = value;
            OnPropertyChanged();
            RefreshFieldStationCanvas();
        }
    }

    public bool FieldStationsActive
    {
        get => _fieldStationsActive;
        set
        {
            if (_fieldStationsActive == value) return;
            _fieldStationsActive = value;
            OnPropertyChanged();
            RefreshFieldStationUi();
        }
    }

    public bool PlotBoreholes
    {
        get => _plotBoreholes;
        set
        {
            if (_plotBoreholes == value) return;
            _plotBoreholes = value;
            OnPropertyChanged();
            RefreshFieldStationCanvas();
        }
    }

    public bool ExtendSoilRegionsToBoundary
    {
        get => _extendSoilRegionsToBoundary;
        set
        {
            if (_extendSoilRegionsToBoundary == value) return;
            _extendSoilRegionsToBoundary = value;
            OnPropertyChanged();
        }
    }

    public ICommand ImportFieldStationsCommand { get; private set; } = null!;
    public ICommand CommitFieldStationsCommand { get; private set; } = null!;
    public ICommand CreateMaterialForSelectedUnitCommand { get; private set; } = null!;
    public ICommand CreateSoilBlockCommand { get; private set; } = null!;
    public ICommand DrawNonHorizontalFieldStationLayerCommand { get; private set; } = null!;

    public ObservableCollection<FieldStationMaterialChoice> FieldStationMaterialChoices { get; } = new();

    public IReadOnlyList<System.Windows.Media.Color> FieldStationColourPalette => FieldStationUnitColours.Palette;

    public FieldStation? SelectedFieldStation
    {
        get => _selectedFieldStation;
        set
        {
            if (ReferenceEquals(_selectedFieldStation, value)) return;
            _selectedFieldStation = value;
            OnPropertyChanged();
            RefreshFieldStationCanvas();
        }
    }

    public FieldUnitMaterialMapRow? SelectedFieldUnitMapRow
    {
        get => _selectedFieldUnitMapRow;
        set
        {
            if (ReferenceEquals(_selectedFieldUnitMapRow, value)) return;
            _selectedFieldUnitMapRow = value;
            OnPropertyChanged();
        }
    }

    public string FieldStationCommitHint { get; private set; } =
        "Map every unit to a material before committing.";

    public string FieldStationStatusText
    {
        get => _fieldStationStatusText;
        private set
        {
            if (_fieldStationStatusText == value) return;
            _fieldStationStatusText = value;
            OnPropertyChanged();
        }
    }

    public bool CanCommitFieldStations
    {
        get => _canCommitFieldStations;
        private set
        {
            if (_canCommitFieldStations == value) return;
            _canCommitFieldStations = value;
            OnPropertyChanged();
            (CommitFieldStationsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public bool CanCreateSoilBlock
    {
        get => _canCreateSoilBlock;
        private set
        {
            if (_canCreateSoilBlock == value) return;
            _canCreateSoilBlock = value;
            OnPropertyChanged();
            (CreateSoilBlockCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DrawNonHorizontalFieldStationLayerCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    private void InitFieldStationCommands()
    {
        ImportFieldStationsCommand = new RelayCommand(ImportFieldStations);
        CommitFieldStationsCommand = new RelayCommand(CommitFieldStations, () => CanCommitFieldStations);
        CreateMaterialForSelectedUnitCommand = new RelayCommand(CreateMaterialForSelectedUnit);
        CreateSoilBlockCommand = new RelayCommand(CreateSoilBlockFromStations, () => CanCreateSoilBlock);
        DrawNonHorizontalFieldStationLayerCommand = new RelayCommand(DrawNonHorizontalFieldStationLayer, () => CanCreateSoilBlock);
        AttachFieldStationHooks();
        RebuildFieldStationMaterialChoices();
        RefreshFieldStationUi();
    }

    private void AttachFieldStationHooks()
    {
        if (_fieldStationHooksAttached) return;
        FieldStations.Stations.CollectionChanged += OnFieldStationsCollectionChanged;
        FieldStations.UnitMaterialMap.CollectionChanged += OnFieldUnitMapCollectionChanged;
        foreach (var row in FieldStations.UnitMaterialMap)
            row.PropertyChanged += OnFieldUnitMapRowPropertyChanged;
        _fieldStationHooksAttached = true;
    }

    private void RebindFieldStationHooks()
    {
        _fieldStationHooksAttached = false;
        AttachFieldStationHooks();
        RebuildFieldStationMaterialChoices();
        SelectedFieldStation = FieldStations.Stations.FirstOrDefault();
        RefreshFieldStationUi();
    }

    private void OnFieldStationsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RefreshFieldStationUi();

    private void OnFieldUnitMapCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (FieldUnitMaterialMapRow row in e.NewItems)
                row.PropertyChanged += OnFieldUnitMapRowPropertyChanged;
        }
        if (e.OldItems != null)
        {
            foreach (FieldUnitMaterialMapRow row in e.OldItems)
                row.PropertyChanged -= OnFieldUnitMapRowPropertyChanged;
        }
        RefreshFieldStationUi();
    }

    private void OnFieldUnitMapRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FieldUnitMaterialMapRow.Colour))
        {
            if (sender is FieldUnitMaterialMapRow row
                && row.MaterialId is int id
                && _materialPropertiesVM != null)
            {
                var material = _materialPropertiesVM.Materials.FirstOrDefault(m => m.Id == id);
                if (material != null)
                    material.Colour = row.Colour;
            }
            RefreshFieldStationCanvas();
            return;
        }

        if (e.PropertyName is nameof(FieldUnitMaterialMapRow.MaterialId) or nameof(FieldUnitMaterialMapRow.IsMapped))
        {
            RefreshFieldStationUi();
        }
    }

    private void ImportFieldStations()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Import Field Stations Evidence Pack",
            Filter = "Evidence Pack CSV (*.csv)|*.csv|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() != true)
            return;

        FieldStationCsvPreview preview;
        try
        {
            preview = FieldStationEvidencePackImporter.ReadPreview(dlg.FileName);
        }
        catch (IOException)
        {
            AppDialogs.Show(
                "Could not read the Evidence Pack because the file is open in another program (for example Excel or an editor). Close that program and import again.",
                "Import Evidence Pack", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        catch (Exception ex)
        {
            AppDialogs.Show(ex.Message, "Import Evidence Pack", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var wizard = new FieldStationImportWizardViewModel(dlg.FileName, preview);
        var window = new FieldStationImportWizardDialog(wizard)
        {
            Owner = Application.Current?.MainWindow
        };
        if (window.ShowDialog() != true || window.ResultMap == null)
            return;

        FieldStationImportResult imported;
        try
        {
            imported = FieldStationEvidencePackImporter.Import(dlg.FileName, window.ResultMap);
        }
        catch (IOException)
        {
            AppDialogs.Show(
                "Could not read the Evidence Pack because the file is open in another program (for example Excel or an editor). Close that program and import again.",
                "Import Evidence Pack", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!imported.Success)
        {
            AppDialogs.Show(imported.Message, "Import Evidence Pack", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        FieldStations.Stations.Clear();
        foreach (var station in imported.Stations)
            FieldStations.Stations.Add(station);
        FieldStations.RebuildUnitMapPreservingAssignments();
        FieldStationsActive = true;
        PlotBoreholes = true;
        if (_geometryDefinitionVM != null)
        {
            _geometryDefinitionVM.ShowBoreholesOnGeometryCanvas = true;
            _geometryDefinitionVM.ShowBoreholesOnMaterialCanvas = true;
        }
        SelectedFieldStation = FieldStations.Stations.FirstOrDefault();
        if (_geometryDefinitionVM is { GeometryVertexRows.Count: 0 })
            TryCreateSoilBlock(confirmReplace: false);
        RefreshFieldStationUi();
        AppDialogs.Show(imported.Message, "Import Evidence Pack", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CommitFieldStations()
    {
        if (_geometryDefinitionVM == null)
        {
            AppDialogs.Show("Geometry is not available.", "Field Stations", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!FieldStationsActive)
        {
            AppDialogs.Show("Turn on Field Stations active before committing.", "Field Stations", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = FieldStationCommitService.CommitHorizontal(
            FieldStations, _geometryDefinitionVM, _materialPropertiesVM, FieldStationReferenceElevation);
        RefreshFieldStationUi();
        AppDialogs.Show(
            result.Message,
            "Field Stations",
            MessageBoxButton.OK,
            result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void CreateMaterialForSelectedUnit()
    {
        if (_materialPropertiesVM == null)
        {
            AppDialogs.Show("Material library is not available.", "Field Stations",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var rows = FieldStations.UnitMaterialMap.Where(r => !r.IsMapped).ToList();
        if (SelectedFieldUnitMapRow is { IsMapped: false } selected
            && !rows.Contains(selected))
            rows.Insert(0, selected);

        if (rows.Count == 0)
        {
            AppDialogs.Show("Every unit already has a material. Pick a different material in the map if needed.",
                "Field Stations", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var nextId = _materialPropertiesVM.Materials.Count == 0
            ? 1
            : _materialPropertiesVM.Materials.Max(m => m.Id) + 1;
        foreach (var row in rows)
        {
            var existing = _materialPropertiesVM.Materials.FirstOrDefault(m =>
                string.Equals(m.Name, row.UnitCode, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                row.MaterialId = existing.Id;
                continue;
            }

            var material = new MaterialModel
            {
                Id = nextId++,
                Name = row.UnitCode,
                FailureCriterion = FailureCriterion.MohrCoulomb,
                MaterialType = MaterialType.Isotropic,
                Colour = row.Colour
            };
            _materialPropertiesVM.Materials.Add(material);
            row.MaterialId = material.Id;
        }

        RebuildFieldStationMaterialChoices();
        RefreshFieldStationUi();
    }

    private void CreateSoilBlockFromStations()
        => TryCreateSoilBlock(confirmReplace: true);

    private bool TryCreateSoilBlock(bool confirmReplace)
    {
        if (_geometryDefinitionVM == null)
        {
            AppDialogs.Show("Geometry is not available.", "Field Stations", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (FieldStations.Stations.Count == 0)
        {
            AppDialogs.Show("Import at least one field station first.", "Field Stations",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        if (!FieldStationSoilBlock.TryComputeBounds(
                FieldStations.Stations, FieldStationSoilBlockMargin, out var block,
                FieldStationReferenceElevation))
        {
            AppDialogs.Show("Could not compute a soil block from the stations.", "Field Stations",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (confirmReplace && _geometryDefinitionVM.GeometryVertexRows.Count > 0)
        {
            var answer = AppDialogs.Show(
                "Replace the current geometry with a closed rectangle around the boreholes? Horizontal layer cuts will be cleared. Drawn non-horizontal layers are kept.",
                "Create soil block",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
                return false;
        }

        _geometryDefinitionVM.CreateClosedSoilBlock(block.MinX, block.MinY, block.MaxX, block.MaxY);
        RefreshFieldStationCanvas();
        return true;
    }

    private void DrawNonHorizontalFieldStationLayer()
    {
        if (_geometryDefinitionVM == null)
        {
            AppDialogs.Show("Geometry is not available.", "Field Stations", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!_geometryDefinitionVM.HasClosedSoilPolygon
            && !TryCreateSoilBlock(confirmReplace: false))
            return;

        _geometryDefinitionVM.IsNonHorizontalLayerMode = true;
        _geometryDefinitionVM.StartNonHorizontalLayerDraw();
    }

    internal void RebuildFieldStationMaterialChoices()
    {
        FieldStationMaterialChoices.Clear();
        FieldStationMaterialChoices.Add(new FieldStationMaterialChoice(0, "(unmapped)"));
        if (_materialPropertiesVM == null) return;
        foreach (var m in _materialPropertiesVM.Materials.Where(m => m.Id > 0))
            FieldStationMaterialChoices.Add(new FieldStationMaterialChoice(m.Id, m.Name));
    }

    internal void RefreshFieldStationUi()
    {
        FieldStations.NotifyCounts();
        var stations = FieldStations.StationCount;
        var units = FieldStations.DistinctUnitCount;
        var unmapped = FieldStations.UnmappedUnitCount;
        var ready = FieldStationsActive && stations > 0 && units > 0 && unmapped == 0;
        CanCommitFieldStations = ready;
        CanCreateSoilBlock = stations > 0;
        var datum = Math.Abs(FieldStationReferenceElevation) > 1e-12
            ? $" · model Y = imported + {FieldStationReferenceElevation}"
            : "";
        FieldStationStatusText = ready
            ? $"{stations} stations · {units} units · Commit ready{datum}"
            : unmapped > 0
                ? $"{stations} stations · {units} units · {unmapped} unmapped — Create material or pick in Unit → Material{datum}"
                : $"{stations} stations · {units} units · not ready{datum}";
        FieldStationCommitHint = ready
            ? "Write unit contacts as horizontal layer cuts and assign materials (Layer 1 = bottom)."
            : unmapped > 0
                ? "Commit stays off until every unit is mapped to a material. Use Create material, or pick from Unit → Material."
                : "Import stations and turn on Field Stations active, then commit.";
        OnPropertyChanged(nameof(FieldStationCommitHint));
        RefreshFieldStationCanvas();
    }

    internal void RefreshFieldStationCanvas()
    {
        _geometryDefinitionVM?.SetFieldStationOverlaySource(
            FieldStations,
            SelectedFieldStation?.Id,
            visible: FieldStations.Stations.Count > 0,
            _materialPropertiesVM?.Materials,
            FieldStationReferenceElevation);
    }
}
