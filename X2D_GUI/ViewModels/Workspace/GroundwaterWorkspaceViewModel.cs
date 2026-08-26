using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.ViewModels.Workspace;

/// <summary>
/// GROUNDWATER-ribbon chrome: explorer, properties, table.
/// Hosts existing GroundwaterViewModel; does not invent seepage types.
/// </summary>
public sealed class GroundwaterWorkspaceViewModel : INotifyPropertyChanged
{
    public const string GlyphFolder = "\uE8B7";
    public const string GlyphWater = "\uE90B";
    public const string GlyphChevronExpanded = "\uE70D";
    public const string GlyphChevronCollapsed = "\uE70E";

    public const string PayloadWaterSurfaces = "water-surfaces";
    public const string PayloadWaterHeads = "water-heads";
    public const string PayloadPressureGrids = "pressure-grids";

    private readonly MainWindowViewModel _host;
    private ProjectExplorerNode? _selectedNode;
    private bool _isExplorerExpanded = true;
    private bool _isSummaryExpanded = true;
    private bool _isTableExpanded = true;
    private bool _isPropertiesExpanded;
    private bool _isViewOptionsExpanded = true;
    private bool _refreshing;

    public GroundwaterWorkspaceViewModel(MainWindowViewModel host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        Roots = new ObservableCollection<ProjectExplorerNode>();
        SelectNodeCommand = new RelayCommand<ProjectExplorerNode>(OnSelect);
        ToggleExplorerCommand = new RelayCommand(() => IsExplorerExpanded = !IsExplorerExpanded);
        ToggleSummaryCommand = new RelayCommand(() => IsSummaryExpanded = !IsSummaryExpanded);
        ToggleTableCommand = new RelayCommand(() => IsTableExpanded = !IsTableExpanded);
        TogglePropertiesCommand = new RelayCommand(() => IsPropertiesExpanded = !IsPropertiesExpanded);
        ToggleViewOptionsCommand = new RelayCommand(() => IsViewOptionsExpanded = !IsViewOptionsExpanded);
        ClosePropertiesCommand = new RelayCommand(() => IsPropertiesExpanded = false);

        Groundwater.WaterHeads.CollectionChanged += OnHeadsChanged;
        Groundwater.WaterPressureGrids.CollectionChanged += (_, _) => Refresh();
        if (Groundwater.GroundwaterSurfPoints != null)
            Groundwater.GroundwaterSurfPoints.CollectionChanged += OnSurfChanged;
        Groundwater.PropertyChanged += OnGroundwaterPropertyChanged;
        Refresh();
    }

    public GroundwaterViewModel Groundwater => _host.GroundwaterVM;
    public ObservableCollection<ProjectExplorerNode> Roots { get; }
    public ObservableCollection<WaterHeadModel> WaterHeads => Groundwater.WaterHeads;
    public ObservableCollection<GroundwaterSurfPoint>? SurfPoints => Groundwater.GroundwaterSurfPoints;

    public ICommand SelectNodeCommand { get; }
    public ICommand ToggleExplorerCommand { get; }
    public ICommand ToggleSummaryCommand { get; }
    public ICommand ToggleTableCommand { get; }
    public ICommand TogglePropertiesCommand { get; }
    public ICommand ToggleViewOptionsCommand { get; }
    public ICommand ClosePropertiesCommand { get; }

    public ICommand AddWaterTableCommand => Groundwater.AddWaterTableCommand;
    public ICommand StartPolylineDrawCommand => Groundwater.StartPolylineDrawCommand;
    public ICommand ClearGroundwaterCommand => Groundwater.ClearGroundwaterCommand;
    public ICommand AddWaterHeadCommand => Groundwater.AddWaterHeadCommand;
    public ICommand RemoveWaterHeadCommand => Groundwater.RemoveWaterHeadCommand;
    public ICommand OpenWaterPressureGridDialogCommand => Groundwater.OpenWaterPressureGridDialogCommand;

    public bool IsExplorerExpanded
    {
        get => _isExplorerExpanded;
        set
        {
            if (_isExplorerExpanded == value) return;
            _isExplorerExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExplorerChevronGlyph));
            _host.NotifyWorkspaceChromeChanged();
        }
    }

    public bool IsSummaryExpanded
    {
        get => _isSummaryExpanded;
        set
        {
            if (_isSummaryExpanded == value) return;
            _isSummaryExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SummaryChevronGlyph));
        }
    }

    public bool IsTableExpanded
    {
        get => _isTableExpanded;
        set
        {
            if (_isTableExpanded == value) return;
            _isTableExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TableChevronGlyph));
            _host.NotifyWorkspaceChromeChanged();
        }
    }

    public bool IsPropertiesExpanded
    {
        get => _isPropertiesExpanded;
        set
        {
            if (_isPropertiesExpanded == value) return;
            _isPropertiesExpanded = value;
            OnPropertyChanged();
            _host.NotifyWorkspaceChromeChanged();
        }
    }

    public bool IsViewOptionsExpanded
    {
        get => _isViewOptionsExpanded;
        set
        {
            if (_isViewOptionsExpanded == value) return;
            _isViewOptionsExpanded = value;
            OnPropertyChanged();
        }
    }

    public string ExplorerChevronGlyph => IsExplorerExpanded ? GlyphChevronExpanded : GlyphChevronCollapsed;
    public string SummaryChevronGlyph => IsSummaryExpanded ? GlyphChevronExpanded : GlyphChevronCollapsed;
    public string TableChevronGlyph => IsTableExpanded ? GlyphChevronExpanded : GlyphChevronCollapsed;

    public ProjectExplorerNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (_selectedNode == value) return;
            _selectedNode = value;
            OnPropertyChanged();
            OnSelect(value);
        }
    }

    public int WaterSurfaceCount => SurfPoints?.Count ?? 0;
    public int WaterHeadCount => WaterHeads.Count;
    public int PressureGridCount => Groundwater.WaterPressureGrids.Count;
    public string SummaryText =>
        $"{WaterSurfaceCount} surface points · {WaterHeadCount} water heads · {PressureGridCount} grids";

    public string StatusDimension =>
        _host.ProjectSettingsVM.IsAxisymmetricIdealization ? "2D Axisymmetric" : "2D Plane Strain";

    public void Refresh()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            Roots.Clear();
            var root = new ProjectExplorerNode
            {
                Kind = ExplorerNodeKind.Folder,
                Title = "Groundwater",
                Glyph = GlyphFolder,
                TabIndex = 6,
                IsExpanded = true
            };
            root.Children.Add(Folder("Water Surfaces", PayloadWaterSurfaces, WaterSurfaceCount, SurfPoints));
            root.Children.Add(Folder("Water Heads", PayloadWaterHeads, WaterHeadCount, WaterHeads));
            root.Children.Add(new ProjectExplorerNode
            {
                Kind = ExplorerNodeKind.Folder,
                Title = "Pressure Grids",
                Glyph = GlyphFolder,
                TabIndex = 6,
                Count = PressureGridCount,
                Payload = PayloadPressureGrids,
                IsExpanded = true
            });
            Roots.Add(root);
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(WaterSurfaceCount));
            OnPropertyChanged(nameof(WaterHeadCount));
            OnPropertyChanged(nameof(PressureGridCount));
        }
        finally
        {
            _refreshing = false;
        }
    }

    private ProjectExplorerNode Folder(string title, string payload, int count, IEnumerable<object>? children)
    {
        var folder = new ProjectExplorerNode
        {
            Kind = ExplorerNodeKind.Folder,
            Title = title,
            Glyph = GlyphFolder,
            TabIndex = 6,
            Count = count,
            Payload = payload,
            IsExpanded = true
        };
        if (children == null)
            return folder;
        var i = 1;
        foreach (var child in children)
        {
            var name = child switch
            {
                WaterHeadModel head => string.IsNullOrWhiteSpace(head.Name) ? $"Head {i}" : head.Name,
                GroundwaterSurfPoint pt => $"P{pt.Index}",
                _ => child.ToString() ?? $"Item {i}"
            };
            folder.Children.Add(new ProjectExplorerNode
            {
                Kind = ExplorerNodeKind.GroundwaterItem,
                Title = name,
                Glyph = GlyphWater,
                TabIndex = 6,
                Payload = child
            });
            i++;
        }
        return folder;
    }

    private void OnSelect(ProjectExplorerNode? node)
    {
        if (node?.Payload is WaterHeadModel head)
            Groundwater.SelectedWaterHead = head;
        else if (node?.Payload is GroundwaterSurfPoint pt)
            Groundwater.SelectedSurfPoint = pt;
    }

    private void OnHeadsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();
    private void OnSurfChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();

    private void OnGroundwaterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GroundwaterViewModel.SelectedWaterHead)
            or nameof(GroundwaterViewModel.SelectedSurfPoint)
            or nameof(GroundwaterViewModel.WaterTableInput)
            or nameof(GroundwaterViewModel.Method))
            OnPropertyChanged(nameof(SummaryText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
