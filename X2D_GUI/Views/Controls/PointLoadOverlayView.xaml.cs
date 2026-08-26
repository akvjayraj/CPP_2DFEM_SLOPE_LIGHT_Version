using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace GeotechSlopeApp.Views.Controls;

public partial class PointLoadOverlayView : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(PointLoadOverlayView),
            new PropertyMetadata(null));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public PointLoadOverlayView()
    {
        InitializeComponent();
    }
}
