using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Services;

namespace GeotechSlopeApp.ViewModels.Workspace;

/// <summary>
/// FILE-tab Backstage. Shows the live project and the persisted MRU list — no demo models.
/// </summary>
public sealed class FileWorkspaceViewModel : INotifyPropertyChanged
{
    private readonly MainWindowViewModel _host;
    private string _searchText = "";
    private bool _isListView = true;
    private RecentProjectEntry? _selectedRecent;

    public FileWorkspaceViewModel(MainWindowViewModel host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        FilteredRecent = new ObservableCollection<RecentProjectEntry>();
        _host.RecentProjects.Entries.CollectionChanged += OnRecentChanged;
        _host.ProjectSettingsVM.Project.PropertyChanged += (_, _) => Refresh();
        _host.ProjectSettingsVM.PropertyChanged += (_, _) => Refresh();
        _host.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainWindowViewModel.CurrentXfemProjectPath)
                or nameof(MainWindowViewModel.CurrentStage))
                Refresh();
        };
        Refresh();
    }

    public ObservableCollection<RecentProjectEntry> FilteredRecent { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            var next = value ?? "";
            if (_searchText == next) return;
            _searchText = next;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    public bool IsListView
    {
        get => _isListView;
        set
        {
            if (_isListView == value) return;
            _isListView = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsGridView));
        }
    }

    public bool IsGridView
    {
        get => !_isListView;
        set { if (value) IsListView = false; }
    }

    public RecentProjectEntry? SelectedRecent
    {
        get => _selectedRecent;
        set
        {
            if (ReferenceEquals(_selectedRecent, value)) return;
            _selectedRecent = value;
            OnPropertyChanged();
        }
    }

    public ICommand ShowListCommand => new RelayCommand(() => IsListView = true);
    public ICommand ShowGridCommand => new RelayCommand(() => IsListView = false);

    public string ProjectTitle =>
        string.IsNullOrWhiteSpace(_host.ProjectSettingsVM.Project.ProjectName)
            ? "Untitled project"
            : _host.ProjectSettingsVM.Project.ProjectName;

    public string ProjectAuthor =>
        string.IsNullOrWhiteSpace(_host.ProjectSettingsVM.Project.Author)
            ? "—"
            : _host.ProjectSettingsVM.Project.Author;

    public string ProjectCreatedOn => _host.ProjectSettingsVM.Project.CreatedOn.ToString("yyyy-MM-dd");

    public string ProjectPath =>
        string.IsNullOrWhiteSpace(_host.CurrentXfemProjectPath)
            ? "Not saved"
            : _host.CurrentXfemProjectPath;

    public string ProjectUnits => _host.ProjectSettingsVM.Project.Units ?? ProjectUnitSystem.Si;

    public string ProjectDimension =>
        _host.ProjectSettingsVM.IsAxisymmetricIdealization ? "2D Axisymmetric" : "2D Plane Strain";

    public string ProjectAnalysisType => _host.ProjectSettingsVM.ResolvedAnalysisKind.ToString();

    public string CurrentStageText
    {
        get
        {
            if (_host.CurrentStage is int stage)
                return stage.ToString();
            var n = _host.ProjectSettingsVM.NumberOfStages;
            return n > 0 ? $"1 of {n}" : "—";
        }
    }

    public string ProjectStatus
    {
        get
        {
            var msg = _host.FEAnalysisVM.StatusMessage;
            if (!string.IsNullOrWhiteSpace(msg))
                return msg;
            return _host.FEAnalysisVM.IsSolving ? "Running" : "Ready";
        }
    }

    public string LastModifiedText
    {
        get
        {
            var path = _host.CurrentXfemProjectPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return "Unsaved";
            return File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm");
        }
    }

    public int NodeCount => _host.MeshVM.DiscretizationNodes.Count;
    public int ElementCount => _host.MeshVM.MeshElements.Count;
    public int PointCount => _host.GeometryDefinitionVM.GeometryVertexRows.Count;
    public int LineCount => _host.GeometryDefinitionVM.GeometryLineSegments.Count;
    public int SurfaceCount => _host.GeometryDefinitionVM.GeometrySurfaces.Count;
    public int MaterialCount => _host.MaterialPropertiesVM.Materials.Count;
    public int StageCount => Math.Max(_host.ProjectSettingsVM.NumberOfStages, _host.StagedConstructionVM.Stages.Count);

    public string PreviewHint =>
        "2D model preview lives on the Geometry canvas. This engine has no 3D project thumbnail.";

    public string RecoveryHint =>
        "No recovery files. Autosave and backup are not in this FEM engine.";

    public bool HasRecent => FilteredRecent.Count > 0;

    public void Refresh()
    {
        ApplyFilter();
        OnPropertyChanged(nameof(ProjectTitle));
        OnPropertyChanged(nameof(ProjectAuthor));
        OnPropertyChanged(nameof(ProjectCreatedOn));
        OnPropertyChanged(nameof(ProjectPath));
        OnPropertyChanged(nameof(ProjectUnits));
        OnPropertyChanged(nameof(ProjectDimension));
        OnPropertyChanged(nameof(ProjectAnalysisType));
        OnPropertyChanged(nameof(CurrentStageText));
        OnPropertyChanged(nameof(ProjectStatus));
        OnPropertyChanged(nameof(LastModifiedText));
        OnPropertyChanged(nameof(NodeCount));
        OnPropertyChanged(nameof(ElementCount));
        OnPropertyChanged(nameof(PointCount));
        OnPropertyChanged(nameof(LineCount));
        OnPropertyChanged(nameof(SurfaceCount));
        OnPropertyChanged(nameof(MaterialCount));
        OnPropertyChanged(nameof(StageCount));
    }

    private void OnRecentChanged(object? sender, NotifyCollectionChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var needle = _searchText.Trim();
        FilteredRecent.Clear();
        foreach (var entry in _host.RecentProjects.Entries)
        {
            if (needle.Length == 0
                || entry.FileName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || entry.FilePath.Contains(needle, StringComparison.OrdinalIgnoreCase))
                FilteredRecent.Add(entry);
        }

        OnPropertyChanged(nameof(HasRecent));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
