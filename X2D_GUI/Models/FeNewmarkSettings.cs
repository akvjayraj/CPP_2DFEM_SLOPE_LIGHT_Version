using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GeotechSlopeApp.Models;

public enum FeNewmarkAccelerationUnits
{
    G,
    MetresPerSecondSquared
}

/// <summary>Newmark sliding-block inputs. Mutual alternative to pseudo-static when enabled.</summary>
public sealed class FeNewmarkSettings : INotifyPropertyChanged
{
    private bool _isEnabled;
    private double _yieldAcceleration = 0.1;
    private bool _estimateKyFromSrf = true;
    private double _pgaG = 0.3;
    private double _magnitude = 7.0;
    private double _ariasIntensity = 1.0;
    private double _slidingMassPeriodTs = 0.4;
    private double _spectralAccelerationG = 0.35;
    private double _fundamentalPeriodT0 = 0.5;
    private bool _useJibson = true;
    private bool _useJibsonMagnitude;
    private bool _useJibsonArias;
    private bool _useAmbraseys = true;
    private bool _useMakdisiSeed;
    private bool _useBrayTravasarou;
    private bool _useTimeHistory;
    private string _timeHistoryPath = "";
    private FeNewmarkAccelerationUnits _timeHistoryUnits = FeNewmarkAccelerationUnits.G;
    private double _timeHistoryScale = 1.0;
    private bool _twoWaySliding;

    public bool IsEnabled { get => _isEnabled; set => Set(ref _isEnabled, value); }
    public double YieldAcceleration { get => _yieldAcceleration; set => Set(ref _yieldAcceleration, value); }
    public bool EstimateKyFromSrf { get => _estimateKyFromSrf; set => Set(ref _estimateKyFromSrf, value); }
    public double PgaG { get => _pgaG; set => Set(ref _pgaG, value); }
    public double Magnitude { get => _magnitude; set => Set(ref _magnitude, value); }
    public double AriasIntensity { get => _ariasIntensity; set => Set(ref _ariasIntensity, value); }
    public double SlidingMassPeriodTs { get => _slidingMassPeriodTs; set => Set(ref _slidingMassPeriodTs, value); }
    public double SpectralAccelerationG { get => _spectralAccelerationG; set => Set(ref _spectralAccelerationG, value); }
    public double FundamentalPeriodT0 { get => _fundamentalPeriodT0; set => Set(ref _fundamentalPeriodT0, value); }
    public bool UseJibson { get => _useJibson; set => Set(ref _useJibson, value); }
    public bool UseJibsonMagnitude { get => _useJibsonMagnitude; set => Set(ref _useJibsonMagnitude, value); }
    public bool UseJibsonArias { get => _useJibsonArias; set => Set(ref _useJibsonArias, value); }
    public bool UseAmbraseys { get => _useAmbraseys; set => Set(ref _useAmbraseys, value); }
    public bool UseMakdisiSeed { get => _useMakdisiSeed; set => Set(ref _useMakdisiSeed, value); }
    public bool UseBrayTravasarou { get => _useBrayTravasarou; set => Set(ref _useBrayTravasarou, value); }
    public bool UseTimeHistory { get => _useTimeHistory; set => Set(ref _useTimeHistory, value); }
    public string TimeHistoryPath { get => _timeHistoryPath; set => Set(ref _timeHistoryPath, value ?? ""); }
    public FeNewmarkAccelerationUnits TimeHistoryUnits { get => _timeHistoryUnits; set => Set(ref _timeHistoryUnits, value); }
    public double TimeHistoryScale { get => _timeHistoryScale; set => Set(ref _timeHistoryScale, value); }
    public bool TwoWaySliding { get => _twoWaySliding; set => Set(ref _twoWaySliding, value); }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class FeNewmarkModelResult
{
    public string ModelName { get; init; } = "";
    public double DisplacementCm { get; init; } = double.NaN;
    public double DisplacementLowerCm { get; init; } = double.NaN;
    public double DisplacementUpperCm { get; init; } = double.NaN;
    public string Notes { get; init; } = "";
    public bool HasDisplacement => double.IsFinite(DisplacementCm);
}

public sealed class FeNewmarkResult
{
    public double YieldAcceleration { get; init; } = double.NaN;
    public double StaticSrf { get; init; } = double.NaN;
    public string KySource { get; init; } = "";
    public IReadOnlyList<FeNewmarkModelResult> ModelResults { get; init; } = Array.Empty<FeNewmarkModelResult>();
    public double TimeHistoryDisplacementCm { get; init; } = double.NaN;
    public int TimeHistoryExceedanceCount { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public bool HasYieldAcceleration => double.IsFinite(YieldAcceleration);
}
