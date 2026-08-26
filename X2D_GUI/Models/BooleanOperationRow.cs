using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GeotechSlopeApp.Models;

public enum BooleanOperationType
{
    Union,
    Subtract,
    Intersect
}

public partial class BooleanOperationRow : ObservableObject
{
    [ObservableProperty]
    private int _operationId;

    [ObservableProperty]
    private string _operationType = "Union";

    [ObservableProperty]
    private string? _regionAName;

    [ObservableProperty]
    private string? _regionBName;

    [ObservableProperty]
    private string? _resultRegionName;

    [ObservableProperty]
    private bool _isComplete;

    [ObservableProperty]
    private string? _validationStatus;

    [ObservableProperty]
    private double _regionAArea;

    [ObservableProperty]
    private double _regionBArea;

    [ObservableProperty]
    private double _resultArea;

    /// <summary>
    /// Construction stage number when this operation becomes visible (RS2/Plaxis style).
    /// 0 = legacy unset (treated as the first excavation stage when staged excavation is enabled).
    /// </summary>
    [ObservableProperty]
    private int _appliedInStageNumber;

    public BooleanOperationType TypeEnum { get; set; } = BooleanOperationType.Union;

    /// <summary>Surface numbers from the Surface grid (for reference / future recompute).</summary>
    public int RegionASurfaceNumber { get; set; }
    public int RegionBSurfaceNumber { get; set; }

    /// <summary>When true, <see cref="ParticipatingSurfaceNumbers"/> defines a multi-surface boolean (union, subtract chain, or intersect).</summary>
    public bool IsMultiRegion { get; set; }

    /// <summary>Ordered surface numbers for <see cref="IsMultiRegion"/> (list order matches checkbox list / surface number order).</summary>
    public List<int> ParticipatingSurfaceNumbers { get; set; } = new();

    public string RegionAAreaDisplay => RegionAArea > 0 ? $"{RegionAArea:F2}" : "-";
    public string RegionBAreaDisplay => RegionBArea > 0 ? $"{RegionBArea:F2}" : "-";
    public string ResultAreaDisplay => ResultArea > 0 ? $"{ResultArea:F2}" : "-";
}
