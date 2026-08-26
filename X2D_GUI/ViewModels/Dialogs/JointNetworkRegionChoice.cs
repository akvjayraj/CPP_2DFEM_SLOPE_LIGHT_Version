using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GeotechSlopeApp.ViewModels.Dialogs;

public sealed class JointNetworkRegionChoice : INotifyPropertyChanged
{
    private bool _isSelected;

    public JointNetworkRegionChoice(int surfaceNumber, string displayName, bool isSelected = false)
    {
        SurfaceNumber = surfaceNumber;
        DisplayName = displayName ?? $"S{surfaceNumber}";
        _isSelected = isSelected;
    }

    public int SurfaceNumber { get; }
    public string DisplayName { get; }
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? SelectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class FeJointNetworkModelChoice
{
    public FeJointNetworkModelChoice(Models.Geometry.FeJointNetworkModel model, string displayName)
    {
        Model = model;
        DisplayName = displayName;
    }

    public Models.Geometry.FeJointNetworkModel Model { get; }
    public string DisplayName { get; }
}

public sealed class FeJointDistributionKindChoice
{
    public FeJointDistributionKindChoice(Models.Geometry.FeJointDistributionKind kind, string displayName)
    {
        Kind = kind;
        DisplayName = displayName;
    }

    public Models.Geometry.FeJointDistributionKind Kind { get; }
    public string DisplayName { get; }
}

public sealed class FeJointVoronoiRegularityChoice
{
    public FeJointVoronoiRegularityChoice(Models.Geometry.FeJointVoronoiRegularity regularity, string displayName)
    {
        Regularity = regularity;
        DisplayName = displayName;
    }

    public Models.Geometry.FeJointVoronoiRegularity Regularity { get; }
    public string DisplayName { get; }
}

public sealed class FeJointVoronoiDensityMethodChoice
{
    public FeJointVoronoiDensityMethodChoice(Models.Geometry.FeJointVoronoiDensityMethod method, string displayName)
    {
        Method = method;
        DisplayName = displayName;
    }

    public Models.Geometry.FeJointVoronoiDensityMethod Method { get; }
    public string DisplayName { get; }
}
