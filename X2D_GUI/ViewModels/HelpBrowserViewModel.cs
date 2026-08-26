using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Documents;
using System.Windows.Input;
using GeotechSlopeApp.Services.Help;

namespace GeotechSlopeApp.ViewModels;

/// <summary>One node of the contents tree, with the selection state the TreeView binds to.</summary>
public sealed class HelpTopicNode : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isExpanded;

    public HelpTopicNode(HelpTopic topic, HelpTopicNode? parent)
    {
        Topic = topic;
        Parent = parent;
        Children = new ObservableCollection<HelpTopicNode>(
            topic.Children.Select(c => new HelpTopicNode(c, this)));
    }

    public HelpTopic Topic { get; }
    public HelpTopicNode? Parent { get; }
    public string Title => Topic.Title;
    public ObservableCollection<HelpTopicNode> Children { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    public void ExpandAncestors()
    {
        for (var node = Parent; node != null; node = node.Parent)
            node.IsExpanded = true;
    }

    public IEnumerable<HelpTopicNode> Flatten()
        => new[] { this }.Concat(Children.SelectMany(c => c.Flatten()));

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Drives the help browser: the contents tree, the rendered page, full-text search and history.
/// </summary>
public sealed class HelpBrowserViewModel : INotifyPropertyChanged
{
    private readonly HelpTableOfContents _toc;
    private readonly HelpSearchIndex _search;
    private readonly List<(string TopicId, string Anchor)> _history = new();
    private int _historyPosition = -1;
    private bool _navigating;

    private FlowDocument? _document;
    private HelpTopic? _currentTopic;
    private string _searchText = string.Empty;
    private string _breadcrumb = string.Empty;

    public HelpBrowserViewModel()
    {
        _toc = HelpTableOfContents.Load();
        _search = HelpSearchIndex.Build(_toc);
        Nodes = new ObservableCollection<HelpTopicNode>(
            _toc.Roots.Select(r => new HelpTopicNode(r, null)));
        foreach (var node in Nodes)
            node.IsExpanded = true;

        BackCommand = new RelayCommand(GoBack, () => CanGoBack);
        ForwardCommand = new RelayCommand(GoForward, () => CanGoForward);
        HomeCommand = new RelayCommand(() => OpenHome());
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
    }

    public string Title => _toc.Title;

    public bool IsAvailable => _toc.IsAvailable;

    public ObservableCollection<HelpTopicNode> Nodes { get; }

    public ObservableCollection<HelpSearchHit> SearchResults { get; } = new();

    public ICommand BackCommand { get; }
    public ICommand ForwardCommand { get; }
    public ICommand HomeCommand { get; }
    public ICommand ClearSearchCommand { get; }

    /// <summary>Set by the view so the renderer can scroll a heading into view.</summary>
    public Action<TextElement?>? ScrollToElement { get; set; }

    public FlowDocument? Document
    {
        get => _document;
        private set { _document = value; OnPropertyChanged(); }
    }

    public HelpTopic? CurrentTopic
    {
        get => _currentTopic;
        private set { _currentTopic = value; OnPropertyChanged(); }
    }

    public string Breadcrumb
    {
        get => _breadcrumb;
        private set { _breadcrumb = value; OnPropertyChanged(); }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value) return;
            _searchText = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSearchActive));
            RefreshSearchResults();
        }
    }

    public bool IsSearchActive => _searchText.Trim().Length >= 2;

    public bool CanGoBack => _historyPosition > 0;

    public bool CanGoForward => _historyPosition >= 0 && _historyPosition < _history.Count - 1;

    /// <summary>Opens the landing page of the manual.</summary>
    public void OpenHome() => Open(_toc.Home());

    public void OpenTopicById(string? topicId)
        => Open(_toc.FindById(topicId) ?? _toc.Home());

    /// <summary>Opens the page documenting a main-window tab, for context-sensitive help.</summary>
    public void OpenTopicForTab(int tabIndex)
        => Open(_toc.FindByTabIndex(tabIndex) ?? _toc.Home());

    public void OpenSearchHit(HelpSearchHit? hit)
    {
        if (hit != null) Open(hit.Topic);
    }

    public void OpenNode(HelpTopicNode? node)
    {
        if (node == null) return;
        var topic = node.Topic.HasContent ? node.Topic : node.Topic.FirstWithContent();
        if (topic != null && !ReferenceEquals(topic, CurrentTopic)) Open(topic);
    }

    private void Open(HelpTopic? topic, string anchor = "", bool recordHistory = true)
    {
        if (topic == null)
        {
            Document = HelpMarkdownRenderer.Render(
                "# User manual not found\n\nThe `Help` folder was not installed next to the application.\n",
                new HelpMarkdownRenderer.Context(string.Empty, string.Empty, null)).Document;
            return;
        }

        var context = new HelpMarkdownRenderer.Context(_toc.Directory, topic.RelativePath, Navigate);
        var rendered = HelpMarkdownRenderer.Render(_toc.ReadTopic(topic), context);

        CurrentTopic = topic;
        Document = rendered.Document;
        Breadcrumb = string.Join("  >  ", topic.BreadcrumbTitles);
        SyncTreeSelection(topic);

        if (recordHistory) PushHistory(topic, anchor);

        ScrollToElement?.Invoke(
            anchor.Length > 0 && rendered.Anchors.TryGetValue(anchor, out var element) ? element : null);
    }

    /// <summary>Follows a link from inside a page: another topic, a heading, or an external site.</summary>
    private void Navigate(HelpLinkTarget target)
    {
        if (target == null) return;

        if (target.IsExternal)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = target.ExternalUrl, UseShellExecute = true });
            }
            catch (Exception)
            {
                // A blocked or missing browser must not take the help window down.
            }
            return;
        }

        var path = target.TopicPath.Length > 0 ? target.TopicPath : CurrentTopic?.RelativePath;
        var topic = _toc.FindByPath(path);
        if (topic == null)
        {
            Document = HelpMarkdownRenderer.Render(
                $"# Page not found\n\nThis page links to `{target.TopicPath}`, which is not part of the manual.\n",
                new HelpMarkdownRenderer.Context(_toc.Directory, CurrentTopic?.RelativePath ?? string.Empty, null))
                .Document;
            return;
        }

        Open(topic, target.Anchor);
    }

    private void PushHistory(HelpTopic topic, string anchor)
    {
        if (_navigating) return;
        if (_historyPosition >= 0 && _historyPosition < _history.Count
            && _history[_historyPosition].TopicId == topic.Id
            && _history[_historyPosition].Anchor == anchor)
            return;

        if (_historyPosition < _history.Count - 1)
            _history.RemoveRange(_historyPosition + 1, _history.Count - _historyPosition - 1);

        _history.Add((topic.Id, anchor));
        _historyPosition = _history.Count - 1;
        RaiseHistoryChanged();
    }

    private void GoBack()
    {
        if (!CanGoBack) return;
        _historyPosition--;
        ReplayHistory();
    }

    private void GoForward()
    {
        if (!CanGoForward) return;
        _historyPosition++;
        ReplayHistory();
    }

    private void ReplayHistory()
    {
        var (topicId, anchor) = _history[_historyPosition];
        _navigating = true;
        try
        {
            Open(_toc.FindById(topicId), anchor, recordHistory: false);
        }
        finally
        {
            _navigating = false;
        }
        RaiseHistoryChanged();
    }

    private void RaiseHistoryChanged()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        CommandManager.InvalidateRequerySuggested();
    }

    private void SyncTreeSelection(HelpTopic topic)
    {
        HelpTopicNode? match = null;
        foreach (var node in Nodes.SelectMany(n => n.Flatten()))
        {
            var selected = ReferenceEquals(node.Topic, topic);
            if (selected) match = node;
            if (node.IsSelected != selected)
                node.IsSelected = selected;
        }
        match?.ExpandAncestors();
    }

    private void RefreshSearchResults()
    {
        SearchResults.Clear();
        if (!IsSearchActive) return;
        foreach (var hit in _search.Search(_searchText))
            SearchResults.Add(hit);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
