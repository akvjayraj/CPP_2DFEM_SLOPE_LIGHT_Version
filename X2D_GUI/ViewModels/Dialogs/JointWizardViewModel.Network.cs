using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using GeotechSlopeApp.Models.Geometry;
using GeotechSlopeApp.Services.Geometry;

namespace GeotechSlopeApp.ViewModels.Dialogs;

public sealed partial class JointWizardViewModel
{
    private FeJointNetworkDefinition? _selectedNetwork;
    private bool _suppressNetworkPreview;

    public ObservableCollection<FeJointNetworkDefinition> JointNetworks => _geometry.JointNetworks;
    public ObservableCollection<JointNetworkRegionChoice> NetworkRegions { get; } = new();

    public IReadOnlyList<FeJointNetworkModelChoice> AvailableJointModels { get; } =
    [
        new(FeJointNetworkModel.ParallelDeterministic, "Parallel Deterministic"),
        new(FeJointNetworkModel.ParallelStatistical, "Parallel Statistical"),
        new(FeJointNetworkModel.CrossJointed, "Cross Jointed"),
        new(FeJointNetworkModel.Baecher, "Baecher"),
        new(FeJointNetworkModel.Veneziano, "Veneziano"),
        new(FeJointNetworkModel.Voronoi, "Voronoi")
    ];

    public IReadOnlyList<FeJointDistributionKindChoice> AvailableDistributions { get; } =
    [
        new(FeJointDistributionKind.Constant, "Constant"),
        new(FeJointDistributionKind.Uniform, "Uniform"),
        new(FeJointDistributionKind.Normal, "Normal"),
        new(FeJointDistributionKind.Lognormal, "Lognormal"),
        new(FeJointDistributionKind.NegativeExponential, "Negative Exponential")
    ];

    public IReadOnlyList<FeJointVoronoiRegularityChoice> AvailableVoronoiRegularities { get; } =
    [
        new(FeJointVoronoiRegularity.Irregular, "Irregular"),
        new(FeJointVoronoiRegularity.MediumRegular, "Medium Regular"),
        new(FeJointVoronoiRegularity.AlmostRegular, "Almost Regular"),
        new(FeJointVoronoiRegularity.RegularHexagon, "Regular Hexagon")
    ];

    public IReadOnlyList<FeJointVoronoiDensityMethodChoice> AvailableVoronoiDensityMethods { get; } =
    [
        new(FeJointVoronoiDensityMethod.Density, "Density (cells / area)"),
        new(FeJointVoronoiDensityMethod.SiteCount, "Site count"),
        new(FeJointVoronoiDensityMethod.AverageJointLength, "Average joint length")
    ];

    public ICommand AddNetworkCommand { get; private set; } = null!;
    public ICommand DeleteNetworkCommand { get; private set; } = null!;
    public ICommand RandomizeNetworkCommand { get; private set; } = null!;

    public FeJointNetworkDefinition? SelectedNetwork
    {
        get => _selectedNetwork;
        set
        {
            if (ReferenceEquals(_selectedNetwork, value)) return;
            if (_selectedNetwork != null)
                _selectedNetwork.PropertyChanged -= OnSelectedNetworkChanged;
            _selectedNetwork = value;
            if (_selectedNetwork != null)
                _selectedNetwork.PropertyChanged += OnSelectedNetworkChanged;
            OnPropertyChanged();
            SyncRegionChecksFromNetwork();
            NotifyNetworkVisibility();
            (DeleteNetworkCommand as RelayCommand)?.RaiseCanExecuteChanged();
            RefreshPreview();
        }
    }

    public FeJointNetworkModel SelectedJointModel
    {
        get => SelectedNetwork?.Model ?? FeJointNetworkModel.ParallelDeterministic;
        set
        {
            if (SelectedNetwork == null || SelectedNetwork.Model == value) return;
            SelectedNetwork.Model = value;
            if (value == FeJointNetworkModel.CrossJointed
                && Math.Abs(SelectedNetwork.Set2.DipDeg - SelectedNetwork.Set1.DipDeg) < 1e-6)
                SelectedNetwork.Set2.DipDeg = SelectedNetwork.Set1.DipDeg + 90;
            NotifyNetworkVisibility();
            RefreshPreview();
        }
    }

    public bool HasSelectedNetworkRegion => NetworkRegions.Any(r => r.IsSelected);

    public bool ShowParallelDeterministic => IsJointNetwork && SelectedJointModel == FeJointNetworkModel.ParallelDeterministic;
    public bool ShowParallelStatistical => IsJointNetwork && SelectedJointModel == FeJointNetworkModel.ParallelStatistical;
    public bool ShowCrossJointed => IsJointNetwork && SelectedJointModel == FeJointNetworkModel.CrossJointed;
    public bool ShowBaecher => IsJointNetwork && SelectedJointModel == FeJointNetworkModel.Baecher;
    public bool ShowVeneziano => IsJointNetwork && SelectedJointModel == FeJointNetworkModel.Veneziano;
    public bool ShowVoronoi => IsJointNetwork && SelectedJointModel == FeJointNetworkModel.Voronoi;
    public bool ShowNetworkSeed => IsJointNetwork && SelectedJointModel != FeJointNetworkModel.ParallelDeterministic
        || (ShowParallelDeterministic && (SelectedNetwork?.RandomLocation == true || SelectedNetwork?.Set1.RandomOffset == true));
    public bool ShowDeterministicLengthParams => ShowParallelDeterministic && SelectedNetwork is { Set1.InfiniteLength: false };
    public bool ShowStatisticalLengthParams => ShowParallelStatistical && SelectedNetwork is { Set1.InfiniteLength: false };
    public bool ShowVoronoiSiteCount => ShowVoronoi && SelectedNetwork?.VoronoiDensityMethod == FeJointVoronoiDensityMethod.SiteCount;
    public bool ShowVoronoiDensityValue => ShowVoronoi && SelectedNetwork?.VoronoiDensityMethod != FeJointVoronoiDensityMethod.SiteCount;
    public bool ShowVoronoiHexagonAxis => ShowVoronoi && SelectedNetwork?.VoronoiRegularity == FeJointVoronoiRegularity.RegularHexagon;
    public bool ShowThroughLocation => ShowParallelDeterministic && SelectedNetwork is { RandomLocation: false };

    private void InitNetworkCommands()
    {
        AddNetworkCommand = new RelayCommand(AddNetwork);
        DeleteNetworkCommand = new RelayCommand(DeleteNetwork, () => SelectedNetwork != null && JointNetworks.Count > 0);
        RandomizeNetworkCommand = new RelayCommand(RandomizeNetwork, () => SelectedNetwork != null);
        RebuildNetworkRegions();
        if (JointNetworks.Count > 0)
            SelectedNetwork = JointNetworks[0];
    }

    private void EnsureNetworkDefaults()
    {
        _geometry.EnsureDefaultJointPropertySet();
        if (JointNetworks.Count == 0)
            AddNetwork();
        else if (SelectedNetwork == null)
            SelectedNetwork = JointNetworks[0];
        RebuildNetworkRegions();
        if (SelectedNetwork != null && SelectedNetwork.TargetSurfaceNumbers.Count == 0)
            SelectDefaultRegions();
        NotifyNetworkVisibility();
    }

    private void AddNetwork()
    {
        var n = JointNetworks.Count + 1;
        var def = FeJointNetworkDefinition.CreateDefault($"Network {n}", SelectedPropertySet?.Id);
        def.EndCondition = EndCondition;
        def.InstallStage = InstallStage;
        foreach (var region in NetworkRegions.Where(r => r.IsSelected))
            def.TargetSurfaceNumbers.Add(region.SurfaceNumber);
        if (def.TargetSurfaceNumbers.Count == 0)
        {
            foreach (var region in NetworkRegions)
                def.TargetSurfaceNumbers.Add(region.SurfaceNumber);
        }

        _geometry.JointNetworks.Add(def);
        SelectedNetwork = def;
        Status = $"Added {def.Name}. Select a Joint Model, then the regions to apply.";
    }

    private void DeleteNetwork()
    {
        if (SelectedNetwork == null) return;
        var def = SelectedNetwork;
        _geometry.RemoveJointNetwork(def);
        SelectedNetwork = JointNetworks.FirstOrDefault();
        Status = $"Removed {def.Name} and its generated traces.";
        RefreshPreview();
    }

    private void RandomizeNetwork()
    {
        if (SelectedNetwork == null) return;
        SelectedNetwork.RandomSeed = unchecked(SelectedNetwork.RandomSeed * 1103515245 + 12345) & 0x7fffffff;
        if (SelectedNetwork.RandomSeed == 0)
            SelectedNetwork.RandomSeed = 1;
        Status = $"Randomized {SelectedNetwork.Name} (seed {SelectedNetwork.RandomSeed}).";
        RefreshPreview();
    }

    private void RebuildNetworkRegions()
    {
        foreach (var row in NetworkRegions)
            row.SelectionChanged -= OnRegionSelectionChanged;
        NetworkRegions.Clear();

        if (_geometry.GeometrySurfaces.Count > 0)
        {
            foreach (var surface in _geometry.GeometrySurfaces.OrderBy(s => s.SurfaceNumber))
            {
                var name = string.IsNullOrWhiteSpace(surface.Tag)
                    ? (string.IsNullOrWhiteSpace(surface.SurfaceName) ? $"S{surface.SurfaceNumber}" : surface.SurfaceName)
                    : surface.Tag;
                var mat = surface.AssignedMaterialId is int mid ? $" · Mat {mid}" : "";
                var selected = SelectedNetwork?.TargetSurfaceNumbers.Contains(surface.SurfaceNumber) == true;
                var row = new JointNetworkRegionChoice(surface.SurfaceNumber, $"S{surface.SurfaceNumber}  {name}{mat}", selected);
                row.SelectionChanged += OnRegionSelectionChanged;
                NetworkRegions.Add(row);
            }
        }
        else
        {
            var selected = SelectedNetwork?.TargetSurfaceNumbers.Contains(0) == true
                || SelectedNetwork == null
                || SelectedNetwork.TargetSurfaceNumbers.Count == 0;
            var row = new JointNetworkRegionChoice(0, "Soil boundary", selected);
            row.SelectionChanged += OnRegionSelectionChanged;
            NetworkRegions.Add(row);
        }

        OnPropertyChanged(nameof(NetworkRegions));
        OnPropertyChanged(nameof(HasSelectedNetworkRegion));
    }

    private void SelectDefaultRegions()
    {
        if (SelectedNetwork == null) return;
        _suppressNetworkPreview = true;
        try
        {
            SelectedNetwork.TargetSurfaceNumbers.Clear();
            foreach (var row in NetworkRegions)
            {
                row.IsSelected = true;
                if (!SelectedNetwork.TargetSurfaceNumbers.Contains(row.SurfaceNumber))
                    SelectedNetwork.TargetSurfaceNumbers.Add(row.SurfaceNumber);
            }
        }
        finally
        {
            _suppressNetworkPreview = false;
        }
    }

    private void SyncRegionChecksFromNetwork()
    {
        _suppressNetworkPreview = true;
        try
        {
            var targets = SelectedNetwork?.TargetSurfaceNumbers;
            foreach (var row in NetworkRegions)
                row.IsSelected = targets != null && targets.Contains(row.SurfaceNumber);
            if (SelectedNetwork != null && SelectedNetwork.TargetSurfaceNumbers.Count == 0 && NetworkRegions.Count > 0)
                SelectDefaultRegions();
        }
        finally
        {
            _suppressNetworkPreview = false;
        }
    }

    private void OnRegionSelectionChanged(object? sender, EventArgs e)
    {
        if (_suppressNetworkPreview || SelectedNetwork == null) return;
        SelectedNetwork.TargetSurfaceNumbers.Clear();
        foreach (var row in NetworkRegions.Where(r => r.IsSelected))
            SelectedNetwork.TargetSurfaceNumbers.Add(row.SurfaceNumber);
        OnPropertyChanged(nameof(HasSelectedNetworkRegion));
        RefreshPreview();
    }

    private void OnSelectedNetworkChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FeJointNetworkDefinition.Model)
            || e.PropertyName == nameof(FeJointNetworkDefinition.VoronoiDensityMethod)
            || e.PropertyName == nameof(FeJointNetworkDefinition.VoronoiRegularity)
            || e.PropertyName == nameof(FeJointSetParameters.InfiniteLength)
            || e.PropertyName == nameof(FeJointNetworkDefinition.RandomLocation))
            NotifyNetworkVisibility();
        if (!_suppressNetworkPreview)
            RefreshPreview();
    }

    private void NotifyNetworkVisibility()
    {
        OnPropertyChanged(nameof(SelectedJointModel));
        OnPropertyChanged(nameof(ShowParallelDeterministic));
        OnPropertyChanged(nameof(ShowParallelStatistical));
        OnPropertyChanged(nameof(ShowCrossJointed));
        OnPropertyChanged(nameof(ShowBaecher));
        OnPropertyChanged(nameof(ShowVeneziano));
        OnPropertyChanged(nameof(ShowVoronoi));
        OnPropertyChanged(nameof(ShowNetworkSeed));
        OnPropertyChanged(nameof(ShowDeterministicLengthParams));
        OnPropertyChanged(nameof(ShowStatisticalLengthParams));
        OnPropertyChanged(nameof(ShowVoronoiSiteCount));
        OnPropertyChanged(nameof(ShowVoronoiDensityValue));
        OnPropertyChanged(nameof(ShowVoronoiHexagonAxis));
        OnPropertyChanged(nameof(ShowThroughLocation));
        OnPropertyChanged(nameof(HasSelectedNetworkRegion));
    }

    public void CommitSelectedNetworkProperties()
    {
        if (SelectedNetwork == null) return;
        SelectedNetwork.PropertySetId = SelectedPropertySet?.Id;
        SelectedNetwork.CrossPropertySetId ??= SelectedPropertySet?.Id;
        SelectedNetwork.EndCondition = EndCondition;
        SelectedNetwork.InstallStage = InstallStage;
        if (string.IsNullOrWhiteSpace(SelectedNetwork.Name))
            SelectedNetwork.Name = Name;
    }

    private void RefreshNetworkPreview()
    {
        var regions = ResolveSelectedRegionPolygons();
        PreviewRegionsAxis = regions
            .Select(r => r.Select(p => new Point(p.X, p.Y)).ToArray())
            .ToList();
        OnPropertyChanged(nameof(PreviewRegionsAxis));

        if (SelectedNetwork == null)
        {
            _previewPolylines = Array.Empty<FeJointPolyline>();
            PreviewJointsAxis = Array.Empty<Point[]>();
            Status = "Add a joint network, then choose a Joint Model.";
            OnPropertyChanged(nameof(PreviewJointsAxis));
            OnPropertyChanged(nameof(PreviewPolylines));
            (ApplyCommand as RelayCommand)?.RaiseCanExecuteChanged();
            return;
        }

        if (regions.Count == 0)
        {
            _previewPolylines = Array.Empty<FeJointPolyline>();
            PreviewJointsAxis = Array.Empty<Point[]>();
            Status = "Select at least one region (surface) to generate a joint network.";
            OnPropertyChanged(nameof(PreviewJointsAxis));
            OnPropertyChanged(nameof(PreviewPolylines));
            (ApplyCommand as RelayCommand)?.RaiseCanExecuteChanged();
            return;
        }

        CommitSelectedNetworkProperties();
        var result = FeJointNetworkGenerator.Generate(SelectedNetwork, regions);
        _previewPolylines = result.Polylines;
        PreviewJointsAxis = _previewPolylines
            .Select(p => p.Vertices.Select(v => new Point(v.X, v.Y)).ToArray())
            .ToList();
        var modelName = FeJointNetworkModelNames.DisplayName(SelectedNetwork.Model);
        Status = result.WasCapped
            ? $"{SelectedNetwork.Name}: {modelName} reached the {FeJointNetworkGenerator.MaxSegments}-trace cap. Reduce density or intensity."
            : result.GeneratedCount == 0
                ? $"{SelectedNetwork.Name}: {modelName} produced no traces in the selected region(s). Adjust spacing, intensity, or seed."
                : $"{SelectedNetwork.Name}: {result.GeneratedCount} {modelName} trace(s) clipped to the selected region(s).";
        OnPropertyChanged(nameof(PreviewJointsAxis));
        OnPropertyChanged(nameof(PreviewPolylines));
        (ApplyCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    public IReadOnlyList<IReadOnlyList<(double X, double Y)>> ResolveSelectedRegionPolygons()
    {
        var selected = NetworkRegions.Where(r => r.IsSelected).Select(r => r.SurfaceNumber).ToList();
        return _geometry.ResolveJointNetworkRegions(selected);
    }
}
