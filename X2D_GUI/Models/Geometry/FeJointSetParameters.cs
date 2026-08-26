using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GeotechSlopeApp.Models.Geometry;

/// <summary>One joint set: inclination, spacing, length/persistence, and first-joint offset.</summary>
public sealed class FeJointSetParameters : INotifyPropertyChanged
{
    private double _dipDeg = -45;
    private double _dipStdDeg;
    private bool _infiniteLength = true;
    private bool _randomOffset;
    private double _offsetLength;
    private double _firstJointOffset;
    private bool _useStatisticalSpacing;

    public FeJointSetParameters()
    {
        Spacing = FeJointStatisticalDistribution.Constant(2);
        Length = FeJointStatisticalDistribution.Constant(4);
        Persistence = FeJointStatisticalDistribution.Constant(0.7);
        Hook(Spacing);
        Hook(Length);
        Hook(Persistence);
    }

    public double DipDeg { get => _dipDeg; set => Set(ref _dipDeg, value); }
    public double DipStdDeg { get => _dipStdDeg; set => Set(ref _dipStdDeg, Math.Max(0, value)); }
    public FeJointStatisticalDistribution Spacing { get; }
    public FeJointStatisticalDistribution Length { get; }
    public FeJointStatisticalDistribution Persistence { get; }
    public bool InfiniteLength { get => _infiniteLength; set => Set(ref _infiniteLength, value); }
    public bool RandomOffset { get => _randomOffset; set => Set(ref _randomOffset, value); }
    public double OffsetLength { get => _offsetLength; set => Set(ref _offsetLength, value); }
    public double FirstJointOffset { get => _firstJointOffset; set => Set(ref _firstJointOffset, value); }
    public bool UseStatisticalSpacing
    {
        get => _useStatisticalSpacing;
        set => Set(ref _useStatisticalSpacing, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public FeJointSetParameters Clone()
    {
        var copy = new FeJointSetParameters
        {
            DipDeg = DipDeg,
            DipStdDeg = DipStdDeg,
            InfiniteLength = InfiniteLength,
            RandomOffset = RandomOffset,
            OffsetLength = OffsetLength,
            FirstJointOffset = FirstJointOffset,
            UseStatisticalSpacing = UseStatisticalSpacing
        };
        copy.Spacing.CopyFrom(Spacing);
        copy.Length.CopyFrom(Length);
        copy.Persistence.CopyFrom(Persistence);
        return copy;
    }

    public void CopyFrom(FeJointSetParameters src)
    {
        DipDeg = src.DipDeg;
        DipStdDeg = src.DipStdDeg;
        InfiniteLength = src.InfiniteLength;
        RandomOffset = src.RandomOffset;
        OffsetLength = src.OffsetLength;
        FirstJointOffset = src.FirstJointOffset;
        UseStatisticalSpacing = src.UseStatisticalSpacing;
        Spacing.CopyFrom(src.Spacing);
        Length.CopyFrom(src.Length);
        Persistence.CopyFrom(src.Persistence);
    }

    private void Hook(FeJointStatisticalDistribution dist)
        => dist.PropertyChanged += (_, e) => PropertyChanged?.Invoke(this, e);

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
