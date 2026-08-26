using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace GeotechSlopeApp.Models;

/// <summary>One BC glyph on the geometry canvas (pixel space).</summary>
public sealed class BcCanvasMarker : INotifyPropertyChanged
{
    private BcSymbolKind _kind;
    private double _x;
    private double _y;
    private int _nodeId;
    private string _bcType = "";
    private string _analysisCategory = "";

    public BcCanvasMarker(
        BcSymbolKind kind,
        Point canvasPoint,
        int nodeId,
        string bcType,
        string analysisCategory)
    {
        _kind = kind;
        _x = canvasPoint.X;
        _y = canvasPoint.Y;
        _nodeId = nodeId;
        _bcType = bcType ?? "";
        _analysisCategory = string.IsNullOrWhiteSpace(analysisCategory)
            ? BcSymbolCatalog.AnalysisFamily(kind)
            : analysisCategory;
    }

    public BcSymbolKind Kind
    {
        get => _kind;
        set { if (_kind == value) return; _kind = value; OnPropertyChanged(); OnPropertyChanged(nameof(ToolTipText)); }
    }

    public double X
    {
        get => _x;
        set { if (Math.Abs(_x - value) < 1e-9) return; _x = value; OnPropertyChanged(); }
    }

    public double Y
    {
        get => _y;
        set { if (Math.Abs(_y - value) < 1e-9) return; _y = value; OnPropertyChanged(); }
    }

    public int NodeId
    {
        get => _nodeId;
        set { if (_nodeId == value) return; _nodeId = value; OnPropertyChanged(); OnPropertyChanged(nameof(ToolTipText)); }
    }

    public string BCType
    {
        get => _bcType;
        set { _bcType = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(ToolTipText)); }
    }

    public string AnalysisCategory
    {
        get => _analysisCategory;
        set { _analysisCategory = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(ToolTipText)); }
    }

    public string ToolTipText =>
        $"Node {_nodeId} — {_bcType} ({_analysisCategory})";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
