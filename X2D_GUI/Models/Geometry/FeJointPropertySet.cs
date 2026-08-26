using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace GeotechSlopeApp.Models.Geometry;

public enum FeJointSlipCriterion
{
    MohrCoulomb
}

public enum FeJointEndCondition
{
    BothClosed,
    BothOpen,
    FirstClosedLastOpen,
    FirstOpenLastClosed
}

public sealed class FeJointStageFactor : INotifyPropertyChanged
{
    private int _stageNumber = 1;
    private double _knFactor = 1;
    private double _ksFactor = 1;
    private double _cohesionFactor = 1;
    private double _frictionFactor = 1;

    public int StageNumber { get => _stageNumber; set => Set(ref _stageNumber, Math.Max(1, value)); }
    public double KnFactor { get => _knFactor; set => Set(ref _knFactor, value); }
    public double KsFactor { get => _ksFactor; set => Set(ref _ksFactor, value); }
    public double CohesionFactor { get => _cohesionFactor; set => Set(ref _cohesionFactor, value); }
    public double FrictionFactor { get => _frictionFactor; set => Set(ref _frictionFactor, value); }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public FeJointStageFactor Clone() => new()
    {
        StageNumber = StageNumber,
        KnFactor = KnFactor,
        KsFactor = KsFactor,
        CohesionFactor = CohesionFactor,
        FrictionFactor = FrictionFactor
    };
}

/// <summary>Reusable RS2-style joint constitutive set (library item).</summary>
public sealed class FeJointPropertySet : INotifyPropertyChanged
{
    private int _id = 1;
    private string _name = "Joint 1";
    private Color _colour = Color.FromRgb(0xEA, 0x58, 0x0C);
    private FeJointSlipCriterion _criterion = FeJointSlipCriterion.MohrCoulomb;
    private double _tensileStrengthKpa;
    private double _peakCohesionKpa;
    private double _peakFrictionDeg = 20;
    private bool _useResidualStrength;
    private double _residualTensileStrengthKpa;
    private double _residualCohesionKpa;
    private double _residualFrictionDeg = 20;
    private double _normalStiffnessKpaPerM = 1e5;
    private double _shearStiffnessKpaPerM = 1e4;
    private bool _useGroundwaterPorePressure = true;
    private bool _useAdditionalPressure;
    private double _additionalPressureKpa;
    private bool _applyPressureToLinerSideOnly;
    private bool _useStagedProperties;
    private int _allowSlipFromStage = 1;
    private bool _initialJointDeformation;

    public int Id { get => _id; set => Set(ref _id, value); }
    public string Name { get => _name; set => Set(ref _name, value ?? ""); }
    public Color Colour
    {
        get => _colour;
        set
        {
            if (_colour == value) return;
            _colour = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ColourBrush));
        }
    }
    public SolidColorBrush ColourBrush
    {
        get
        {
            var brush = new SolidColorBrush(Colour);
            if (brush.CanFreeze) brush.Freeze();
            return brush;
        }
    }

    public FeJointSlipCriterion Criterion { get => _criterion; set => Set(ref _criterion, value); }
    public double TensileStrengthKpa { get => _tensileStrengthKpa; set => Set(ref _tensileStrengthKpa, value); }
    public double PeakCohesionKpa { get => _peakCohesionKpa; set => Set(ref _peakCohesionKpa, value); }
    public double PeakFrictionDeg { get => _peakFrictionDeg; set => Set(ref _peakFrictionDeg, value); }
    public bool UseResidualStrength { get => _useResidualStrength; set => Set(ref _useResidualStrength, value); }
    public double ResidualTensileStrengthKpa { get => _residualTensileStrengthKpa; set => Set(ref _residualTensileStrengthKpa, value); }
    public double ResidualCohesionKpa { get => _residualCohesionKpa; set => Set(ref _residualCohesionKpa, value); }
    public double ResidualFrictionDeg { get => _residualFrictionDeg; set => Set(ref _residualFrictionDeg, value); }
    public double NormalStiffnessKpaPerM { get => _normalStiffnessKpaPerM; set => Set(ref _normalStiffnessKpaPerM, Math.Max(0, value)); }
    public double ShearStiffnessKpaPerM { get => _shearStiffnessKpaPerM; set => Set(ref _shearStiffnessKpaPerM, Math.Max(0, value)); }
    public bool UseGroundwaterPorePressure { get => _useGroundwaterPorePressure; set => Set(ref _useGroundwaterPorePressure, value); }
    public bool UseAdditionalPressure { get => _useAdditionalPressure; set => Set(ref _useAdditionalPressure, value); }
    public double AdditionalPressureKpa { get => _additionalPressureKpa; set => Set(ref _additionalPressureKpa, value); }
    public bool ApplyPressureToLinerSideOnly { get => _applyPressureToLinerSideOnly; set => Set(ref _applyPressureToLinerSideOnly, value); }
    public bool UseStagedProperties { get => _useStagedProperties; set => Set(ref _useStagedProperties, value); }
    public int AllowSlipFromStage { get => _allowSlipFromStage; set => Set(ref _allowSlipFromStage, Math.Max(1, value)); }
    public bool InitialJointDeformation { get => _initialJointDeformation; set => Set(ref _initialJointDeformation, value); }
    public ObservableCollection<FeJointStageFactor> StageFactors { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public static FeJointPropertySet CreateDefault(int id = 1) => new()
    {
        Id = id,
        Name = $"Joint {id}"
    };

    public FeJointPropertySet Clone(int? newId = null)
    {
        var copy = new FeJointPropertySet
        {
            Id = newId ?? Id,
            Name = newId is int nid ? $"Joint {nid}" : Name,
            Colour = Colour,
            Criterion = Criterion,
            TensileStrengthKpa = TensileStrengthKpa,
            PeakCohesionKpa = PeakCohesionKpa,
            PeakFrictionDeg = PeakFrictionDeg,
            UseResidualStrength = UseResidualStrength,
            ResidualTensileStrengthKpa = ResidualTensileStrengthKpa,
            ResidualCohesionKpa = ResidualCohesionKpa,
            ResidualFrictionDeg = ResidualFrictionDeg,
            NormalStiffnessKpaPerM = NormalStiffnessKpaPerM,
            ShearStiffnessKpaPerM = ShearStiffnessKpaPerM,
            UseGroundwaterPorePressure = UseGroundwaterPorePressure,
            UseAdditionalPressure = UseAdditionalPressure,
            AdditionalPressureKpa = AdditionalPressureKpa,
            ApplyPressureToLinerSideOnly = ApplyPressureToLinerSideOnly,
            UseStagedProperties = UseStagedProperties,
            AllowSlipFromStage = AllowSlipFromStage,
            InitialJointDeformation = InitialJointDeformation
        };
        foreach (var f in StageFactors)
            copy.StageFactors.Add(f.Clone());
        return copy;
    }

    public void CopyFrom(FeJointPropertySet src)
    {
        Id = src.Id;
        Name = src.Name;
        Colour = src.Colour;
        Criterion = src.Criterion;
        TensileStrengthKpa = src.TensileStrengthKpa;
        PeakCohesionKpa = src.PeakCohesionKpa;
        PeakFrictionDeg = src.PeakFrictionDeg;
        UseResidualStrength = src.UseResidualStrength;
        ResidualTensileStrengthKpa = src.ResidualTensileStrengthKpa;
        ResidualCohesionKpa = src.ResidualCohesionKpa;
        ResidualFrictionDeg = src.ResidualFrictionDeg;
        NormalStiffnessKpaPerM = src.NormalStiffnessKpaPerM;
        ShearStiffnessKpaPerM = src.ShearStiffnessKpaPerM;
        UseGroundwaterPorePressure = src.UseGroundwaterPorePressure;
        UseAdditionalPressure = src.UseAdditionalPressure;
        AdditionalPressureKpa = src.AdditionalPressureKpa;
        ApplyPressureToLinerSideOnly = src.ApplyPressureToLinerSideOnly;
        UseStagedProperties = src.UseStagedProperties;
        AllowSlipFromStage = src.AllowSlipFromStage;
        InitialJointDeformation = src.InitialJointDeformation;
        StageFactors.Clear();
        foreach (var f in src.StageFactors)
            StageFactors.Add(f.Clone());
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
