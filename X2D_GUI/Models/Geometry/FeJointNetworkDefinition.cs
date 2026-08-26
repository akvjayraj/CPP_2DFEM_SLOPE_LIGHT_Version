using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GeotechSlopeApp.Models.Geometry;

/// <summary>Named joint network that can target one or more model regions.</summary>
public sealed class FeJointNetworkDefinition : INotifyPropertyChanged
{
    private string _name = "Network 1";
    private FeJointNetworkModel _model = FeJointNetworkModel.ParallelDeterministic;
    private int? _propertySetId;
    private int? _crossPropertySetId;
    private FeJointEndCondition _endCondition = FeJointEndCondition.BothClosed;
    private int _installStage = 1;
    private int _randomSeed = 1;
    private bool _randomLocation = true;
    private double _throughX;
    private double _throughY;
    private double _intensity = 0.4;
    private FeJointVoronoiDensityMethod _voronoiDensityMethod = FeJointVoronoiDensityMethod.Density;
    private FeJointVoronoiRegularity _voronoiRegularity = FeJointVoronoiRegularity.Irregular;
    private bool _hexagonHorizontal = true;
    private int _siteCount = 24;
    private double _anisotropyX = 1;
    private double _anisotropyY = 1;

    public FeJointNetworkDefinition()
    {
        Set1 = new FeJointSetParameters { DipDeg = -45 };
        Set2 = new FeJointSetParameters { DipDeg = 45 };
        Orientation = FeJointStatisticalDistribution.Constant(-45);
        Length = new FeJointStatisticalDistribution
        {
            Kind = FeJointDistributionKind.NegativeExponential,
            Mean = 4,
            StdDev = 1,
            AutoMinMax3Std = true
        };
        Persistence = FeJointStatisticalDistribution.Normal(0.7, 0.1);
        Hook(Set1);
        Hook(Set2);
        Hook(Orientation);
        Hook(Length);
        Hook(Persistence);
        TargetSurfaceNumbers.CollectionChanged += (_, _) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TargetSurfaceNumbers)));
    }

    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get => _name; set => Set(ref _name, value ?? ""); }
    public FeJointNetworkModel Model { get => _model; set => Set(ref _model, value); }
    public ObservableCollection<int> TargetSurfaceNumbers { get; } = new();
    public int? PropertySetId { get => _propertySetId; set => Set(ref _propertySetId, value); }
    public int? CrossPropertySetId { get => _crossPropertySetId; set => Set(ref _crossPropertySetId, value); }
    public FeJointEndCondition EndCondition { get => _endCondition; set => Set(ref _endCondition, value); }
    public int InstallStage { get => _installStage; set => Set(ref _installStage, Math.Max(1, value)); }
    public int RandomSeed { get => _randomSeed; set => Set(ref _randomSeed, value); }
    public bool RandomLocation { get => _randomLocation; set => Set(ref _randomLocation, value); }
    public double ThroughX { get => _throughX; set => Set(ref _throughX, value); }
    public double ThroughY { get => _throughY; set => Set(ref _throughY, value); }

    public FeJointSetParameters Set1 { get; }
    public FeJointSetParameters Set2 { get; }

    /// <summary>Baecher P21 (traces / area), Veneziano line intensity, or Voronoi cell density.</summary>
    public double Intensity { get => _intensity; set => Set(ref _intensity, Math.Max(0, value)); }
    public FeJointStatisticalDistribution Orientation { get; }
    public FeJointStatisticalDistribution Length { get; }
    public FeJointStatisticalDistribution Persistence { get; }

    public FeJointVoronoiDensityMethod VoronoiDensityMethod
    {
        get => _voronoiDensityMethod;
        set => Set(ref _voronoiDensityMethod, value);
    }

    public FeJointVoronoiRegularity VoronoiRegularity
    {
        get => _voronoiRegularity;
        set => Set(ref _voronoiRegularity, value);
    }

    public bool HexagonHorizontal { get => _hexagonHorizontal; set => Set(ref _hexagonHorizontal, value); }
    public int SiteCount { get => _siteCount; set => Set(ref _siteCount, Math.Max(3, value)); }
    public double AnisotropyX { get => _anisotropyX; set => Set(ref _anisotropyX, Math.Max(0.05, value)); }
    public double AnisotropyY { get => _anisotropyY; set => Set(ref _anisotropyY, Math.Max(0.05, value)); }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static FeJointNetworkDefinition CreateDefault(string? name = null, int? propertySetId = null)
    {
        var def = new FeJointNetworkDefinition
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Network 1" : name.Trim(),
            PropertySetId = propertySetId,
            CrossPropertySetId = propertySetId
        };
        return def;
    }

    public FeJointNetworkDefinition Clone()
    {
        var copy = new FeJointNetworkDefinition
        {
            Id = Id,
            Name = Name,
            Model = Model,
            PropertySetId = PropertySetId,
            CrossPropertySetId = CrossPropertySetId,
            EndCondition = EndCondition,
            InstallStage = InstallStage,
            RandomSeed = RandomSeed,
            RandomLocation = RandomLocation,
            ThroughX = ThroughX,
            ThroughY = ThroughY,
            Intensity = Intensity,
            VoronoiDensityMethod = VoronoiDensityMethod,
            VoronoiRegularity = VoronoiRegularity,
            HexagonHorizontal = HexagonHorizontal,
            SiteCount = SiteCount,
            AnisotropyX = AnisotropyX,
            AnisotropyY = AnisotropyY
        };
        copy.TargetSurfaceNumbers.Clear();
        foreach (var n in TargetSurfaceNumbers)
            copy.TargetSurfaceNumbers.Add(n);
        copy.Set1.CopyFrom(Set1);
        copy.Set2.CopyFrom(Set2);
        copy.Orientation.CopyFrom(Orientation);
        copy.Length.CopyFrom(Length);
        copy.Persistence.CopyFrom(Persistence);
        return copy;
    }

    public void CopyFrom(FeJointNetworkDefinition src)
    {
        Name = src.Name;
        Model = src.Model;
        PropertySetId = src.PropertySetId;
        CrossPropertySetId = src.CrossPropertySetId;
        EndCondition = src.EndCondition;
        InstallStage = src.InstallStage;
        RandomSeed = src.RandomSeed;
        RandomLocation = src.RandomLocation;
        ThroughX = src.ThroughX;
        ThroughY = src.ThroughY;
        Intensity = src.Intensity;
        VoronoiDensityMethod = src.VoronoiDensityMethod;
        VoronoiRegularity = src.VoronoiRegularity;
        HexagonHorizontal = src.HexagonHorizontal;
        SiteCount = src.SiteCount;
        AnisotropyX = src.AnisotropyX;
        AnisotropyY = src.AnisotropyY;
        TargetSurfaceNumbers.Clear();
        foreach (var n in src.TargetSurfaceNumbers)
            TargetSurfaceNumbers.Add(n);
        Set1.CopyFrom(src.Set1);
        Set2.CopyFrom(src.Set2);
        Orientation.CopyFrom(src.Orientation);
        Length.CopyFrom(src.Length);
        Persistence.CopyFrom(src.Persistence);
    }

    private void Hook(INotifyPropertyChanged child)
        => child.PropertyChanged += (_, e) => PropertyChanged?.Invoke(this, e);

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
