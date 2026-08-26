using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GeotechSlopeApp.ViewModels;

/// <summary>Left-panel Analysis tree: top-level analysis category with detailed type children.</summary>
public class AnalysisCategoryNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;
    private bool _isOn;

    public AnalysisCategoryNode(string name)
    {
        Name = name;
    }

    public string Name { get; }

    /// <summary>Category nodes are always enabled; present so the shared TreeViewItem style can bind it.</summary>
    public bool IsEnabled => true;

    public string? DisabledReason => null;

    public ObservableCollection<AnalysisTypeNode> Types { get; } = new();

    /// <summary>
    /// Project Settings → Analysis category master switch (ON). When false, type options and
    /// analysis functions for this heading are disabled / not offered.
    /// </summary>
    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn == value) return;
            _isOn = value;
            OnPropertyChanged();
        }
    }

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
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Left-panel Analysis tree: selectable detailed analysis type (child of a category).</summary>
public class AnalysisTypeNode : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isExpanded;
    private bool _isEnabled = true;
    private string? _disabledReason;

    public AnalysisTypeNode(string category, string name, bool isEnabled = true, string? disabledReason = null)
    {
        Category = category;
        Name = name;
        _isEnabled = isEnabled;
        _disabledReason = disabledReason;
    }

    public string Category { get; }

    public string Name { get; }

    /// <summary>False for future leaves (Sequential, Fully coupled). Tree item is visible but not selectable.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set { if (_isEnabled == value) return; _isEnabled = value; OnPropertyChanged(); }
    }

    public string? DisabledReason
    {
        get => _disabledReason;
        set { if (_disabledReason == value) return; _disabledReason = value; OnPropertyChanged(); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); }
    }

    /// <summary>Leaf nodes never expand; present so a shared TreeViewItem style can bind it.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded == value) return; _isExpanded = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
