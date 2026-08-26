using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.ViewModels;

public partial class GeometryDefinitionViewModel
{
    private LoadConditionsViewModel? _loadOverlaySource;
    private bool _showLineLoadOverlaysOnCanvas;
    private bool _showPointLoadOverlaysOnCanvas;

    /// <summary>Source for RS2D-style load overlays (wired from MainWindow).</summary>
    public void SetLoadOverlaySource(LoadConditionsViewModel? source)
    {
        if (_loadOverlaySource != null)
            _loadOverlaySource.PropertyChanged -= OnLoadOverlaySourcePropertyChanged;

        _loadOverlaySource = source;

        if (_loadOverlaySource != null)
            _loadOverlaySource.PropertyChanged += OnLoadOverlaySourcePropertyChanged;

        OnPropertyChanged(nameof(LineLoadOverlays));
        OnPropertyChanged(nameof(PointLoadOverlays));
    }

    public bool ShowLineLoadOverlaysOnCanvas
    {
        get => _showLineLoadOverlaysOnCanvas;
        set
        {
            if (_showLineLoadOverlaysOnCanvas == value) return;
            _showLineLoadOverlaysOnCanvas = value;
            OnPropertyChanged();
        }
    }

    public bool ShowPointLoadOverlaysOnCanvas
    {
        get => _showPointLoadOverlaysOnCanvas;
        set
        {
            if (_showPointLoadOverlaysOnCanvas == value) return;
            _showPointLoadOverlaysOnCanvas = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<LineLoadOverlayDisplay> LineLoadOverlays =>
        _loadOverlaySource?.LineLoadOverlays ?? EmptyLineLoadOverlays;

    public ObservableCollection<PointLoadOverlayDisplay> PointLoadOverlays =>
        _loadOverlaySource?.PointLoadOverlays ?? EmptyPointLoadOverlays;

    private static readonly ObservableCollection<LineLoadOverlayDisplay> EmptyLineLoadOverlays = new();
    private static readonly ObservableCollection<PointLoadOverlayDisplay> EmptyPointLoadOverlays = new();

    private void OnLoadOverlaySourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or "" or nameof(LoadConditionsViewModel.LineLoadOverlays))
            OnPropertyChanged(nameof(LineLoadOverlays));
        if (e.PropertyName is null or "" or nameof(LoadConditionsViewModel.PointLoadOverlays))
            OnPropertyChanged(nameof(PointLoadOverlays));
    }
}

