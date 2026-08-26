using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GeotechSlopeApp.Models;

public enum FeDrawdownMethod
{
    None,
    DuncanThreeStage,
    CorpsTwoStage,
    InstantaneousDrained
}

public sealed class FeDrawdownWaterPoint : INotifyPropertyChanged
{
    private double _x;
    private double _y;
    public double X { get => _x; set => Set(ref _x, value); }
    public double Y { get => _y; set => Set(ref _y, value); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>Staged reservoir drawdown: pre/post water surfaces and procedure.</summary>
public sealed class FeDrawdownSettings : INotifyPropertyChanged
{
    private bool _isEnabled;
    private FeDrawdownMethod _method = FeDrawdownMethod.DuncanThreeStage;
    private double _preReservoirLevel;
    private double _postReservoirLevel;
    private bool _takeLowerOfUndrainedAndDrained = true;

    public bool IsEnabled { get => _isEnabled; set => Set(ref _isEnabled, value); }
    public FeDrawdownMethod Method
    {
        get => _method;
        set
        {
            if (_method == value) return;
            _method = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDuncanThreeStage));
            OnPropertyChanged(nameof(IsCorpsTwoStage));
            OnPropertyChanged(nameof(IsInstantaneousDrained));
        }
    }

    public bool IsDuncanThreeStage
    {
        get => Method == FeDrawdownMethod.DuncanThreeStage;
        set { if (value) Method = FeDrawdownMethod.DuncanThreeStage; }
    }

    public bool IsCorpsTwoStage
    {
        get => Method == FeDrawdownMethod.CorpsTwoStage;
        set { if (value) Method = FeDrawdownMethod.CorpsTwoStage; }
    }

    public bool IsInstantaneousDrained
    {
        get => Method == FeDrawdownMethod.InstantaneousDrained;
        set { if (value) Method = FeDrawdownMethod.InstantaneousDrained; }
    }

    public ObservableCollection<FeDrawdownWaterPoint> PreDrawdownSurface { get; } = new();
    public ObservableCollection<FeDrawdownWaterPoint> PostDrawdownSurface { get; } = new();
    public double PreReservoirLevel { get => _preReservoirLevel; set => Set(ref _preReservoirLevel, value); }
    public double PostReservoirLevel { get => _postReservoirLevel; set => Set(ref _postReservoirLevel, value); }
    public bool TakeLowerOfUndrainedAndDrained
    {
        get => _takeLowerOfUndrainedAndDrained;
        set => Set(ref _takeLowerOfUndrainedAndDrained, value);
    }

    public bool HasBothSurfaces => PreDrawdownSurface.Count >= 2 && PostDrawdownSurface.Count >= 2;
    public bool IsActive => IsEnabled && Method != FeDrawdownMethod.None && HasBothSurfaces;

    public GroundwaterModel BuildStageModel(GroundwaterModel template, bool postDrawdown)
    {
        var clone = new GroundwaterModel
        {
            IsEnabled = true,
            Method = GroundwaterMethod.Hydrostatic,
            GammaW = template.GammaW,
            GammaSat = new List<double>(template.GammaSat),
            StaticSettings = template.StaticSettings
        };
        var src = postDrawdown ? PostDrawdownSurface : PreDrawdownSurface;
        clone.SurfPoints = new List<(double X, double Y)>();
        foreach (var p in src)
            clone.SurfPoints.Add((p.X, p.Y));
        return clone;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
