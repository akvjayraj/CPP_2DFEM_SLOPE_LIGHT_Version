using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace GeotechSlopeApp.Views.Tabs;

public partial class MaterialPropertiesView : UserControl
{
    public static readonly DependencyProperty ShowCanvasProperty =
        DependencyProperty.Register(
            nameof(ShowCanvas),
            typeof(bool),
            typeof(MaterialPropertiesView),
            new PropertyMetadata(true));

    public bool ShowCanvas
    {
        get => (bool)GetValue(ShowCanvasProperty);
        set => SetValue(ShowCanvasProperty, value);
    }

    public static readonly DependencyProperty ShowSubtabHeadersProperty =
        DependencyProperty.Register(
            nameof(ShowSubtabHeaders),
            typeof(bool),
            typeof(MaterialPropertiesView),
            new PropertyMetadata(true));

    /// <summary>
    /// When false, the Materials panel shows only the selected tool content
    /// (Explorer Tools switch Layering / Project materials / Assignment).
    /// </summary>
    public bool ShowSubtabHeaders
    {
        get => (bool)GetValue(ShowSubtabHeadersProperty);
        set => SetValue(ShowSubtabHeadersProperty, value);
    }

    public static readonly DependencyProperty FocusedSubtabIndexProperty =
        DependencyProperty.Register(
            nameof(FocusedSubtabIndex),
            typeof(int),
            typeof(MaterialPropertiesView),
            new PropertyMetadata(-1, OnFocusedSubtabIndexChanged));

    public int FocusedSubtabIndex
    {
        get => (int)GetValue(FocusedSubtabIndexProperty);
        set => SetValue(FocusedSubtabIndexProperty, value);
    }

    public MaterialPropertiesView()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyFocusedSubtab();
    }

    private static void OnFocusedSubtabIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MaterialPropertiesView view && view.IsLoaded)
            view.ApplyFocusedSubtab();
    }

    private void ApplyFocusedSubtab()
    {
        if (FocusedSubtabIndex < 0)
            return;

        BindingOperations.ClearBinding(MaterialSubtabs, TabControl.SelectedIndexProperty);
        LayeringControlsTab.Visibility = FocusedSubtabIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        ProjectMaterialsTab.Visibility = FocusedSubtabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        LayerMaterialAssignmentTab.Visibility = FocusedSubtabIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        MaterialSubtabs.SelectedIndex = FocusedSubtabIndex;
    }
}
