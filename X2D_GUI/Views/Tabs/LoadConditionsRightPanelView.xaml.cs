using System.Windows;
using System.Windows.Controls;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;
using GeotechSlopeApp.Views.Dialogs;

namespace GeotechSlopeApp.Views.Tabs;

public partial class LoadConditionsRightPanelView : UserControl
{
    public static readonly DependencyProperty FocusedSectionProperty =
        DependencyProperty.Register(
            nameof(FocusedSection),
            typeof(LoadConditionsFocusedSection),
            typeof(LoadConditionsRightPanelView),
            new PropertyMetadata(LoadConditionsFocusedSection.All, OnFocusedSectionChanged));

    public LoadConditionsFocusedSection FocusedSection
    {
        get => (LoadConditionsFocusedSection)GetValue(FocusedSectionProperty);
        set => SetValue(FocusedSectionProperty, value);
    }

    public LoadConditionsRightPanelView()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyFocusedSection();
    }

    private static void OnFocusedSectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LoadConditionsRightPanelView view && view.IsLoaded)
            view.ApplyFocusedSection();
    }

    private void ApplyFocusedSection()
    {
        ScopeBanner.Visibility = FocusedSection == LoadConditionsFocusedSection.All
            ? Visibility.Visible
            : Visibility.Collapsed;
        FieldStressExpander.Visibility = FocusedSection is LoadConditionsFocusedSection.All or LoadConditionsFocusedSection.FieldStress
            ? Visibility.Visible
            : Visibility.Collapsed;
        SeismicLoadExpander.Visibility = FocusedSection is LoadConditionsFocusedSection.All or LoadConditionsFocusedSection.SeismicLoad
            ? Visibility.Visible
            : Visibility.Collapsed;
        LoadsDataGrid.Visibility = FocusedSection is LoadConditionsFocusedSection.All or LoadConditionsFocusedSection.Loads
            ? Visibility.Visible
            : Visibility.Collapsed;
        LoadActionsPanel.Visibility = FocusedSection is LoadConditionsFocusedSection.All or LoadConditionsFocusedSection.Loads
            ? Visibility.Visible
            : Visibility.Collapsed;

        ScopeBannerRow.Height = FocusedSection == LoadConditionsFocusedSection.All ? GridLength.Auto : new GridLength(0);
        FieldStressRow.Height = FieldStressExpander.Visibility == Visibility.Visible ? GridLength.Auto : new GridLength(0);
        SeismicLoadRow.Height = SeismicLoadExpander.Visibility == Visibility.Visible ? GridLength.Auto : new GridLength(0);
        LoadsGridRow.Height = LoadsDataGrid.Visibility == Visibility.Visible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        LoadsGridRow.MinHeight = LoadsDataGrid.Visibility == Visibility.Visible ? 80 : 0;
        LoadDetailsRow.Height = LoadDetailPanel.Visibility == Visibility.Visible ? GridLength.Auto : new GridLength(0);
        LoadActionsRow.Height = LoadActionsPanel.Visibility == Visibility.Visible ? GridLength.Auto : new GridLength(0);

        if (FocusedSection == LoadConditionsFocusedSection.FieldStress)
            FieldStressExpander.IsExpanded = true;
        if (FocusedSection == LoadConditionsFocusedSection.SeismicLoad)
            SeismicLoadExpander.IsExpanded = true;
    }

    private void OnAdvancedOverridesClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LoadConditionsViewModel vm) return;
        var dialog = new FieldStressOverridesDialog(vm.FieldStress)
        {
            Owner = Window.GetWindow(this)
        };
        dialog.ShowDialog();
    }

    private void OnLoadsDataGridBeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.Item is not LoadModel load || !load.IsPointLoad)
            return;

        string header = e.Column.Header?.ToString() ?? "";
        if (header is "End Mag." or "End X (axis)" or "End Y (axis)")
            e.Cancel = true;
    }

    private void OnStageFactorsClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Stage Load Factors allow you to scale this load's magnitude independently per construction stage.\n\nThis feature is planned for a future release.",
            "Stage Factors",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}

public enum LoadConditionsFocusedSection
{
    All,
    FieldStress,
    SeismicLoad,
    Loads
}
