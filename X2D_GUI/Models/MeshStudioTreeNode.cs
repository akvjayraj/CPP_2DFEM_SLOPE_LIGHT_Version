using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GeotechSlopeApp.Models;

/// <summary>Logical kind of a Mesh Studio tree node — selected by the inspector pane to pick a card template.</summary>
public enum MeshStudioNodeKind
{
    Project,
    Geometry,
    MeshControls,
    RegionsRoot,
    Region,
    Quality
}

/// <summary>
/// A row in the Mesh Studio master TreeView. The right-hand inspector pane swaps cards
/// based on <see cref="Kind"/> (and, for region nodes, the surface number carried in <see cref="Tag"/>).
/// </summary>
public class MeshStudioTreeNode : INotifyPropertyChanged
{
    private bool _isExpanded = true;
    private bool _isSelected;
    private string _title = "";
    private string _subtitle = "";

    public MeshStudioNodeKind Kind { get; init; }

    public string Title
    {
        get => _title;
        set { if (_title == value) return; _title = value ?? ""; OnPropertyChanged(); }
    }

    public string Subtitle
    {
        get => _subtitle;
        set { if (_subtitle == value) return; _subtitle = value ?? ""; OnPropertyChanged(); }
    }

    /// <summary>Free-form payload — for region nodes this is the surface number (int).</summary>
    public object? Tag { get; init; }

    public ObservableCollection<MeshStudioTreeNode> Children { get; } = new();

    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded == value) return; _isExpanded = value; OnPropertyChanged(); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
