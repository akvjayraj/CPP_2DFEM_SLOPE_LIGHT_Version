using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using GeotechSlopeApp.Services.Help;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Views.Dialogs;

public partial class HelpWindow : Window
{
    private static HelpWindow? _openInstance;
    private readonly HelpBrowserViewModel _vm;
    private bool _suppressSelection;

    public HelpWindow()
    {
        InitializeComponent();
        _vm = new HelpBrowserViewModel();
        DataContext = _vm;
        _vm.ScrollToElement = ScrollToElement;
        Closed += (_, _) =>
        {
            if (ReferenceEquals(_openInstance, this))
                _openInstance = null;
        };
    }

    /// <summary>Opens or focuses the help browser at the landing page of the manual.</summary>
    public static void ShowManual(Window? owner)
        => ShowOrActivate(owner, vm => vm.OpenHome());

    /// <summary>Opens or focuses the help browser at the topic for a main-window tab.</summary>
    public static void ShowContextHelp(Window? owner, int tabIndex)
        => ShowOrActivate(owner, vm => vm.OpenTopicForTab(tabIndex));

    /// <summary>Opens or focuses the help browser at a named topic id from <c>toc.json</c>.</summary>
    public static void ShowTopic(Window? owner, string topicId)
        => ShowOrActivate(owner, vm => vm.OpenTopicById(topicId));

    private static void ShowOrActivate(Window? owner, Action<HelpBrowserViewModel> open)
    {
        if (_openInstance != null)
        {
            if (_openInstance.WindowState == WindowState.Minimized)
                _openInstance.WindowState = WindowState.Normal;
            open(_openInstance._vm);
            _openInstance.Activate();
            return;
        }

        var win = new HelpWindow
        {
            Owner = owner,
            Title = "User Manual"
        };
        if (!string.IsNullOrWhiteSpace(win._vm.Title))
            win.Title = win._vm.Title;

        open(win._vm);
        _openInstance = win;
        win.Show();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ContentsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_suppressSelection) return;
        if (e.NewValue is HelpTopicNode node)
            _vm.OpenNode(node);
    }

    private void SearchResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SearchResultsList.SelectedItem is HelpSearchHit hit)
        {
            _vm.OpenSearchHit(hit);
            SearchResultsList.SelectedItem = null;
        }
    }

    private void ScrollToElement(TextElement? element)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (element != null)
            {
                element.BringIntoView();
                return;
            }

            // FlowDocumentScrollViewer has no ScrollToHome; scroll its internal ScrollViewer.
            if (Viewer.Template?.FindName("PART_ContentHost", Viewer) is ScrollViewer host)
                host.ScrollToHome();
        });
    }

    /// <summary>
    /// Keeps the TreeView selection in sync when navigation comes from links, history or search
    /// rather than from a click in the tree itself.
    /// </summary>
    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(HelpBrowserViewModel.CurrentTopic))
                SyncTreeFromCurrentTopic();
        };
        SyncTreeFromCurrentTopic();
    }

    private void SyncTreeFromCurrentTopic()
    {
        if (_vm.CurrentTopic == null) return;
        _suppressSelection = true;
        try
        {
            // Selection is driven by HelpTopicNode.IsSelected; expand path is already handled in the VM.
        }
        finally
        {
            _suppressSelection = false;
        }
    }
}
